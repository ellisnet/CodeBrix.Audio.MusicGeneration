using System;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The streaming lifecycle as an APPLICATION meets it: music that keeps going, a prompt that can
/// be changed while it plays, a model that is loaded and released on purpose, the thread count it
/// is given, and the numbers a game loop reads to see how the music is doing.
/// </summary>
/// <remarks>
/// Nothing here opens the audio device: the application-owns-its-output mode means the play head
/// moves only as the test renders, so the test decides how much has been heard, and the clock is
/// moved by hand.
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MusicSessionLifecycleTests
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;
    private const string TestGeneratorName = "TestGenerator";
    private const string SecondGeneratorName = "SecondGenerator";

    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicSessionLifecycleTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    // --- KEEPING GOING ------------------------------------------------------------------------

    [Fact]
    public async Task a_session_keeps_the_music_going_by_default()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);

        //Act
        await EnginePump.RunUntilAsync(time, () => session.Diagnostics.SegmentCount >= 3);

        //Assert - the same generator, asked again, placed at the next bar line
        generator.PassCount.Should().BeGreaterThanOrEqualTo(3);
        session.Stream.IsCompleted.Should().BeFalse();
        session.IsFinished.Should().BeFalse();
    }

    [Fact]
    public async Task the_end_of_piece_policy_of_stopping_ends_the_music_the_way_a_file_does()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        var options = Options(TestGeneratorName);
        options.EndOfPiece = EndOfPiecePolicy.Stop;

        using var session = Play(options, time);

        //Act
        await EnginePump.RunUntilAsync(time, () => session.Stream.IsCompleted);

        //Assert
        generator.PassCount.Should().Be(1);
        session.Diagnostics.SegmentCount.Should().Be(1);
    }

    [Fact]
    public async Task stopping_stops_asking_the_generator_for_anything()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);
        await EnginePump.RunUntilAsync(time, () => session.Diagnostics.SegmentCount >= 2);

        //Act
        session.Stop();
        var asked = generator.PassCount;

        for (var step = 0; step < 50; step++)
        {
            await EnginePump.StepAsync(time);
        }

        //Assert
        session.IsPlaying.Should().BeFalse();
        generator.PassCount.Should().Be(asked);
    }

    // --- A FOLLOW-UP PROMPT -------------------------------------------------------------------

    [Fact]
    public async Task a_follow_up_takes_over_and_the_active_source_changes_at_the_switch()
    {
        //Arrange
        var time = new ManualTimeProvider();
        Register(TestGeneratorName, TestMusic.Bars(4, Resolution), time, 8.0);
        var second = Register(SecondGeneratorName, TestMusic.Bars(4, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);
        await EnginePump.RunUntilAsync(time, () => session.Stream.EventCount > 0);

        //Act
        session.FollowUp(new MusicRequest(), SecondGeneratorName);
        var atTheCall = session.ActiveSource.GeneratorName;
        await EnginePump.RunUntilAsync(time,
            () => session.ActiveSource.GeneratorName == SecondGeneratorName);

        //Assert - the report follows what is PLAYING, not what has been asked for
        atTheCall.Should().Be(TestGeneratorName);
        session.ActiveSource.GeneratorName.Should().Be(SecondGeneratorName);
        second.PassCount.Should().BeGreaterThan(0);
        session.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task a_follow_up_without_a_generator_name_keeps_the_one_that_is_playing()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(4, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);
        await EnginePump.RunUntilAsync(time, () => session.Stream.EventCount > 0);
        var asked = generator.PassCount;

        //Act
        session.FollowUp(new MusicRequest { Text = "something else" });
        await EnginePump.RunUntilAsync(time, () => generator.PassCount > asked);

        //Assert
        session.ActiveSource.GeneratorName.Should().Be(TestGeneratorName);
        generator.Requests[generator.PassCount - 1].Text.Should().Be("something else");
    }

    [Fact]
    public async Task a_follow_up_naming_a_generator_that_is_not_registered_is_an_error_and_the_music_carries_on()
    {
        //Arrange
        var time = new ManualTimeProvider();
        Register(TestGeneratorName, TestMusic.Bars(4, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);
        await EnginePump.RunUntilAsync(time, () => session.Stream.EventCount > 0);
        Action act = () => session.FollowUp(new MusicRequest(), "NotRegistered");

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Contain("No music generator named 'NotRegistered' is registered");
        session.IsPlaying.Should().BeTrue();
        session.ActiveSource.GeneratorName.Should().Be(TestGeneratorName);
        session.Stream.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task a_follow_up_the_generator_cannot_honour_is_refused_and_the_music_carries_on()
    {
        //Arrange - the embedded replay honours a continuation and nothing else
        var time = new ManualTimeProvider();
        ReplayOnTestClock.Registered(EmbeddedReplay.Midi, time);
        using var session = Play(Options(null), time);
        await EnginePump.RunUntilAsync(time, () => session.Stream.EventCount > 0);
        Action act = () => session.FollowUp(new MusicRequest { Seed = 7 });

        //Act
        var thrown = act.Should().Throw<MusicRequestNotHonouredException>().Which;

        //Assert
        thrown.Message.Should().Contain("seed");
        session.IsPlaying.Should().BeTrue();
        session.Stream.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void a_follow_up_before_anything_is_playing_says_what_to_do_about_it()
    {
        //Arrange
        var time = new ManualTimeProvider();
        Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Session(Options(TestGeneratorName), time);
        Action act = () => session.FollowUp(new MusicRequest());

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Contain("call Play() first");
    }

    [Fact]
    public void a_follow_up_needs_a_request()
    {
        //Arrange
        var time = new ManualTimeProvider();
        Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);
        Action act = () => session.FollowUp(null);

        //Act and Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task a_follow_up_is_asked_at_the_resolution_of_the_music_that_is_playing()
    {
        //Arrange - several segments from several generators land on ONE timeline
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(4, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);
        await EnginePump.RunUntilAsync(time, () => session.Stream.EventCount > 0);
        var asked = generator.PassCount;

        //Act
        session.FollowUp(new MusicRequest { TicksPerQuarterNote = 96 });
        await EnginePump.RunUntilAsync(time, () => generator.PassCount > asked);

        //Assert
        generator.Requests[generator.PassCount - 1].TicksPerQuarterNote.Should().Be(Resolution);
    }

    // --- LOADING ------------------------------------------------------------------------------

    [Fact]
    public async Task preloading_loads_the_generator_the_options_name()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Session(Options(TestGeneratorName), time);

        //Act
        generator.LoadCount.Should().Be(0);
        await session.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        generator.LoadCount.Should().Be(1);
        generator.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task a_generator_that_was_not_preloaded_loads_when_it_is_first_used()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);

        //Act
        await EnginePump.RunUntilAsync(time, () => generator.IsLoaded);

        //Assert
        generator.LoadCount.Should().Be(1);
    }

    [Fact]
    public async Task a_loaded_generator_stays_loaded_across_pieces_and_across_sessions()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);

        using (var first = Play(Options(TestGeneratorName), time))
        {
            await EnginePump.RunUntilAsync(time, () => generator.IsLoaded);
            first.Stop();
        }

        //Act - a second session, and a third piece in it
        using var second = Play(Options(TestGeneratorName), time);
        await EnginePump.RunUntilAsync(time, () => second.Diagnostics.SegmentCount >= 2);

        //Assert - it lives in the registry, not in a session
        generator.LoadCount.Should().Be(1);
        generator.ReleaseCount.Should().Be(0);
    }

    [Fact]
    public async Task disposing_a_session_releases_nothing()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        var session = Play(Options(TestGeneratorName), time);
        await EnginePump.RunUntilAsync(time, () => generator.IsLoaded);

        //Act
        session.Dispose();

        //Assert - a loaded model belongs to the application, not to one session
        generator.ReleaseCount.Should().Be(0);
        generator.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task releasing_gives_the_memory_back_and_the_next_use_loads_it_again()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Session(Options(TestGeneratorName), time);
        await session.PreloadAsync(TestContext.Current.CancellationToken);

        //Act
        session.Release();
        var afterRelease = generator.IsLoaded;

        session.Play();
        await EnginePump.RunUntilAsync(time, () => generator.IsLoaded);

        //Assert - the registration survived, and asking again loaded it again
        afterRelease.Should().BeFalse();
        generator.ReleaseCount.Should().Be(1);
        generator.LoadCount.Should().Be(2);
        MusicGeneratorRegistry.IsRegistered(TestGeneratorName).Should().BeTrue();
    }

    // --- THE THREAD COUNT ---------------------------------------------------------------------

    [Fact]
    public async Task the_thread_count_in_the_options_reaches_the_request()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        var options = Options(TestGeneratorName);
        options.InferenceThreadCount = 3;

        using var session = Play(options, time);

        //Act
        await EnginePump.RunUntilAsync(time, () => generator.PassCount > 0);

        //Assert
        generator.Requests[0].InferenceThreadCount.Should().Be(3);
    }

    [Fact]
    public async Task an_unset_thread_count_leaves_the_generator_to_its_own_conservative_default()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);

        //Act
        await EnginePump.RunUntilAsync(time, () => generator.PassCount > 0);

        //Assert
        generator.Requests[0].InferenceThreadCount.Should().BeNull();
    }

    [Fact]
    public void a_thread_count_a_generator_cannot_honour_is_refused_by_name()
    {
        //Arrange - a replay has no model and nothing to run threads on
        var time = new ManualTimeProvider();
        var options = Options(null);
        options.InferenceThreadCount = 4;

        using var session = Session(options, time);
        Action act = () => session.Play();

        //Act
        var thrown = act.Should().Throw<MusicRequestNotHonouredException>().Which;

        //Assert
        thrown.Message.Should().Contain("thread");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void a_thread_count_that_is_not_a_count_is_refused(int threads) =>
        ((Action)(() => new MusicGenerationOptions { InferenceThreadCount = threads }))
        .Should().Throw<ArgumentOutOfRangeException>();

    // --- THE GENERATE-AHEAD SETTING -----------------------------------------------------------

    [Fact]
    public void the_generate_ahead_window_starts_at_half_a_minute() =>
        new MusicGenerationOptions().GenerateAhead.Should().Be(TimeSpan.FromSeconds(30.0));

    [Fact]
    public void an_empty_generate_ahead_window_is_refused() =>
        ((Action)(() => new MusicGenerationOptions { GenerateAhead = TimeSpan.Zero }))
        .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void the_music_keeps_generating_unless_the_options_say_otherwise() =>
        new MusicGenerationOptions().EndOfPiece.Should().Be(EndOfPiecePolicy.KeepGenerating);

    [Fact]
    public void every_lifecycle_setting_survives_a_copy()
    {
        //Arrange
        var options = new MusicGenerationOptions
        {
            GenerateAhead = TimeSpan.FromSeconds(12.0),
            EndOfPiece = EndOfPiecePolicy.Stop,
            InferenceThreadCount = 2
        };

        //Act
        var copy = options.Clone();

        //Assert
        copy.GenerateAhead.Should().Be(TimeSpan.FromSeconds(12.0));
        copy.EndOfPiece.Should().Be(EndOfPiecePolicy.Stop);
        copy.InferenceThreadCount.Should().Be(2);
    }

    [Fact]
    public async Task the_generate_ahead_setting_is_what_the_engine_runs_on()
    {
        //Arrange
        var time = new ManualTimeProvider();
        Register(TestGeneratorName, TestMusic.Bars(8, Resolution), time, 8.0);
        var options = Options(TestGeneratorName);
        options.GenerateAhead = TimeSpan.FromSeconds(6.0);

        using var session = Play(options, time);

        //Act
        await EnginePump.RunUntilAsync(time,
            () => session.Diagnostics.Lead >= TimeSpan.FromSeconds(6.0));

        for (var step = 0; step < 200; step++)
        {
            await EnginePump.StepAsync(time);
        }

        //Assert - the head never moved, so nothing ever took the lead back down
        session.Diagnostics.Lead.Should().BeLessThan(TimeSpan.FromSeconds(14.0));
    }

    // --- THE DIAGNOSTICS ----------------------------------------------------------------------

    [Fact]
    public void the_diagnostics_before_the_music_starts_say_that_nothing_has_happened()
    {
        //Arrange
        var time = new ManualTimeProvider();
        using var session = Session(Options(null), time);

        //Act
        var diagnostics = session.Diagnostics;

        //Assert
        diagnostics.Should().NotBeNull();
        diagnostics.SegmentCount.Should().Be(0);
        diagnostics.StarvationGapCount.Should().Be(0);
        diagnostics.RealTimeFactor.Should().BeNull();
        diagnostics.ToString().Should().Contain("not measured yet");
        diagnostics.Lead.Should().Be(TimeSpan.Zero);
        diagnostics.Mode.Should().Be(MusicDeliveryMode.Streaming);
        diagnostics.IsSegmentAtATime.Should().BeFalse();
        diagnostics.Voicing.Should().BeNull();
    }

    [Fact]
    public async Task the_diagnostics_report_what_the_music_has_been_doing()
    {
        //Arrange
        var time = new ManualTimeProvider();
        Register(TestGeneratorName, TestMusic.Bars(1, Resolution), time, 8.0);
        using var session = Play(Options(TestGeneratorName), time);

        //Act
        await EnginePump.RunUntilAsync(time, () => session.Diagnostics.SegmentCount >= 3);
        var diagnostics = session.Diagnostics;

        //Assert
        diagnostics.SegmentCount.Should().BeGreaterThanOrEqualTo(3);
        diagnostics.Lead.Should().BeGreaterThan(TimeSpan.Zero);
        diagnostics.LateEventCount.Should().Be(0);

        //A generator eight times faster than the music never runs dry, and the wait for the first
        //pre-roll is not a gap - so an ordinary start reports none at all.
        diagnostics.StarvationGapCount.Should().Be(0);
        diagnostics.Voicing.Should().NotBeNull();
        diagnostics.Voicing.Parts.Should().NotBeEmpty();
        diagnostics.ToString().Should().Contain("segment");
    }

    [Fact]
    public async Task a_generator_slower_than_real_time_is_reported_as_one()
    {
        //Arrange - the head really plays, through a real sequencer, and really runs out of music
        var time = new ManualTimeProvider();
        Register(TestGeneratorName, TestMusic.Bars(16, Resolution), time, 0.2);
        var options = Options(TestGeneratorName);
        options.Preroll = TimeSpan.FromSeconds(0.5);

        using var session = Play(options, time);

        var left = new float[SampleRate];
        var right = new float[SampleRate];

        //Act - eight seconds of listening against a generator producing a fifth of real time
        //a second of audio at a time, and then the clock moved on by that second THROUGH THE
        //PUMP, timer by timer, letting the pull loop and the generator catch up at every step -
        //so what the generator has written by each second is the same on every run and every
        //machine, rather than whatever a thread-pool thread happened to reach
        for (var second = 0; second < 8; second++)
        {
            session.Renderer.Render(left, right);

            var until = time.GetUtcNow() + TimeSpan.FromSeconds(1.0);

            await EnginePump.RunUntilAsync(time, () => time.GetUtcNow() >= until);
        }

        var diagnostics = session.Diagnostics;

        //Assert - it ran out, it said so, and nothing arrived behind the head
        diagnostics.StarvationGapCount.Should().BeGreaterThan(0);
        diagnostics.RealTimeFactor.Should().BeLessThan(1.0);
        diagnostics.Mode.Should().Be(MusicDeliveryMode.SegmentAtATime);
        diagnostics.IsSegmentAtATime.Should().BeTrue();
        diagnostics.LateEventCount.Should().Be(0);
        session.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task a_generator_slower_than_real_time_still_falls_back_with_a_crossfade_being_prepared()
    {
        //Arrange - the same slow generator, with fresh seams crossfaded, so the engine's own
        //crossfade preparation starts in the background as soon as the music does
        var time = new ManualTimeProvider();
        Register(TestGeneratorName, TestMusic.Bars(16, Resolution), time, 0.2);
        var options = Options(TestGeneratorName);
        options.Preroll = TimeSpan.FromSeconds(0.5);
        options.SegmentPriming = SegmentPriming.Fresh;
        options.SeamCrossfade = TimeSpan.FromSeconds(1.0);

        using var session = Play(options, time);

        var left = new float[SampleRate];
        var right = new float[SampleRate];

        //Act
        for (var second = 0; second < 8; second++)
        {
            session.Renderer.Render(left, right);

            var until = time.GetUtcNow() + TimeSpan.FromSeconds(1.0);

            await EnginePump.RunUntilAsync(time, () => time.GetUtcNow() >= until);
        }

        await session.Engine.CrossfadePreparation.WaitAsync(TimeSpan.FromSeconds(30.0),
            TestContext.Current.CancellationToken);

        //Assert - preparation ran, and the slow generator was still caught and fallen back from
        var diagnostics = session.Diagnostics;
        session.Engine.CrossfadePreparationStarted.Should().BeTrue();
        diagnostics.RealTimeFactor.Should().BeLessThan(1.0);
        diagnostics.Mode.Should().Be(MusicDeliveryMode.SegmentAtATime);
        session.GenerationError.Should().BeNull();
    }

    // --- the rig ------------------------------------------------------------------------------

    private static RecordingMusicGenerator Register(string name, MidiEventCollection music,
        ManualTimeProvider time, double pacingRate)
    {
        var generator = new RecordingMusicGenerator(name,
            ReplayOnTestClock.On(ReplayMusicGenerator.FromMidiEvents(name, music), time, pacingRate));

        MusicGeneratorRegistry.Register(generator);

        return generator;
    }

    private static MusicGenerationOptions Options(string generatorName) =>
        new MusicGenerationOptions
        {
            ApplicationOwnsAudioOutput = true,
            Generator = generatorName,
            InstrumentLibrary = TestInstrumentLibraries.CompleteName,
            Preroll = TimeSpan.FromSeconds(1.0),
            SampleRate = SampleRate
        };

    private static MusicSession Session(MusicGenerationOptions options, ManualTimeProvider time)
    {
        TestInstrumentLibraries.Complete();

        return new MusicSession(options, TestInstrumentLibraries.Lookup(), time);
    }

    private static MusicSession Play(MusicGenerationOptions options, ManualTimeProvider time)
    {
        var session = Session(options, time);
        session.Play();

        return session;
    }
}
