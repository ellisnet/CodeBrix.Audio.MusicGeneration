using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A CROSSFADE AT A FRESH SEAM, HEARD: through a session whose application owns its audio output,
/// and in a file rendered offline - the same mixer either way, so the two must sound the same.
/// </summary>
/// <remarks>
/// <para>
/// The instruments hold ONE CONSTANT LEVEL - see <see cref="ConstantToneInstrumentLibrary"/> - and
/// every segment is the same two bars voiced the same way, so the outgoing and incoming pieces play
/// at the same level. What a crossfade does to that level is then exactly the curve: an
/// equal-power fade of two equal levels rises to the square root of two half-way through, and a
/// straight-line fade stays flat.
/// </para>
/// <para>
/// The piece is two bars of quarter notes at 120 beats per minute, so it is four seconds long. With
/// a one-second crossfade each fresh piece starts at three seconds into the one before it: the
/// first fade runs from 3 s to 4 s, the second from 6 s to 7 s.
/// </para>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MusicSessionCrossfadeTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;
    private const string Piece = "CrossfadePiece";

    private static readonly TimeSpan Fade = TimeSpan.FromSeconds(1.0);

    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicSessionCrossfadeTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public async Task a_rendered_equal_power_crossfade_rises_to_the_square_root_of_two_half_way()
    {
        //Arrange
        using var folder = new RenderOutputFolder("crossfade-equal-power");
        using var session = RenderSession(MusicFadeCurve.EqualPower);
        var path = folder.File("equal-power.wav");

        //Act
        var result = await session.RenderToFileAsync(path, RenderOptions(),
            TestContext.Current.CancellationToken);

        //Assert
        var frames = ReadLeftChannel(path);
        var level = frames[At(1.0)];

        level.Should().BeGreaterThan(0.01F);
        frames[At(2.5)].Should().Be(level);
        frames[At(3.5)].Should().BeApproximately(level * (float)Math.Sqrt(2.0), 0.001F);
        frames[At(4.5)].Should().BeApproximately(level, 0.00001F);
        frames[At(6.5)].Should().BeApproximately(level * (float)Math.Sqrt(2.0), 0.001F);
        result.SeamTicks.Take(3).Should().Equal(0L, 2880L, 5760L);
    }

    [Fact]
    public async Task a_rendered_straight_line_crossfade_of_two_equal_levels_stays_flat()
    {
        //Arrange
        using var folder = new RenderOutputFolder("crossfade-straight");
        using var session = RenderSession(MusicFadeCurve.StraightLine);
        var path = folder.File("straight.wav");

        //Act
        await session.RenderToFileAsync(path, RenderOptions(), TestContext.Current.CancellationToken);

        //Assert - a sum of gains that is always one, over two equal levels, is the level itself
        var frames = ReadLeftChannel(path);
        var level = frames[At(1.0)];

        for (var second = 2.9; second < 4.2; second += 0.05)
        {
            frames[At(second)].Should().BeApproximately(level, 0.0001F);
        }
    }

    [Fact]
    public async Task the_midi_of_a_render_has_both_pieces_at_the_seam_and_no_cue()
    {
        //Arrange
        using var folder = new RenderOutputFolder("crossfade-midi");
        using var session = RenderSession(MusicFadeCurve.EqualPower);

        //Act
        var result = await session.RenderToFileAsync(folder.File("midi.wav"), RenderOptions(),
            TestContext.Current.CancellationToken);

        //Assert
        var events = result.Music.SelectMany(track => track).ToArray();
        events.OfType<ControlChangeEvent>().Should().BeEmpty();

        // At the first seam the incoming piece's first note AND the outgoing piece's second-last one
        // both start at tick 2880 - they overlapped, and the file says so.
        events.OfType<NoteOnEvent>().Count(note => note.AbsoluteTime == 2880L && note.Velocity > 0)
            .Should().Be(2);
        result.Diagnostics.Should().NotContain(line => line.Contains("crossfade"));
    }

    [Fact]
    public async Task a_session_that_owns_its_output_hears_the_same_crossfade_with_no_gap()
    {
        //Arrange
        var time = new ManualTimeProvider();
        MusicGeneratorRegistry.Register(ReplayOnTestClock.On(
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(2, Resolution)), time, 8.0));

        var options = Options(MusicFadeCurve.EqualPower);
        options.ApplicationOwnsAudioOutput = true;
        options.Preroll = TimeSpan.FromSeconds(0.25);
        options.GenerateAhead = TimeSpan.FromSeconds(8.0);

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(), time);
        session.Play();

        //Act - listen in real time on the test's clock: every step renders exactly as much audio as
        //the clock moved on by, the way a sound card pulls it
        var heard = new List<float>();
        var buffer = new float[SampleRate];
        var spare = new float[SampleRate];
        var last = time.GetUtcNow();

        for (var step = 0; step < 20000 && heard.Count < At(7.5); step++)
        {
            await EnginePump.StepAsync(time);

            var now = time.GetUtcNow();
            var frames = (int)Math.Min(SampleRate, (now - last).TotalSeconds * SampleRate);

            if (frames > 0)
            {
                session.Renderer.Render(buffer.AsSpan(0, frames), spare.AsSpan(0, frames));
                heard.AddRange(buffer.Take(frames));
                last = last + TimeSpan.FromSeconds((double)frames / SampleRate);
            }
        }

        //Assert
        var diagnostics = session.Diagnostics;
        var level = heard[At(1.0)];

        heard.Count.Should().BeGreaterThanOrEqualTo(At(7.5));
        diagnostics.CrossfadeCount.Should().BeGreaterThanOrEqualTo(2);
        diagnostics.ShortenedCrossfadeCount.Should().Be(0);
        diagnostics.StarvationGapCount.Should().Be(0);
        diagnostics.FreshSegmentCount.Should().BeGreaterThanOrEqualTo(2);
        diagnostics.GeneratingSegmentKind.Should().Be(MusicSegmentKind.Fresh);
        diagnostics.ToString().Should().Contain("crossfade");
        session.GenerationError.Should().BeNull();

        // The head waited for its first pre-roll before it moved, so the fade is a little later in
        // what was heard than on the timeline: its PEAK is what is asserted, and the level either side.
        heard[At(2.5)].Should().Be(level);
        heard.Take(At(5.0)).Max().Should().BeApproximately(level * (float)Math.Sqrt(2.0), 0.001F);
        heard[At(4.8)].Should().BeApproximately(level, 0.00001F);
    }

    [Fact]
    public async Task a_rendered_crossfade_skips_a_fresh_pieces_silent_opening_bars_as_a_session_does()
    {
        //Arrange - six bars: two silent ones, then four of quarter notes; one-second fade
        MusicGeneratorRegistry.Register(ReplayMusicGenerator.FromMidiEvents(Piece, TwoSilentBarsThenMusic()));
        using var folder = new RenderOutputFolder("crossfade-silent-bars");
        using var session = new MusicSession(Options(MusicFadeCurve.EqualPower), TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        //Act
        var result = await session.RenderToFileAsync(folder.File("silent-bars.wav"), new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(16.0),
            Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken);

        //Assert - the incoming piece's first note is AT the start of the fade, beside the outgoing
        //piece's own note there; without the skip it would be two bars later
        var seam = result.SeamTicks[1];
        var notes = result.Music.SelectMany(track => track).OfType<NoteOnEvent>()
            .Where(note => note.Velocity > 0).ToArray();

        seam.Should().Be((6L * 1920L) - 960L);
        notes.Count(note => note.AbsoluteTime == seam).Should().Be(2);
        notes.Count(note => note.AbsoluteTime == seam + 480L).Should().Be(2);
    }

    [Fact]
    public async Task a_render_behind_a_slow_generator_still_gives_every_fresh_seam_its_whole_fade()
    {
        //Arrange - a generator far slower than the render, so the render is always right behind
        //the music it is given: the case in which a render used to shorten the fade
        MusicGeneratorRegistry.Register(new SlowMusicGenerator(Piece,
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(2, Resolution)), TimeSpan.FromMilliseconds(15.0)));
        using var folder = new RenderOutputFolder("crossfade-slow-generator");
        using var session = new MusicSession(Options(MusicFadeCurve.EqualPower), TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        //Act
        var result = await session.RenderToFileAsync(folder.File("slow.wav"), RenderOptions(),
            TestContext.Current.CancellationToken);

        //Assert - both fresh seams a whole second before the bar line; nothing shortened
        result.SeamTicks.Take(3).Should().Equal(0L, 2880L, 5760L);
        result.Diagnostics.Should().NotContain(line => line.Contains("crossfade"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(4.0)]
    public async Task a_crossfade_changes_nothing_about_how_the_music_starts(double crossfadeSeconds)
    {
        //Arrange - the same piece, with and without a crossfade asked for
        var time = new ManualTimeProvider();
        MusicGeneratorRegistry.Register(ReplayOnTestClock.On(
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(2, Resolution)), time, 8.0));
        var options = Options(MusicFadeCurve.EqualPower);
        options.SeamCrossfade = TimeSpan.FromSeconds(crossfadeSeconds);
        options.ApplicationOwnsAudioOutput = true;
        options.Preroll = TimeSpan.FromSeconds(0.5);

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(), time);
        var started = time.GetUtcNow();

        //Act - Play returns having asked for preparation, not having done it
        session.Play();
        var preparedWhenPlayReturned = session.Engine.CrossfadePreparationStarted;
        await EnginePump.RunUntilAsync(time, () => session.Stream.HorizonTime >= options.Preroll);
        var firstPreroll = time.GetUtcNow() - started;
        await EnginePump.RunUntilAsync(time, () => session.Engine.CrossfadePreparationStarted ||
                                                   crossfadeSeconds == 0.0);
        await session.Engine.CrossfadePreparation.WaitAsync(TimeSpan.FromSeconds(30.0),
            TestContext.Current.CancellationToken);

        //Assert - the first pre-roll is written within the pre-roll's own time on the test clock
        //(the generator writes eight times faster than real time), and the stream never left
        //streaming delivery
        preparedWhenPlayReturned.Should().BeFalse();
        firstPreroll.Should().BeLessThanOrEqualTo(options.Preroll);
        session.Diagnostics.Mode.Should().Be(MusicDeliveryMode.Streaming);
        (session.Engine.Mixer != null && session.Engine.Mixer.IsWarmedUp).Should().Be(crossfadeSeconds > 0.0);
    }

    private static MidiEventCollection TwoSilentBarsThenMusic()
    {
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();
        music.AddEvent(new TempoEvent(500000, 0L), 0);
        music.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);

        for (var beat = 8; beat < 24; beat++)
        {
            var note = new NoteOnEvent(beat * (long)Resolution, 1, 60 + (beat % 12), 100, Resolution);
            music.AddEvent(note, 0);
            music.AddEvent(note.OffEvent, 0);
        }

        return music;
    }

    private static int At(double seconds) => (int)(seconds * SampleRate);

    private static MusicGenerationOptions Options(MusicFadeCurve curve) =>
        new MusicGenerationOptions
        {
            Generator = Piece,
            InstrumentLibrary = TestInstrumentLibraries.ConstantTone().Name,
            SampleRate = SampleRate,
            SegmentPriming = SegmentPriming.Fresh,
            SeamCrossfade = Fade,
            SeamCrossfadeCurve = curve
        };

    private static MusicRenderOptions RenderOptions() =>
        new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(8.0),
            Ending = RenderEnding.HardCut
        };

    private static MusicSession RenderSession(MusicFadeCurve curve)
    {
        MusicGeneratorRegistry.Register(
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(2, Resolution)));

        return new MusicSession(Options(curve), TestInstrumentLibraries.Lookup(), TimeProvider.System);
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
