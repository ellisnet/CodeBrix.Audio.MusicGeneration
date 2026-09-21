using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.ModestSynth;
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
/// THE LONG RENDER, which is the plan's own DONE criterion: six minutes twenty-five seconds of
/// generated music with a fade, as a <c>.wav</c> and as a <c>.opus</c>, both exactly that long.
/// </summary>
/// <remarks>
/// <para>
/// GATED BY TIME, not by sound: it opens no audio device, but it renders nearly four hundred
/// seconds of audio through the General MIDI bank and writes over a hundred megabytes, which is
/// not something an ordinary suite run should do. Opt in with
/// CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS=1.
/// </para>
/// <code>
/// CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS=1 \
///   dotnet tests/CodeBrix.Audio.MusicGeneration.Tests/bin/Release/net10.0/CodeBrix.Audio.MusicGeneration.Tests.dll \
///   -class "CodeBrix.Audio.MusicGeneration.Tests.LongRenderTests"
/// </code>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class LongRenderTests
{
    private static readonly bool LongRendersEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS") == "1";

    private const string SkipReason =
        "Set CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS=1 to run renders that take minutes and write " +
        "hundreds of megabytes.";

    private const int SampleRate = 44100;

    private static readonly TimeSpan SixMinutesTwentyFiveSeconds = TimeSpan.FromSeconds(385.0);

    /// <summary>Starts from freshly built built-ins, because both tables are process-wide.</summary>
    public LongRenderTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public async Task six_minutes_twenty_five_seconds_with_a_fade_is_exactly_that_long()
    {
        Assert.SkipUnless(LongRendersEnabled, SkipReason);

        //Arrange - the whole of the done criterion: one registration line, nothing else specified,
        //and the Opus package's own Register() for the second format
        GeneralMidiInstrumentLibrary.Register();
        CodeBrixAudioOpus.Register();

        using var folder = new RenderOutputFolder("six-twenty-five");

        var options = new MusicGenerationOptions
        {
            InstrumentLibrary = GeneralMidiInstrumentLibrary.LibraryName,
            SampleRate = SampleRate
        };

        using var music = new MusicSession(options, TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        var render = new MusicRenderOptions { TargetLength = SixMinutesTwentyFiveSeconds };

        var wavPath = folder.File("six-twenty-five.wav");
        var opusPath = folder.File("six-twenty-five.opus");

        //Act
        var startedWav = Stopwatch.GetTimestamp();
        var wav = await music.RenderToFileAsync(wavPath, render,
            TestContext.Current.CancellationToken);
        var wavTook = Stopwatch.GetElapsedTime(startedWav);

        var startedOpus = Stopwatch.GetTimestamp();
        var opus = await music.RenderToFileAsync(opusPath, render,
            TestContext.Current.CancellationToken);
        var opusTook = Stopwatch.GetElapsedTime(startedOpus);

        //Assert - the WAV is exact to the frame
        wav.FrameCount.Should().Be(385L * SampleRate);
        wav.Duration.Should().Be(SixMinutesTwentyFiveSeconds);
        wav.ReachedTargetLength.Should().BeTrue();
        wav.Ending.Should().Be(RenderEnding.Fade);
        wav.FadeLength.Should().Be(MusicFade.DefaultLength);
        wav.FadeCurve.Should().Be(MusicFade.DefaultCurve);
        wav.SegmentCount.Should().BeGreaterThanOrEqualTo(3, "the embedded piece is about 2:05 long");
        wav.Source.IsReplay.Should().BeTrue();
        wav.Source.InstrumentLibraryName.Should().Be(GeneralMidiInstrumentLibrary.LibraryName);

        using (var reader = new WaveFileReader(wavPath))
        {
            reader.SampleCount.Should().Be(385L * SampleRate);
            reader.WaveFormat.BitsPerSample.Should().Be(32);
            reader.TotalTime.Should().BeCloseTo(SixMinutesTwentyFiveSeconds,
                TimeSpan.FromMilliseconds(1.0));
        }

        //... and the Opus file is the same length within the codec's own tolerance
        opus.FrameCount.Should().Be(385L * SampleRate);

        using (var reader = new OpusFileReader(opusPath))
        {
            reader.TotalTime.Should().BeCloseTo(SixMinutesTwentyFiveSeconds,
                TimeSpan.FromMilliseconds(250.0));
            reader.WaveFormat.Channels.Should().Be(2);
        }

        Report("wav", wav, wavTook);
        Report("opus", opus, opusTook);
    }

    private static void Report(string what, MusicRenderResult result, TimeSpan took) =>
        TestContext.Current.TestOutputHelper.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} of music in {2:mm\\:ss\\.fff} - {3:F1}x real time, {4} segment(s), seams at {5}",
            what, result.Duration, took, result.Duration.TotalSeconds / took.TotalSeconds,
            result.SegmentCount, string.Join(", ", result.SeamTicks)));
}
