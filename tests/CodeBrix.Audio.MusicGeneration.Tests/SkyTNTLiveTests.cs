using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Rendition;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The tests that really load the SkyTNT model and really generate music. Opt in by setting
/// CODEBRIX_AUDIO_MUSICGEN_SKYTNT_BUNDLE to the folder holding the bundle; skipped otherwise.
/// </summary>
/// <remarks>
/// <para>
/// EVERY ONE OF THEM IS SHORT - a few dozen events - because a model writes music at processor
/// speed and a suite is run over and over. They make no sound: the audible one is in
/// <see cref="SkyTNTAudibleTests"/>, behind a second gate.
/// </para>
/// <para>
/// THE MODEL IS LOADED ONCE FOR THE WHOLE CLASS and released when it is finished with, because
/// loading it is the expensive part and this class's job is what the model does, not how often it
/// can be loaded. The two tests that are ABOUT loading build generators of their own.
/// </para>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class SkyTNTLiveTests : IClassFixture<SkyTNTLoadedModel>
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;
    private const int ShortPass = 60;

    /// <summary>The name the shared generator is registered and asked for under.</summary>
    public const string Name = "SkyTNTLive";

    private static readonly TimeSpan HowLongToWaitForMusic = TimeSpan.FromMinutes(3.0);

    private readonly SkyTNTMusicGenerator generator;

    /// <summary>Starts from freshly built built-ins, over the model the class loaded once.</summary>
    /// <param name="loaded">The model, loaded once for the whole class.</param>
    public SkyTNTLiveTests(SkyTNTLoadedModel loaded)
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();

        generator = loaded.Generator;
    }

    [Fact]
    public async Task the_model_loads_from_a_folder_and_says_so()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Act
        await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        generator.IsLoaded.Should().BeTrue();
        generator.LoadedThreadCount.Should().Be(Options().InferenceThreadCount);
    }

    [Fact]
    public async Task the_model_loads_from_a_map_of_files_too()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange - the route a store that keeps its files under digests takes
        using var byPath = new SkyTNTMusicGenerator("SkyTNTByPath", SkyTNTModelBundle.Files(),
            Options());

        //Act
        await byPath.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        byPath.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task releasing_gives_the_memory_back_and_the_next_request_loads_it_again()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange
        using var reloaded = new SkyTNTMusicGenerator("SkyTNTReload", SkyTNTModelBundle.Directory,
            Options());

        await reloaded.PreloadAsync(TestContext.Current.CancellationToken);

        //Act
        reloaded.Release();
        var afterRelease = reloaded.IsLoaded;

        await Pull(reloaded, Fresh());

        //Assert
        afterRelease.Should().BeFalse();
        reloaded.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task a_request_naming_a_different_thread_count_reloads_the_model_rather_than_ignoring_it()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange
        using var threaded = new SkyTNTMusicGenerator("SkyTNTThreads", SkyTNTModelBundle.Directory,
            Options());

        await threaded.PreloadAsync(TestContext.Current.CancellationToken);
        var loadedFirst = threaded.LoadedThreadCount;

        //Act
        var request = Fresh();
        request.InferenceThreadCount = 2;

        await Pull(threaded, request);

        //Assert
        loadedFirst.Should().Be(Options().InferenceThreadCount);
        threaded.LoadedThreadCount.Should().Be(2);
    }

    [Fact]
    public async Task a_short_pass_writes_music_at_segment_relative_ticks()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Act
        var produced = await Pull(generator, Fresh());

        //Assert
        var events = produced.Where(item => item.HasEvent).ToArray();
        events.Should().NotBeEmpty();
        events.Select(item => item.Event.AbsoluteTime).Should().OnlyContain(tick => tick >= 0L);
        events.Select(item => item.Event).OfType<NoteOnEvent>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task every_channel_is_one_to_sixteen()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Act
        var produced = await Pull(generator, Fresh());

        //Assert - the model counts 0 to 15 and this is the one place that is corrected
        produced.Where(item => item.HasEvent)
            .Select(item => item.Event)
            .OfType<NoteOnEvent>()
            .Select(note => note.Channel)
            .Should().OnlyContain(channel => channel >= 1 && channel <= 16);
    }

    [Fact]
    public async Task no_event_ever_arrives_at_or_before_a_tick_already_announced_as_settled()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Act
        var produced = await Pull(generator, Fresh());

        //Assert - the model promises a HORIZON, which is weaker than a settled tick, so this is
        //the fence that the horizon was turned into a settled tick correctly
        var settled = GeneratedMusicEvent.NothingSettled;

        foreach (var item in produced)
        {
            if (item.HasEvent)
            {
                item.Event.AbsoluteTime.Should().BeGreaterThan(settled);
            }

            if (item.HasSettled && item.SettledThroughTick > settled)
            {
                settled = item.SettledThroughTick;
            }
        }
    }

    [Fact]
    public async Task a_pass_ends_on_a_bar_line()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Act
        var produced = await Pull(generator, Fresh());

        //Assert - the last word is a settled tick alone, and it is a whole number of bars
        var last = produced[produced.Count - 1];

        last.HasEvent.Should().BeFalse();
        last.SettledThroughTick.Should().BeGreaterThan(0L);
        (last.SettledThroughTick % (4L * Resolution)).Should().Be(0L);
        produced.Where(item => item.HasEvent)
            .Should().OnlyContain(item => item.Event.AbsoluteTime < last.SettledThroughTick);
    }

    [Fact]
    public async Task a_seeded_generation_is_reproducible()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Act
        var first = await Pull(generator, Fresh());
        var second = await Pull(generator, Fresh());

        //Assert
        Describe(first).Should().Equal(Describe(second));
    }

    [Fact]
    public async Task a_different_seed_writes_different_music()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange
        var other = Fresh();
        other.Seed = 20260921;

        //Act
        var first = await Pull(generator, Fresh());
        var second = await Pull(generator, other);

        //Assert
        Describe(first).Should().NotEqual(Describe(second));
    }

    [Fact]
    public async Task a_continuation_starts_after_the_tail_and_re_emits_none_of_it()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange - a first pass, turned into the tail the engine would hand over
        var first = await Pull(generator, Fresh());
        var tailTicks = 4L * 4L * Resolution;
        var tail = TailOf(first, tailTicks);

        var request = Fresh();
        request.Seed = 20260922;
        request.Continuation = new MusicContinuation
        {
            Tail = tail,
            TailTicks = tailTicks,
            Meter = MusicMeter.CommonTime
        };

        //Act
        var second = await Pull(generator, request);

        //Assert - new music, written from tick 0 of its own segment, and none of the prompt again
        second.Where(item => item.HasEvent).Should().NotBeEmpty();
        second.Where(item => item.HasEvent)
            .Should().OnlyContain(item => item.Event.AbsoluteTime >= 0L);

        //The prompt's own events are not re-emitted, so a pass capped at ShortPass events cannot
        //come back carrying the four bars it was shown as well.
        second.Count(item => item.HasEvent).Should().BeLessThanOrEqualTo(ShortPass);
        Describe(second).Should().NotEqual(Describe(first));
    }

    [Fact]
    public async Task a_follow_up_prompt_over_one_generator_hands_the_model_over_to_the_new_music()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange - ONE generator, the way an application registers one, and a session on it
        TestInstrumentLibraries.GeneralMidi();
        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions
        {
            Generator = Name,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            ApplicationOwnsAudioOutput = true,
            SampleRate = SampleRate,
            Preroll = TimeSpan.FromSeconds(1.0),
            EndOfPiece = EndOfPiecePolicy.Stop
        };

        options.Request.Seed = 20260920;
        options.Request.Controls.MaximumEvents = 120;

        using var session = new MusicSession(options);

        session.Play();
        await WaitFor(() => session.Stream.HorizonTime >= TimeSpan.FromSeconds(2.0) ||
                            session.GenerationError != null);

        session.GenerationError.Should().BeNull();

        var segmentsAtTheCall = session.Diagnostics.SegmentCount;
        var followUp = new MusicRequest { Seed = 20260923 };

        followUp.Controls.MaximumEvents = 120;

        //Act - the same generator instance, a new prompt: the engine cancels what is running,
        //waits for the model to let go, and only then asks for the new music.
        session.FollowUp(followUp);

        var left = new float[SampleRate];
        var right = new float[SampleRate];

        await WaitFor(() =>
        {
            // The play head has to move for a follow-up to take over, and this session's head is
            // the application's own renderer.
            session.Renderer.Render(left, right);

            return session.Diagnostics.SegmentCount > segmentsAtTheCall ||
                   session.GenerationError != null;
        });

        //Assert
        session.GenerationError.Should().BeNull();
        session.Diagnostics.SegmentCount.Should().BeGreaterThan(segmentsAtTheCall);
        session.Diagnostics.LateEventCount.Should().Be(0);
    }

    [Fact]
    public async Task a_specified_generator_renders_music_that_is_not_silence()
    {
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange - the device-less road: the application owns its audio output
        TestInstrumentLibraries.GeneralMidi();
        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions
        {
            Generator = Name,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            ApplicationOwnsAudioOutput = true,
            SampleRate = SampleRate,
            Preroll = TimeSpan.FromSeconds(1.0),
            EndOfPiece = EndOfPiecePolicy.Stop
        };

        options.Request.Seed = 20260920;
        options.Request.Controls.MaximumEvents = 120;

        using var session = new MusicSession(options);

        //Act
        session.Play();
        await WaitFor(() => session.Stream.HorizonTime >= TimeSpan.FromSeconds(2.0) ||
                            session.GenerationError != null);

        session.GenerationError.Should().BeNull();

        var peak = 0.0F;
        var left = new float[SampleRate];
        var right = new float[SampleRate];

        for (var second = 0; second < 2; second++)
        {
            session.Renderer.Render(left, right);
            peak = Math.Max(peak, Peak(left, right));
        }

        //Assert
        peak.Should().BeGreaterThan(0.001F);
        session.ActiveSource.GeneratorName.Should().Be(Name);
        session.ActiveSource.GeneratorFamily.Should().Be(SkyTNTMusicGenerator.SkyTNTFamily);
        session.Diagnostics.LateEventCount.Should().Be(0);
    }

    private static SkyTNTGeneratorOptions Options() => SkyTNTLoadedModel.Options();

    private static MusicRequest Fresh()
    {
        var request = new MusicRequest { Seed = 20260920, TicksPerQuarterNote = Resolution };

        request.Controls.MaximumEvents = ShortPass;

        return request;
    }

    private static async Task<IReadOnlyList<GeneratedMusicEvent>> Pull(
        SkyTNTMusicGenerator from, MusicRequest request)
    {
        var produced = new List<GeneratedMusicEvent>();

        await foreach (var item in from.GenerateAsync(request,
                           TestContext.Current.CancellationToken))
        {
            produced.Add(item);
        }

        return produced;
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<GeneratedMusicEvent> produced)
    {
        var described = new List<string>();

        foreach (var item in produced)
        {
            described.Add(item.ToString());
        }

        return described;
    }

    private static MidiEventCollection TailOf(IReadOnlyList<GeneratedMusicEvent> produced,
        long tailTicks)
    {
        var end = 0L;

        foreach (var item in produced)
        {
            if (item.HasSettled && item.SettledThroughTick > end)
            {
                end = item.SettledThroughTick;
            }
        }

        var from = end - tailTicks < 0L ? 0L : end - tailTicks;
        var tail = new MidiEventCollection(1, Resolution);
        var track = tail.AddTrack();

        foreach (var item in produced)
        {
            if (!item.HasEvent || item.Event.AbsoluteTime < from ||
                item.Event.AbsoluteTime >= end)
            {
                continue;
            }

            var moved = item.Event.AbsoluteTime - from;

            track.Add(item.Event is NoteOnEvent note
                ? new NoteOnEvent(moved, note.Channel, note.NoteNumber, note.Velocity,
                    note.NoteLength)
                : Moved(item.Event, moved));
        }

        return tail;
    }

    private static MidiEvent Moved(MidiEvent midiEvent, long tick)
    {
        var copy = midiEvent.Clone();
        copy.AbsoluteTime = tick;

        return copy;
    }

    private static float Peak(float[] left, float[] right)
    {
        var peak = 0.0F;

        for (var i = 0; i < left.Length; i++)
        {
            peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
        }

        return peak;
    }

    // NOTHING SLEEPS. The engine's own background timer is running on the real clock; this waits on
    // the work it is doing by yielding the thread, and gives up with a message rather than hanging.
    private static async Task WaitFor(Func<bool> until)
    {
        var clock = Stopwatch.StartNew();

        while (!until())
        {
            if (clock.Elapsed > HowLongToWaitForMusic)
            {
                throw new InvalidOperationException(
                    "The SkyTNT model did not produce enough music to listen to within " +
                    HowLongToWaitForMusic + ".");
            }

            await Task.Yield();
        }
    }
}
