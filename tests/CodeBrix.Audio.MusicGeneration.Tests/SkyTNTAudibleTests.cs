using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Rendition;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The SkyTNT test that MAKES A SOUND: music streamed out of the audio device while the model is
/// still writing it.
/// </summary>
/// <remarks>
/// <para>
/// TWO GATES, because it needs two things. CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 opens the audio
/// device, and CODEBRIX_AUDIO_MUSICGEN_SKYTNT_BUNDLE says where the model is; without either it
/// skips and says which one is missing.
/// </para>
/// <para>
/// IT IS FOR A PURPOSE, not for a suite: what a test can assert is that the transport really ran,
/// that nothing arrived late and that music reached the device while it was still being written.
/// Whether it is any good is a question for an ear, and the diagnostics it ends with are what the
/// run is for.
/// </para>
/// <code>
/// CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 CODEBRIX_AUDIO_MUSICGEN_SKYTNT_BUNDLE=/path/to/bundle \
///   dotnet tests/CodeBrix.Audio.MusicGeneration.Tests/bin/Release/net10.0/CodeBrix.Audio.MusicGeneration.Tests.dll \
///   -class "CodeBrix.Audio.MusicGeneration.Tests.SkyTNTAudibleTests"
/// </code>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class SkyTNTAudibleTests
{
    private const string Name = "SkyTNTAudible";

    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    private static readonly TimeSpan HowLongToListen = TimeSpan.FromSeconds(30.0);
    private static readonly TimeSpan HowLongToWaitForTheFirstMusic = TimeSpan.FromMinutes(3.0);

    /// <summary>
    /// Starts from freshly built built-ins: other classes in this collection put the embedded
    /// replays on a clock of their own, and a generator paced against a test's clock would never
    /// release a note to a real audio device.
    /// </summary>
    public SkyTNTAudibleTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public async Task plays_half_a_minute_of_SkyTNT_out_loud_while_it_is_still_being_written()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange - the whole road, exactly as an application would write it
        GeneralMidiInstrumentLibrary.Register();

        using var generator = new SkyTNTMusicGenerator(Name, SkyTNTModelBundle.Directory,
            new SkyTNTGeneratorOptions
            {
                InferenceThreadCount = SkyTNTGeneratorOptions.DefaultInferenceThreadCount,
                MaximumEventsPerPass = SkyTNTGeneratorOptions.DefaultMaximumEventsPerPass,
                DrumKit = 0
            });

        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions { Generator = Name };

        options.Request.Seed = 20260920;
        options.Request.InstrumentHints.Add(GeneralMidiProgram.AcousticGrandPiano);
        options.Request.InstrumentHints.Add(GeneralMidiProgram.Violin);
        options.Request.InstrumentHints.Add(GeneralMidiProgram.Flute);
        options.Request.InstrumentHints.Add(GeneralMidiProgram.TubularBells);
        options.Request.InstrumentHints.Add(GeneralMidiProgram.StringEnsemble1);

        using var music = new MusicSession(options);

        //Act - start it, wait for the music to reach the device, then listen for half a minute
        music.Play();

        await WaitFor(() => music.Position > TimeSpan.Zero || music.GenerationError != null,
            HowLongToWaitForTheFirstMusic, "the first music to reach the device");

        music.GenerationError.Should().BeNull();

        var heardFrom = music.Position;

        await WaitFor(() => music.Position - heardFrom >= HowLongToListen ||
                            music.IsFinished || music.GenerationError != null,
            HowLongToListen + TimeSpan.FromMinutes(3.0), "half a minute of music to be heard");

        var health = music.Diagnostics;
        var heard = music.Position - heardFrom;
        var error = music.GenerationError;

        // EVERYTHING IS READ BEFORE THE TRANSPORT IS STOPPED: a stopped player's head is back at
        // the start, and a number read after it would say the music never played.
        music.Stop();

        //Assert - the transport really ran, and nothing arrived behind the play head
        error.Should().BeNull();
        heard.Should().BeGreaterThan(TimeSpan.FromSeconds(20.0));
        health.SegmentCount.Should().BeGreaterThan(0);
        health.LateEventCount.Should().Be(0);
        health.Voicing.Should().NotBeNull();

        //What the run is FOR: the numbers it ended with, in the test's own output.
        TestContext.Current.TestOutputHelper.WriteLine(health.ToString());
        TestContext.Current.TestOutputHelper.WriteLine(
            "heard " + heard + "; real-time factor " +
            (health.RealTimeFactor.HasValue ? health.RealTimeFactor.Value.ToString("0.00") : "not measured yet") +
            "; gaps " + health.StarvationGapCount + "; holds " + health.HoldCount + " of " +
            health.HeldBarCount + " bar(s); mode " + health.Mode);
    }

    // NOTHING SLEEPS. The audio device and the engine's own timer are running on the real clock;
    // this waits on them by yielding the thread, and gives up with a message rather than hanging.
    private static async Task WaitFor(Func<bool> until, TimeSpan limit, string what)
    {
        var clock = Stopwatch.StartNew();

        while (!until())
        {
            if (clock.Elapsed > limit)
            {
                throw new InvalidOperationException(
                    "Waited " + limit + " for " + what + " and it did not happen.");
            }

            await Task.Yield();
        }
    }
}
