using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.Opus;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// <c>.opus</c> THROUGH THE WRITER REGISTRY, which is the whole point of the registry: this
/// library never references the Opus package, and an application that does - as this test project
/// does - reaches the format by calling that package's <c>Register()</c> and naming a file.
/// </summary>
/// <remarks>
/// FORMAT SUPPORT IS A REGISTRATION QUESTION, NOT A DEPENDENCY QUESTION. The shipped library has
/// exactly three package references and none of them is CodeBrix.Audio.Opus; everything here works
/// because a CONSUMER added the encoder.
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class OpusRenderTests
{
    private const int Resolution = 480;
    private const int SampleRate = 48000;
    private const string Piece = "OpusPiece";

    /// <summary>Starts from freshly built built-ins, and makes sure the encoder is registered.</summary>
    public OpusRenderTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();

        // ONE CALL, from the application. It is idempotent, and it teaches CodeBrix.Audio's reader
        // AND writer registries about .opus.
        CodeBrixAudioOpus.Register();
    }

    [Fact]
    public async Task an_opus_file_is_written_by_its_extension_and_reads_back_at_the_right_length()
    {
        //Arrange
        using var folder = new RenderOutputFolder("opus-file");
        using var session = Session();

        var path = folder.File("music.opus");

        //Act
        var result = await session.RenderToFileAsync(path, new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(3.0),
            Ending = RenderEnding.Fade,
            FadeLength = TimeSpan.FromSeconds(1.0)
        }, TestContext.Current.CancellationToken);

        //Assert - the render itself is exact; the FILE is within the codec's own tolerance
        result.Format.Should().Be(".opus");
        result.FrameCount.Should().Be(3L * SampleRate);

        using var reader = new OpusFileReader(path);

        reader.WaveFormat.Channels.Should().Be(2);
        reader.EncoderInputSampleRate.Should().Be(SampleRate);
        reader.TotalTime.Should().BeCloseTo(TimeSpan.FromSeconds(3.0), TimeSpan.FromMilliseconds(100.0));
        PeakOf(reader).Should().BeGreaterThan(0.001F);
    }

    [Fact]
    public async Task a_stream_that_cannot_seek_is_accepted_for_opus()
    {
        //Arrange - what a WAV refuses, because Opus is written strictly forwards
        using var session = Session();
        using var forwardOnly = new ForwardOnlyStream();

        //Act
        var result = await session.RenderToStreamAsync(forwardOnly, "broadcast.opus",
            new MusicRenderOptions
            {
                TargetLength = TimeSpan.FromSeconds(1.0),
                Ending = RenderEnding.HardCut
            }, TestContext.Current.CancellationToken);

        //Assert
        result.Format.Should().Be(".opus");
        result.FrameCount.Should().Be(SampleRate);
        forwardOnly.BytesWritten.Should().BeGreaterThan(0L);

        using var written = new MemoryStream(forwardOnly.ToArray());
        using var reader = new OpusFileReader(written);

        reader.TotalTime.Should().BeCloseTo(TimeSpan.FromSeconds(1.0), TimeSpan.FromMilliseconds(100.0));
    }

    [Fact]
    public void the_registry_is_what_knows_about_opus_at_all()
    {
        //Assert - the road a render takes to reach a format nothing in this library names
        AudioFileWriterRegistry.Supports(".opus").Should().BeTrue();
        AudioFileWriterRegistry.Resolve("music.opus").RequiresSeekableStream.Should().BeFalse();
    }

    private static MusicSession Session()
    {
        MusicGeneratorRegistry.Register(
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(4, Resolution)));

        var options = new MusicGenerationOptions
        {
            Generator = Piece,
            InstrumentLibrary = TestInstrumentLibraries.ConstantTone().Name,
            SampleRate = SampleRate
        };

        return new MusicSession(options, TestInstrumentLibraries.Lookup(), TimeProvider.System);
    }

    private static float PeakOf(OpusFileReader reader)
    {
        var buffer = new byte[4096];
        var peak = 0.0F;
        var read = reader.Read(buffer, 0, buffer.Length);

        while (read > 0)
        {
            for (var index = 0; index + 4 <= read; index += 4)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(buffer, index)));
            }

            read = reader.Read(buffer, 0, buffer.Length);
        }

        return peak;
    }
}
