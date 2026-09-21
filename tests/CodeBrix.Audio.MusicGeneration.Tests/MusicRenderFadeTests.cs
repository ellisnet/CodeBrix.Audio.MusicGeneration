using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE SHAPE OF A FADE IN A FILE THAT WAS REALLY RENDERED, asserted sample by sample against the
/// curve function itself.
/// </summary>
/// <remarks>
/// It is measurable because the instruments hold ONE CONSTANT LEVEL - see
/// <see cref="ConstantToneInstrumentLibrary"/> - so every sample of the file is the level times
/// the fade's own gain, and nothing else. Over a real instrument the same file would be the sum of
/// an envelope, an oscillator and a release tail, and the fade could not be read out of it.
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MusicRenderFadeTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;
    private const string Piece = "FadePiece";

    private static readonly TimeSpan Target = TimeSpan.FromSeconds(4.0);
    private static readonly TimeSpan Fade = TimeSpan.FromSeconds(2.0);

    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicRenderFadeTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Theory]
    [InlineData(MusicFadeCurve.EasedDecibels)]
    [InlineData(MusicFadeCurve.StraightLine)]
    [InlineData(MusicFadeCurve.EqualPower)]
    public async Task the_fade_in_the_file_follows_the_curve_it_was_given(MusicFadeCurve curve)
    {
        //Arrange
        using var folder = new RenderOutputFolder("fade-shape");
        using var session = Session();

        var path = folder.File($"fade-{curve}.wav");

        //Act
        var result = await session.RenderToFileAsync(path, Options(curve),
            TestContext.Current.CancellationToken);

        //Assert
        var frames = ReadLeftChannel(path);
        var fadeFrames = (long)(Fade.TotalSeconds * SampleRate);
        var fadeFrom = result.FrameCount - fadeFrames;
        var level = frames[0];

        level.Should().BeGreaterThan(0.01F, "the instruments hold a constant, audible level");
        result.FadeLength.Should().Be(Fade);
        result.FadeCurve.Should().Be(curve);

        // Before the fade, every sample is the same level: nothing has been touched.
        frames[(int)fadeFrom - 1].Should().Be(level);

        // Inside it, every sample is the level times the curve, to four decimal places.
        for (var frame = fadeFrom; frame < result.FrameCount; frame += 97L)
        {
            var progress = (double)(frame - fadeFrom) / (fadeFrames - 1L);
            var expected = level * MusicFade.GainAt(curve, progress);

            frames[(int)frame].Should().BeApproximately(expected, 0.0001F);
        }
    }

    [Theory]
    [InlineData(MusicFadeCurve.EasedDecibels)]
    [InlineData(MusicFadeCurve.StraightLine)]
    [InlineData(MusicFadeCurve.EqualPower)]
    public async Task the_fade_reaches_silence_at_the_very_last_frame(MusicFadeCurve curve)
    {
        //Arrange
        using var folder = new RenderOutputFolder("fade-silence");
        using var session = Session();

        var path = folder.File($"silence-{curve}.wav");

        //Act
        var result = await session.RenderToFileAsync(path, Options(curve),
            TestContext.Current.CancellationToken);

        //Assert
        var frames = ReadLeftChannel(path);

        frames.Length.Should().Be((int)result.FrameCount);
        frames[frames.Length - 1].Should().Be(0.0F);
        Math.Abs(frames[frames.Length - 2]).Should().BeLessThan(Math.Abs(frames[0]));
    }

    [Fact]
    public async Task a_fade_longer_than_the_file_is_clamped_to_the_whole_of_it()
    {
        //Arrange - a fade of a minute over a file of four seconds
        using var folder = new RenderOutputFolder("fade-clamped");
        using var session = Session();

        var path = folder.File("clamped.wav");

        //Act
        var result = await session.RenderToFileAsync(path, new MusicRenderOptions
        {
            TargetLength = Target,
            Ending = RenderEnding.Fade,
            FadeLength = TimeSpan.FromMinutes(1.0),
            FadeCurve = MusicFadeCurve.StraightLine
        }, TestContext.Current.CancellationToken);

        //Assert - CLAMPED, NOT REFUSED: the whole file fades, and the render says so
        result.FrameCount.Should().Be(4L * SampleRate);
        result.FadeLength.Should().Be(Target);
        result.Diagnostics.Should().Contain(line => line.Contains("clamped"));

        var frames = ReadLeftChannel(path);
        var level = frames[0];

        level.Should().BeGreaterThan(0.01F);
        frames[frames.Length / 2].Should().BeApproximately(level * 0.5F, 0.001F);
        frames[frames.Length - 1].Should().Be(0.0F);
    }

    [Fact]
    public async Task a_fade_with_nothing_said_uses_the_default_length_and_curve()
    {
        //Arrange
        using var folder = new RenderOutputFolder("fade-default");
        using var session = Session();

        var path = folder.File("default.wav");

        //Act - a target length, and nothing else said at all
        var result = await session.RenderToFileAsync(path,
            new MusicRenderOptions { TargetLength = TimeSpan.FromSeconds(8.0) },
            TestContext.Current.CancellationToken);

        //Assert
        result.Ending.Should().Be(RenderEnding.Fade, "a target length asks to be faded");
        result.FadeLength.Should().Be(MusicFade.DefaultLength);
        result.FadeCurve.Should().Be(MusicFade.DefaultCurve);

        var frames = ReadLeftChannel(path);
        var fadeFrom = (int)result.FrameCount - (int)(MusicFade.DefaultLength.TotalSeconds * SampleRate);

        frames[fadeFrom - 1].Should().Be(frames[0]);
        frames[frames.Length - 1].Should().Be(0.0F);
    }

    private static MusicRenderOptions Options(MusicFadeCurve curve) =>
        new MusicRenderOptions
        {
            TargetLength = Target,
            Ending = RenderEnding.Fade,
            FadeLength = Fade,
            FadeCurve = curve
        };

    private static MusicSession Session()
    {
        // Eight bars, so the whole file is one segment and the level cannot change at a seam.
        MusicGeneratorRegistry.Register(
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(8, Resolution)));

        var options = new MusicGenerationOptions
        {
            Generator = Piece,
            InstrumentLibrary = TestInstrumentLibraries.ConstantTone().Name,
            SampleRate = SampleRate
        };

        return new MusicSession(options, TestInstrumentLibraries.Lookup(), TimeProvider.System);
    }

    private static float[] ReadLeftChannel(string path)
    {
        using var reader = new WaveFileReader(path);

        var frames = new float[reader.SampleCount];
        var frame = reader.ReadNextSampleFrame();
        var index = 0;

        while (frame != null && index < frames.Length)
        {
            frames[index++] = frame[0];
            frame = reader.ReadNextSampleFrame();
        }

        return frames;
    }
}
