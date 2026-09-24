using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using CodeBrix.Audio.MusicGeneration.MuPT;
using CodeBrix.Audio.MusicGeneration.Rendition;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The tests that load the published MuPT package's copied GGUF and write music.
/// They run in the ordinary suite without a staging-path environment variable.
/// </summary>
/// <remarks>
/// <para>
/// EVERY ONE OF THEM IS SHORT - a few bars - because a model writes music at processor speed and a
/// suite is run over and over. They make no sound: the audible one is in
/// <see cref="MuPTAudibleTests"/>, behind its playback opt-in.
/// </para>
/// <para>
/// THE MODEL IS LOADED ONCE FOR THE WHOLE CLASS and released when it is finished with, because
/// loading it is the expensive part and this class's job is what the model does, not how often it
/// can be loaded. The tests that are ABOUT loading build generators of their own.
/// </para>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MuPTLiveTests : IClassFixture<MuPTLoadedModel>, IAsyncLifetime
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;

    /// <summary>The name the shared generator is registered and asked for under.</summary>
    public const string Name = "MuPTLive";

    /// <summary>The waltz duet prompt: the shape that produced the best-rated piece of the audition.</summary>
    public const string WaltzDuet =
        "X:1<n>L:1/8<n>Q:1/4=108<n>M:3/4<n>K:Am<n>\"Am\" e2 a2 c'2 | A,2 [CE]2 [CE]2 | <|> <|> " +
        "\"Dm\" d2 f2 a2 | D,2 [FA]2 [FA]2 | <|> <|> ";

    private static readonly TimeSpan HowLongToWaitForMusic = TimeSpan.FromMinutes(3.0);

    private readonly MuPTMusicGenerator generator;

    /// <summary>Starts from freshly built built-ins, over the model the class loaded once.</summary>
    /// <param name="loaded">The model, loaded once for the whole class.</param>
    public MuPTLiveTests(MuPTLoadedModel loaded)
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();

        generator = loaded.Generator;
    }

    /// <summary>
    /// Waits for the shared generator to be free before each test. A session test ends by
    /// disposing its session, which CANCELS the generation it was pulling - but that pass lets go
    /// of the model on its own task a moment later, and the generator refuses a second generation
    /// by name while the first still holds it. On a busy machine the next test could otherwise
    /// start inside that moment and be refused.
    /// </summary>
    /// <returns>A task that completes when the generator is free.</returns>
    public async ValueTask InitializeAsync()
    {
        var clock = Stopwatch.StartNew();

        while (generator.IsGenerating)
        {
            if (clock.Elapsed > HowLongToWaitForMusic)
            {
                throw new InvalidOperationException(
                    "The shared generator was still writing a piece from an earlier test after " +
                    HowLongToWaitForMusic + ".");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Nothing to give back: the class fixture owns the generator.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task the_model_loads_from_a_file_and_says_so()
    {
        //Act
        await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        generator.IsLoaded.Should().BeTrue();
        generator.LoadedThreadCount.Should()
            .Be(MuPTGeneratorOptions.DefaultInferenceThreadCount);
    }

    [Fact]
    public async Task a_short_pass_writes_a_few_bars_of_the_waltz_duet()
    {
        //Act
        var produced = await Pull(generator, Duet());

        //Assert - music, at segment-relative ticks, with two parts
        var notes = Notes(produced);

        notes.Should().NotBeEmpty();
        notes[0].AbsoluteTime.Should().BeGreaterThanOrEqualTo(0L);
        Channels(notes).Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task the_incremental_path_agrees_with_one_shot_conversion_on_live_output()
    {
        //Arrange - a pass, and the text the model really wrote for it
        var produced = await Pull(generator, Duet());
        var text = generator.LastModelText;

        //Act
        var oneShot = MuPTTune.Read(text, Resolution);

        //Assert - event for event, tick for tick
        Describe(produced).Should().Equal(Describe(oneShot.Events));
    }

    [Fact]
    public async Task nothing_is_ever_let_out_at_or_before_a_tick_already_announced_as_settled()
    {
        //Act
        var produced = await Pull(generator, Duet());

        //Assert
        var settled = GeneratedMusicEvent.NothingSettled;

        foreach (var item in produced)
        {
            if (item.HasEvent)
            {
                item.Event.AbsoluteTime.Should().BeGreaterThan(settled);
            }

            if (item.HasSettled)
            {
                settled = item.SettledThroughTick;
            }
        }
    }

    [Fact]
    public async Task a_seeded_generation_writes_the_same_music_twice()
    {
        //Arrange - something else in between, which leaves the engine's context holding another
        //piece: a seed that only worked on a freshly loaded model would not be worth much.
        var other = Duet();

        other.Seed = 20260930;

        //Act
        var first = await Pull(generator, Duet());

        await Pull(generator, other);

        var second = await Pull(generator, Duet());

        //Assert
        Describe(first).Should().Equal(Describe(second));
    }

    [Fact]
    public async Task another_seed_writes_other_music()
    {
        //Arrange
        var other = Duet();

        other.Seed = 20260922;

        //Act
        var first = await Pull(generator, Duet());
        var second = await Pull(generator, other);

        //Assert
        Describe(first).Should().NotEqual(Describe(second));
    }

    [Fact]
    public async Task a_continuation_carries_the_key_and_the_metre_over_the_seam()
    {
        //Arrange - a first pass, then the request the engine would make for the next segment
        await Pull(generator, Duet());

        var request = new MusicRequest { Seed = 20260922, TicksPerQuarterNote = Resolution };

        request.Controls.MaximumEvents = MuPTLoadedModel.ShortPass;
        request.Continuation = new MusicContinuation
        {
            Meter = new MusicMeter(3, 4),
            Key = "A",
            Mode = MusicMode.Minor,
            BeatsPerMinute = 108.0
        };

        //Act
        var produced = await Pull(generator, request);
        var text = generator.LastModelText;

        //Assert - the model was prompted with its own header and its own last bars, and the new
        //music starts at the segment's own tick 0 rather than after the tail.
        text.Should().StartWith("X:1<n>L:1/8<n>Q:1/4=108<n>M:3/4<n>K:Am<n>");
        Notes(produced).Should().NotBeEmpty();
        Notes(produced)[0].AbsoluteTime.Should().BeLessThan(4L * 3L * Resolution);
    }

    [Fact]
    public async Task releasing_gives_the_memory_back_and_the_next_request_loads_it_again()
    {
        //Arrange
        using var reloaded = new MuPTMusicGenerator("MuPTReload", MuPTModel.ModelPath,
            MuPTLoadedModel.Options());

        await reloaded.PreloadAsync(TestContext.Current.CancellationToken);

        //Act
        reloaded.Release();
        var afterRelease = reloaded.IsLoaded;

        await Pull(reloaded, Duet());

        //Assert
        afterRelease.Should().BeFalse();
        reloaded.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task a_request_naming_a_different_thread_count_reloads_the_model_rather_than_ignoring_it()
    {
        //Arrange
        using var threaded = new MuPTMusicGenerator("MuPTThreads", MuPTModel.ModelPath,
            MuPTLoadedModel.Options());

        await threaded.PreloadAsync(TestContext.Current.CancellationToken);
        var loadedFirst = threaded.LoadedThreadCount;

        var request = Duet();

        request.InferenceThreadCount = 2;

        //Act
        await Pull(threaded, request);

        //Assert
        loadedFirst.Should().Be(MuPTGeneratorOptions.DefaultInferenceThreadCount);
        threaded.LoadedThreadCount.Should().Be(2);
    }

    [Fact]
    public async Task a_specified_generator_renders_two_parts_of_music_that_are_not_silence()
    {
        //Arrange - the device-less road: the application owns its audio output
        TestInstrumentLibraries.GeneralMidi();
        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions
        {
            Generator = Name,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Rendition = BuiltInRenditions.AmbientDuet,
            ApplicationOwnsAudioOutput = true,
            SampleRate = SampleRate,
            Preroll = TimeSpan.FromSeconds(1.0),
            EndOfPiece = EndOfPiecePolicy.Stop
        };

        options.Request.ModelNativeText = WaltzDuet;
        options.Request.Seed = 20260921;
        options.Request.Controls.MaximumEvents = 192;

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
        session.ActiveSource.GeneratorFamily.Should().Be(MuPTMusicGenerator.MuPTFamily);
        session.Diagnostics.Voicing.Parts.Count.Should().BeGreaterThanOrEqualTo(2);
        session.Diagnostics.LateEventCount.Should().Be(0);
    }

    // --- the rig ------------------------------------------------------------------------------

    private static MusicRequest Duet()
    {
        var request = new MusicRequest
        {
            ModelNativeText = WaltzDuet,
            Seed = 20260921,
            TicksPerQuarterNote = Resolution
        };

        request.Controls.MaximumEvents = MuPTLoadedModel.ShortPass;

        return request;
    }

    private static async Task<IReadOnlyList<GeneratedMusicEvent>> Pull(MuPTMusicGenerator from,
        MusicRequest request)
    {
        var produced = new List<GeneratedMusicEvent>();

        await foreach (var item in from.GenerateAsync(request,
                           TestContext.Current.CancellationToken))
        {
            produced.Add(item);
        }

        return produced;
    }

    private static IReadOnlyList<NoteOnEvent> Notes(IReadOnlyList<GeneratedMusicEvent> produced)
    {
        var notes = new List<NoteOnEvent>();

        foreach (var item in produced)
        {
            if (item.HasEvent && item.Event is NoteOnEvent note)
            {
                notes.Add(note);
            }
        }

        return notes;
    }

    private static IReadOnlyList<int> Channels(IReadOnlyList<NoteOnEvent> notes)
    {
        var channels = new List<int>();

        foreach (var note in notes)
        {
            if (!channels.Contains(note.Channel))
            {
                channels.Add(note.Channel);
            }
        }

        return channels;
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<GeneratedMusicEvent> produced)
    {
        var described = new List<string>();

        foreach (var item in produced)
        {
            if (item.HasEvent)
            {
                described.Add(Describe(item.Event));
            }
        }

        return described;
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<MidiEvent> events)
    {
        var described = new List<string>();

        for (var i = 0; i < events.Count; i++)
        {
            described.Add(Describe(events[i]));
        }

        return described;
    }

    private static string Describe(MidiEvent midiEvent) =>
        midiEvent is NoteOnEvent note
            ? "note " + note.NoteNumber + " channel " + note.Channel + " at " + note.AbsoluteTime +
              " for " + (note.OffEvent == null ? 0 : note.NoteLength)
            : midiEvent.GetType().Name + " at " + midiEvent.AbsoluteTime + " " + midiEvent;

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
                    "Waited " + HowLongToWaitForMusic + " for the music and it did not arrive.");
            }

            await Task.Yield();
        }
    }
}
