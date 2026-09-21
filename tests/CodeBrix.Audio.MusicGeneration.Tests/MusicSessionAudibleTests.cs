using System;
using System.Threading;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The tests in this project that MAKE A SOUND. Opt-in via CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1;
/// skipped otherwise.
/// </summary>
/// <remarks>
/// They are played out loud because that is the only way to know that the whole road works:
/// generation, the reorder buffer, the commit window, the rendition, the routing table, the
/// sequencer and the audio device. What a test can assert is that the transport really ran and
/// that nothing arrived late; whether a seam is audible is a question for an ear.
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MusicSessionAudibleTests
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    private static readonly TimeSpan HowLongToListen = TimeSpan.FromSeconds(20.0);
    private static readonly TimeSpan HowLongToListenForALoop = TimeSpan.FromSeconds(34.0);
    private static readonly TimeSpan HowLongToListenAfterAFollowUp = TimeSpan.FromSeconds(16.0);
    private static readonly TimeSpan HowLongToListenToTheFallback = TimeSpan.FromSeconds(75.0);

    /// <summary>
    /// Starts from freshly built built-ins: other classes in this collection put the embedded
    /// replays on a clock of their own, and a generator paced against a test's clock would never
    /// release a note to a real audio device.
    /// </summary>
    public MusicSessionAudibleTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public void plays_the_embedded_music_out_loud_with_nothing_specified()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - this is the whole of layer 1, exactly as the documentation shows it
        GeneralMidiInstrumentLibrary.Register();

        using var music = new MusicSession();

        //Act
        music.Play();

        using var listening = new ManualResetEventSlim(false);
        listening.Wait(HowLongToListen, TestContext.Current.CancellationToken);

        //Assert - what matters is what was heard; these say the transport really ran
        music.Position.Should().BeGreaterThan(HowLongToListen - TimeSpan.FromSeconds(5.0));
        music.GenerationError.Should().BeNull();
        music.ActiveSource.GeneratorName.Should().Be(EmbeddedReplay.Midi);
        music.ActiveSource.Voicing.Parts.Should().NotBeEmpty();
        music.Stop();
    }

    [Fact]
    public void loops_across_a_seam_and_then_takes_a_follow_up_prompt_out_loud()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - the ABC replay is about twenty-seven seconds long, so it reaches the end of
        //itself and LOOPS while a listener is still listening
        GeneralMidiInstrumentLibrary.Register();

        using var music = new MusicSession(new MusicGenerationOptions
        {
            Generator = EmbeddedReplay.Abc,
            Rendition = BuiltInRenditions.AmbientDuet
        });

        //Act - listen across the loop seam
        music.Play();

        using var listening = new ManualResetEventSlim(false);
        listening.Wait(HowLongToListenForALoop, TestContext.Current.CancellationToken);

        var afterTheLoop = music.Diagnostics;
        var sourceBeforeTheFollowUp = music.ActiveSource.GeneratorName;

        // ... and then ask for something else while it is playing
        music.FollowUp(new MusicRequest(), EmbeddedReplay.MidiSecond);
        listening.Wait(HowLongToListenAfterAFollowUp, TestContext.Current.CancellationToken);

        var afterTheFollowUp = music.Diagnostics;

        //Assert - the music went round at least once, then changed to the other piece
        afterTheLoop.SegmentCount.Should().BeGreaterThan(1);
        sourceBeforeTheFollowUp.Should().Be(EmbeddedReplay.Abc);
        music.ActiveSource.GeneratorName.Should().Be(EmbeddedReplay.MidiSecond);
        afterTheFollowUp.SegmentCount.Should().BeGreaterThan(afterTheLoop.SegmentCount);

        // Nothing arrived behind the play head, at the loop seam or at the prompt change: an
        // audible click at a seam would show up here as a late event or as a starvation gap.
        afterTheFollowUp.LateEventCount.Should().Be(0);
        music.Position.Should().BeGreaterThan(HowLongToListenForALoop);
        music.GenerationError.Should().BeNull();
        music.Stop();
    }

    [Fact]
    public void a_generator_slower_than_real_time_plays_phrases_with_rests_between_them_out_loud()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - THE FALLBACK, OUT LOUD, through ModestSynthGm. The embedded ABC duet is written
        //deliberately slower than it plays, so the engine stops streaming and delivers a whole
        //segment at a time: a phrase, a rest of whole bars while the next one is worked out, then
        //the next phrase - in time, and with nothing arriving behind the play head.
        GeneralMidiInstrumentLibrary.Register();

        var slow = (ReplayMusicGenerator)MusicGeneratorRegistry.Resolve(EmbeddedReplay.Abc);

        slow.PacingRate = 0.4;

        using var music = new MusicSession(new MusicGenerationOptions
        {
            Generator = EmbeddedReplay.Abc,
            InstrumentLibrary = GeneralMidiInstrumentLibrary.LibraryName,
            Rendition = BuiltInRenditions.AmbientDuet,
            Preroll = TimeSpan.FromSeconds(2.0),
            GenerateAhead = TimeSpan.FromSeconds(8.0)
        });

        //Act - long enough for two whole segments and the rest between them
        music.Play();

        using var listening = new ManualResetEventSlim(false);
        listening.Wait(HowLongToListenToTheFallback, TestContext.Current.CancellationToken);

        var diagnostics = music.Diagnostics;

        TestContext.Current.TestOutputHelper.WriteLine("The fallback, out loud: " + diagnostics);
        TestContext.Current.TestOutputHelper.WriteLine("Heard " + music.Position + " of music.");

        //Assert - it really did fall back, it really did play, and NOTHING reached the timeline
        //behind the head, which is what a burst of late events at the end of a rest would show as
        diagnostics.IsSegmentAtATime.Should().BeTrue();
        diagnostics.RealTimeFactor.Should().BeLessThan(1.0);
        diagnostics.LateEventCount.Should().Be(0);
        diagnostics.StarvationGapCount.Should().BeGreaterThan(0);
        music.Position.Should().BeGreaterThan(TimeSpan.FromSeconds(15.0));
        music.GenerationError.Should().BeNull();
        music.Stop();
    }
}
