using System;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendering;
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
/// The session tempo as an application meets it: the options, the diagnostics, and a rendered
/// file carrying fresh pieces exactly as a live session does.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class MusicSessionTempoTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;
    private const string Pieces = "TempoPieces";
    private const long PieceTicks = 2L * 4L * Resolution;

    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicSessionTempoTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public void the_tempo_options_change_nothing_with_nothing_said()
    {
        //Arrange
        var options = new MusicGenerationOptions();

        //Assert
        options.TempoPolicy.Should().Be(SessionTempoPolicy.Adopt);
        options.TempoBand.Should().Be(MusicGenerationOptions.DefaultTempoBand);
        MusicGenerationOptions.DefaultTempoBand.Should().Be(0.15);
        options.SessionBeatsPerMinute.Should().BeNull();
    }

    [Fact]
    public void the_tempo_options_refuse_what_is_not_a_tempo_and_are_copied()
    {
        //Arrange
        var options = new MusicGenerationOptions
        {
            TempoPolicy = SessionTempoPolicy.CarryOutsideBand,
            TempoBand = 0.1,
            SessionBeatsPerMinute = 96.0
        };

        //Act
        Action negativeBand = () => options.TempoBand = -0.1;
        Action zeroTempo = () => options.SessionBeatsPerMinute = 0.0;
        var copy = options.Clone();

        //Assert
        negativeBand.Should().Throw<ArgumentOutOfRangeException>();
        zeroTempo.Should().Throw<ArgumentOutOfRangeException>();
        copy.TempoPolicy.Should().Be(SessionTempoPolicy.CarryOutsideBand);
        copy.TempoBand.Should().Be(0.1);
        copy.SessionBeatsPerMinute.Should().Be(96.0);
    }

    [Fact]
    public async Task a_live_session_reports_the_session_tempo_and_what_it_carried()
    {
        //Arrange
        var time = new ManualTimeProvider();
        Register(time);
        var options = Options(SessionTempoPolicy.CarryOutsideBand);
        options.ApplicationOwnsAudioOutput = true;

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(), time);
        session.Play();

        //Act
        await EnginePump.RunUntilAsync(time, () => session.Diagnostics.CarriedTempoCount >= 1 &&
                                                   session.Diagnostics.AdoptedTempoCount >= 1);
        var diagnostics = session.Diagnostics;

        //Assert
        diagnostics.SessionTempo.Should().BeApproximately(120.0, 0.01);
        diagnostics.ToString().Should().Contain("session tempo 120.0");
    }

    [Fact]
    public async Task a_rendered_file_carries_fresh_pieces_exactly_as_a_live_session_does()
    {
        //Arrange - 120, then 126 (within the band), then 180 changing to 200 (outside it)
        Register(null);
        using var folder = new RenderOutputFolder("session-tempo");
        using var session = new MusicSession(Options(SessionTempoPolicy.CarryOutsideBand),
            TestInstrumentLibraries.Lookup(), TimeProvider.System);

        //Act
        var result = await session.RenderToFileAsync(folder.File("tempo.wav"), new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(11.0),
            Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken);

        //Assert - the same tempos the engine puts on a live timeline, and every note at its tick
        var events = result.Music.SelectMany(track => track).ToArray();
        var tempos = events.OfType<TempoEvent>()
            .Select(tempo => (tempo.AbsoluteTime, Math.Round(60000000.0 / tempo.MicrosecondsPerQuarterNote, 1)))
            .Where(tempo => tempo.AbsoluteTime < 3L * PieceTicks)
            .ToArray();

        result.SeamTicks.Take(3).Should().Equal(0L, PieceTicks, 2L * PieceTicks);
        result.Diagnostics.Should().Contain(line => line.Contains("tick 0: 120.0 bpm sets the session tempo"));
        result.Diagnostics.Should().Contain(line => line.Contains("tick 3840: written at 126.0 bpm, adopted"));
        result.Diagnostics.Should().Contain(line => line.Contains("tick 7680: written at 180.0 bpm, carried to the session tempo of 120.0"));
        tempos.Should().Equal((0L, 120.0), (PieceTicks, 126.0), (2L * PieceTicks, 120.0));
        events.OfType<NoteOnEvent>().Where(note => note.Velocity > 0 && note.AbsoluteTime < 3L * PieceTicks)
            .Select(note => note.AbsoluteTime)
            .Should().Equal(Enumerable.Range(0, 24).Select(i => i * (long)Resolution));
    }

    [Fact]
    public async Task a_render_finishes_when_a_fresh_piece_never_sounds_a_note()
    {
        //Arrange - notes, then a long stretch of silence that never sounds a note, then notes
        MusicGeneratorRegistry.Register(new SequenceMusicGenerator(Pieces, new[]
        {
            ReplayMusicGenerator.FromMidiEvents(Pieces, MusicEngineSessionTempoTests.Piece(120.0)),
            ReplayMusicGenerator.FromMidiEvents(Pieces, MusicEngineSessionTempoTests.LongSilence(60)),
            ReplayMusicGenerator.FromMidiEvents(Pieces, MusicEngineSessionTempoTests.Piece(180.0))
        }));
        using var folder = new RenderOutputFolder("session-tempo-silence");
        var options = Options(SessionTempoPolicy.CarryOutsideBand);
        options.SeamCrossfade = TimeSpan.FromSeconds(1.0);
        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(), TimeProvider.System);

        //Act - a WATCHDOG: a render that stops making progress fails here instead of hanging
        var render = session.RenderToFileAsync(folder.File("silence.wav"), new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(30.0),
            Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken);
        var result = await render.WaitAsync(TimeSpan.FromSeconds(60.0), TestContext.Current.CancellationToken);

        //Assert
        result.ReachedTargetLength.Should().BeTrue();
        result.Duration.Should().Be(TimeSpan.FromSeconds(30.0));
    }

    private static void Register(ManualTimeProvider time)
    {
        var pieces = new[]
        {
            MusicEngineSessionTempoTests.Piece(120.0),
            MusicEngineSessionTempoTests.Piece(126.0),
            MusicEngineSessionTempoTests.Piece(180.0, changeTo: 200.0)
        }.Select(piece =>
        {
            var replay = ReplayMusicGenerator.FromMidiEvents(Pieces, piece);

            return time == null ? replay : ReplayOnTestClock.On(replay, time, 8.0);
        }).ToArray();

        MusicGeneratorRegistry.Register(new SequenceMusicGenerator(Pieces, pieces));
    }

    private static MusicGenerationOptions Options(SessionTempoPolicy policy) =>
        new MusicGenerationOptions
        {
            Generator = Pieces,
            InstrumentLibrary = TestInstrumentLibraries.ConstantTone().Name,
            SampleRate = SampleRate,
            SegmentPriming = SegmentPriming.Fresh,
            TempoPolicy = policy
        };
}
