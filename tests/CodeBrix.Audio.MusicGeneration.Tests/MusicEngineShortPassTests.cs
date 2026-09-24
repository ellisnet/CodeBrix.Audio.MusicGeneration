using System;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A PASS THAT BREAKS OFF WITHOUT SETTLING ITS LAST NOTE never stops the music for ever: whatever
/// it still holds is let out when it ends, and a render always finishes - or fails, saying why.
/// This is the offline render that hung on the real MuseCoco model.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class MusicEngineShortPassTests
{
    private const int SampleRate = 22050;

    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicEngineShortPassTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public async Task a_render_through_passes_that_stop_short_reaches_its_length(double crossfadeSeconds)
    {
        //Arrange
        var generator = new ShortEndingMusicGenerator("ShortEnding", throwAtTheEnd: false);
        MusicGeneratorRegistry.Register(generator);
        using var folder = new RenderOutputFolder("short-passes");
        using var session = new MusicSession(Options(generator.Name, crossfadeSeconds),
            TestInstrumentLibraries.Lookup(), TimeProvider.System);

        //Act - with a watchdog, so a hang fails instead of hanging the suite
        var result = await session.RenderToFileAsync(folder.File("short.wav"), new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(20.0), Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60.0),
            TestContext.Current.CancellationToken);

        //Assert - every pass's last note was played, and the music went on to the length asked for
        result.ReachedTargetLength.Should().BeTrue();
        generator.PassCount.Should().BeGreaterThan(4);
        result.Music.SelectMany(track => track).OfType<NoteOnEvent>()
            .Count(note => note.Velocity > 0 && note.NoteNumber == 67).Should().BeGreaterThan(3);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public async Task a_render_through_a_pass_that_fails_holding_a_note_reports_the_failure(double crossfadeSeconds)
    {
        //Arrange
        var generator = new ShortEndingMusicGenerator("Failing", throwAtTheEnd: true);
        MusicGeneratorRegistry.Register(generator);
        using var folder = new RenderOutputFolder("failing-pass");
        using var session = new MusicSession(Options(generator.Name, crossfadeSeconds),
            TestInstrumentLibraries.Lookup(), TimeProvider.System);

        //Act
        Func<Task> render = () => session.RenderToFileAsync(folder.File("failing.wav"), new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(20.0), Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60.0),
            TestContext.Current.CancellationToken);

        //Assert - it FAILS, by name, rather than waiting for ever
        (await render.Should().ThrowAsync<MusicGenerationException>())
            .Which.Message.Should().Contain("The stream broke off.");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public async Task a_pass_that_fails_once_is_followed_by_a_fresh_piece_and_the_render_reaches_its_length(
        double crossfadeSeconds)
    {
        //Arrange - the third pass throws part-way (as MuseCoco does when a piece needs more than
        //fifteen melodic channels); every other pass is whole
        var generator = new ShortEndingMusicGenerator("FailsOnce", throwAtTheEnd: false) { OnlyPassThatThrows = 3 };
        MusicGeneratorRegistry.Register(generator);
        using var folder = new RenderOutputFolder("fails-once");
        using var session = new MusicSession(Options(generator.Name, crossfadeSeconds),
            TestInstrumentLibraries.Lookup(), TimeProvider.System);

        //Act
        var result = await session.RenderToFileAsync(folder.File("once.wav"), new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(20.0), Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60.0),
            TestContext.Current.CancellationToken);

        //Assert
        result.ReachedTargetLength.Should().BeTrue();
        result.Diagnostics.Should().Contain(line => line.Contains("failed and the music went on with a fresh piece") &&
                                                    line.Contains("The stream broke off."));
    }

    private static MusicGenerationOptions Options(string generator, double crossfadeSeconds) =>
        new MusicGenerationOptions
        {
            Generator = generator,
            InstrumentLibrary = TestInstrumentLibraries.ConstantTone().Name,
            SampleRate = SampleRate,
            SegmentPriming = SegmentPriming.Fresh,
            SeamCrossfade = TimeSpan.FromSeconds(crossfadeSeconds),
            TempoPolicy = SessionTempoPolicy.CarryOutsideBand
        };
}
