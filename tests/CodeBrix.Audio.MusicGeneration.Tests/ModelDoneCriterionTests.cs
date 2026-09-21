using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.Opus;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE PLAN'S OWN DONE CRITERION, with a real model behind it: a generator that is REGISTERED and
/// SPECIFIED streams while it is still being generated, and six minutes twenty-five seconds of its
/// music with a fade comes out as a <c>.wav</c> and as a <c>.opus</c> of exactly that length.
/// </summary>
/// <remarks>
/// <para>
/// GATED BY TIME, and by the model variables. It generates and synthesizes more than twenty-five
/// minutes of audio across the four long renders. Opt in with
/// CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS=1 and both model variables.
/// </para>
/// <para>
/// IT MAKES NO SOUND. The streaming half owns its own audio output and reads the samples into
/// arrays; the rendering half writes files.
/// </para>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class ModelDoneCriterionTests : IDisposable
{
    private const int SampleRate = 44100;

    private static readonly TimeSpan SixMinutesTwentyFiveSeconds = TimeSpan.FromSeconds(385.0);
    private static readonly TimeSpan HowLongToWaitForMusic = TimeSpan.FromMinutes(5.0);

    private static readonly bool LongRendersEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS") == "1";

    private const string SkipReason =
        "Set CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS=1, with both model variables, to run the " +
        "renders that take minutes and write hundreds of megabytes.";

    private readonly MuPTMusicGenerator mupt;
    private readonly SkyTNTMusicGenerator skytnt;

    /// <summary>
    /// Starts from freshly built built-ins, over generators built with THE SHIPPED DEFAULTS.
    /// </summary>
    /// <remarks>
    /// IT DOES NOT USE THE LIVE-TEST FIXTURES, and that matters: those cap a pass at a few dozen
    /// events so that the ordinary suite stays quick, and the done criterion is about what a
    /// consumer really gets. Building a generator loads nothing.
    /// </remarks>
    public ModelDoneCriterionTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();

        mupt = MuPTModelFile.IsAvailable
            ? new MuPTMusicGenerator("MuPTDefaults", MuPTModelFile.Path)
            : null;

        skytnt = SkyTNTModelBundle.IsAvailable
            ? new SkyTNTMusicGenerator("SkyTNTDefaults", SkyTNTModelBundle.Directory)
            : null;
    }

    /// <summary>Gives both models' memory back when the class is finished with them.</summary>
    public void Dispose()
    {
        mupt?.Dispose();
        skytnt?.Dispose();
    }

    [Fact]
    public async Task a_registered_and_specified_MuPT_plays_while_it_is_still_generating()
    {
        Assert.SkipUnless(LongRendersEnabled, SkipReason);
        Assert.SkipUnless(MuPTModelFile.IsAvailable, MuPTModelFile.SkipReason);

        await PlaysWhileItGenerates(mupt, MuPTPresets.WaltzDuetInAMinor,
            MuPTMusicGenerator.MuPTFamily);
    }

    [Fact]
    public async Task a_registered_and_specified_SkyTNT_plays_while_it_is_still_generating()
    {
        Assert.SkipUnless(LongRendersEnabled, SkipReason);
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        await PlaysWhileItGenerates(skytnt, SkyTNTPresets.ClubArrangement,
            SkyTNTMusicGenerator.SkyTNTFamily);
    }

    [Fact]
    public async Task six_minutes_twenty_five_seconds_of_MuPT_is_exactly_that_long()
    {
        Assert.SkipUnless(LongRendersEnabled, SkipReason);
        Assert.SkipUnless(MuPTModelFile.IsAvailable, MuPTModelFile.SkipReason);

        await SixTwentyFive(mupt, MuPTPresets.WaltzDuetInAMinor, "mupt");
    }

    [Fact]
    public async Task six_minutes_twenty_five_seconds_of_SkyTNT_is_exactly_that_long()
    {
        Assert.SkipUnless(LongRendersEnabled, SkipReason);
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        await SixTwentyFive(skytnt, SkyTNTPresets.ClubArrangement, "skytnt");
    }

    // --- the two halves of the criterion -------------------------------------------------------

    private async Task PlaysWhileItGenerates(IMusicGenerator generator, MusicPreset preset,
        string family)
    {
        //Arrange - registering is not specifying, so the name is both registered AND asked for
        TestInstrumentLibraries.GeneralMidi();
        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions
        {
            Generator = generator.Name,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Rendition = preset.SuggestedRendition ?? BuiltInRenditions.Automatic,
            ApplicationOwnsAudioOutput = true,
            SampleRate = SampleRate,
            Request = preset.CreateRequest()
        };

        options.Request.Seed = 20260921;

        using var session = new MusicSession(options);

        //Act - the DEFAULT pre-roll is five seconds, and the play head does not move until that
        //much music is written ahead of it, so the wait has to clear it before a pull can hear
        //anything at all
        session.Play();

        await WaitFor(() => session.Stream.HorizonTime >= TimeSpan.FromSeconds(8.0) ||
                            session.GenerationError != null);

        session.GenerationError.Should().BeNull();

        var atFirstAudio = session.Stream.HorizonTime;
        var peak = 0.0F;
        var left = new float[SampleRate];
        var right = new float[SampleRate];

        for (var second = 0; second < 3; second++)
        {
            session.Renderer.Render(left, right);
            peak = Math.Max(peak, Peak(left, right));
        }

        await WaitFor(() => session.Stream.HorizonTime > atFirstAudio ||
                            session.GenerationError != null);

        //Assert - real audio came out, it came out of the model that was named, and more music
        //arrived after the first of it had been listened to: that is streaming while generating
        peak.Should().BeGreaterThan(0.001F);
        session.Position.Should().BeGreaterThan(TimeSpan.Zero);
        session.ActiveSource.GeneratorName.Should().Be(generator.Name);
        session.ActiveSource.GeneratorFamily.Should().Be(family);
        session.ActiveSource.IsReplay.Should().BeFalse();
        session.Stream.HorizonTime.Should().BeGreaterThan(atFirstAudio);
        session.Diagnostics.LateEventCount.Should().Be(0);
        session.GenerationError.Should().BeNull();

        // THE DIAGNOSTICS LINE IS REPORTED, NOT ASSERTED, and it is worth saying why: this test
        // waits by spinning, and nothing pulls audio while it waits, so the real-time factor and
        // the delivery mode it prints are distorted by the test's own rig. What is ASSERTED is
        // the criterion itself - music came out of the named model, and more of it arrived after
        // the first of it had been listened to.
        Report(generator.Name + " streaming: " + session.Diagnostics);
    }

    private async Task SixTwentyFive(IMusicGenerator generator, MusicPreset preset, string what)
    {
        //Arrange
        TestInstrumentLibraries.GeneralMidi();
        CodeBrixAudioOpus.Register();
        MusicGeneratorRegistry.Register(generator);

        using var folder = new RenderOutputFolder("six-twenty-five-" + what);

        var options = new MusicGenerationOptions
        {
            Generator = generator.Name,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Rendition = preset.SuggestedRendition ?? BuiltInRenditions.Automatic,
            SampleRate = SampleRate,
            Request = preset.CreateRequest()
        };

        options.Request.Seed = 20260921;

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        var render = new MusicRenderOptions { TargetLength = SixMinutesTwentyFiveSeconds };

        var wavPath = folder.File(what + ".wav");
        var opusPath = folder.File(what + ".opus");

        //Act
        var startedWav = Stopwatch.GetTimestamp();
        var wav = await session.RenderToFileAsync(wavPath, render,
            TestContext.Current.CancellationToken);
        var wavTook = Stopwatch.GetElapsedTime(startedWav);

        var startedOpus = Stopwatch.GetTimestamp();
        var opus = await session.RenderToFileAsync(opusPath, render,
            TestContext.Current.CancellationToken);
        var opusTook = Stopwatch.GetElapsedTime(startedOpus);

        //Assert - exact to the frame, faded, and made by the model that was named
        wav.FrameCount.Should().Be(385L * SampleRate);
        wav.Duration.Should().Be(SixMinutesTwentyFiveSeconds);
        wav.ReachedTargetLength.Should().BeTrue();
        wav.Ending.Should().Be(RenderEnding.Fade);
        wav.FadeLength.Should().Be(MusicFade.DefaultLength);
        wav.FadeCurve.Should().Be(MusicFade.DefaultCurve);
        wav.Source.IsReplay.Should().BeFalse();
        wav.Source.GeneratorName.Should().Be(generator.Name);
        wav.SegmentCount.Should().BeGreaterThan(1, "a model's pass is shorter than six minutes");

        using (var reader = new WaveFileReader(wavPath))
        {
            reader.SampleCount.Should().Be(385L * SampleRate);
        }

        opus.FrameCount.Should().Be(385L * SampleRate);

        using (var reader = new OpusFileReader(opusPath))
        {
            reader.TotalTime.Should().BeCloseTo(SixMinutesTwentyFiveSeconds,
                TimeSpan.FromMilliseconds(250.0));
            reader.WaveFormat.Channels.Should().Be(2);
        }

        Report(what, wav, wavTook);
        Report(what, opus, opusTook);
    }

    // --- the rig -------------------------------------------------------------------------------

    private static void Report(string line) =>
        TestContext.Current.TestOutputHelper.WriteLine(line);

    private static void Report(string what, MusicRenderResult result, TimeSpan took) =>
        Report(string.Format(CultureInfo.InvariantCulture,
            "{0}{1}: {2:mm\\:ss\\.fff} of music in {3:mm\\:ss\\.fff} - {4:F2}x real time, " +
            "{5} segment(s), seams at {6}",
            what, result.Format, result.Duration, took,
            result.Duration.TotalSeconds / took.TotalSeconds, result.SegmentCount,
            string.Join(", ", result.SeamTicks)));

    private static float Peak(float[] left, float[] right)
    {
        var peak = 0.0F;

        for (var index = 0; index < left.Length; index++)
        {
            peak = Math.Max(peak, Math.Max(Math.Abs(left[index]), Math.Abs(right[index])));
        }

        return peak;
    }

    private static async Task WaitFor(Func<bool> until)
    {
        var started = Stopwatch.GetTimestamp();

        while (!until())
        {
            if (Stopwatch.GetElapsedTime(started) > HowLongToWaitForMusic)
            {
                throw new TimeoutException("The music never arrived.");
            }

            await Task.Yield();
        }
    }
}
