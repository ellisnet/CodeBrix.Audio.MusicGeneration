using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE SESSION TEMPO: whether a fresh piece plays at the tempo it was written at or at the tempo
/// the music has been keeping, and what reaches the timeline either way.
/// </summary>
/// <remarks>
/// Every piece is two bars of quarter notes - 3840 ticks - at a tempo of its own, so a seam is at a
/// known tick and every note of every piece is at a known tick. A fresh piece that is carried keeps
/// its ticks; only the tempo changes.
/// </remarks>
public class MusicEngineSessionTempoTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;
    private const long PieceTicks = 2L * 4L * Resolution;

    [Fact]
    public async Task adopt_plays_every_piece_at_its_own_tempo_as_it_always_has()
    {
        //Arrange
        using var rig = Build(SessionTempoPolicy.Adopt, Piece(120.0), Piece(180.0));

        //Act
        await rig.RunUntil(() => Committed(rig).OfType<NoteOnEvent>().Any(note => note.AbsoluteTime >= PieceTicks));

        //Assert
        Tempos(rig).Should().Contain((PieceTicks, 180.0));
        rig.Engine.SessionTempo.Should().BeNull();
        rig.Engine.CarriedTempoCount.Should().Be(0);
    }

    [Fact]
    public async Task carry_plays_a_fresh_piece_at_the_session_tempo_with_its_ticks_unchanged()
    {
        //Arrange - the second piece is written at 180 and changes to 200 half-way through
        using var rig = Build(SessionTempoPolicy.Carry, Piece(120.0), Piece(180.0, changeTo: 200.0));

        //Act
        await rig.RunUntil(() => Committed(rig).OfType<NoteOnEvent>().Count(note => note.AbsoluteTime >= PieceTicks) >= 8);

        //Assert - only the session tempo (a hard join restating the tempo already playing says
        //nothing, so there is no event at the seam at all); every note where it was written
        Tempos(rig).Where(tempo => tempo.Tick < 2L * PieceTicks).Should().Equal((0L, 120.0));
        Committed(rig).OfType<NoteOnEvent>().Where(note => note.AbsoluteTime >= PieceTicks && note.AbsoluteTime < 2L * PieceTicks)
            .Select(note => note.AbsoluteTime)
            .Should().Equal(Enumerable.Range(0, 8).Select(i => PieceTicks + (i * (long)Resolution)));
        rig.Engine.SessionTempo.Should().BeApproximately(120.0, 0.01);
        rig.Engine.CarriedTempoCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task carry_outside_band_adopts_a_close_tempo_and_carries_a_distant_one()
    {
        //Arrange - 126 is five per cent from 120; 180 is fifty
        using var rig = Build(SessionTempoPolicy.CarryOutsideBand, Piece(120.0), Piece(126.0), Piece(180.0));

        //Act
        await rig.RunUntil(() => Committed(rig).OfType<NoteOnEvent>().Any(note => note.AbsoluteTime >= 2L * PieceTicks));

        //Assert - the second piece keeps 126; the third is played at 120, not 180
        Tempos(rig).Where(tempo => tempo.Tick <= 2L * PieceTicks)
            .Should().Equal((0L, 120.0), (PieceTicks, 126.0), (2L * PieceTicks, 120.0));
        rig.Engine.AdoptedTempoCount.Should().Be(1);
        rig.Engine.CarriedTempoCount.Should().Be(1);
        rig.Engine.SessionTempo.Should().BeApproximately(120.0, 0.01);
    }

    [Fact]
    public async Task a_session_tempo_given_up_front_is_imposed_on_the_first_piece_too()
    {
        //Arrange
        using var rig = Build(SessionTempoPolicy.Carry, Piece(120.0), Piece(180.0));
        rig.Engine.SessionBeatsPerMinute = 90.0;

        //Act
        await rig.RunUntil(() => Committed(rig).OfType<NoteOnEvent>().Any(note => note.AbsoluteTime >= PieceTicks));

        //Assert
        Tempos(rig).Where(tempo => tempo.Tick <= PieceTicks).Should().Equal((0L, 90.0));
        rig.Engine.SessionTempo.Should().BeApproximately(90.0, 0.01);
    }

    [Fact]
    public async Task an_intent_tempo_is_the_session_tempo_when_the_generator_honours_it()
    {
        //Arrange
        var request = new MusicRequest { Intent = new MusicIntent { BeatsPerMinute = 100.0 } };
        using var rig = Build(SessionTempoPolicy.CarryOutsideBand, request, Piece(120.0), Piece(104.0));

        //Act
        await rig.RunUntil(() => Committed(rig).OfType<NoteOnEvent>().Any(note => note.AbsoluteTime >= PieceTicks));

        //Assert - the first piece is played at 100; the second, within the band of 100, keeps 104
        Tempos(rig).Where(tempo => tempo.Tick <= PieceTicks).Should().Equal((0L, 100.0), (PieceTicks, 104.0));
        rig.Engine.AdoptedTempoCount.Should().Be(1);
    }

    [Fact]
    public async Task a_crossfaded_fresh_piece_is_carried_from_the_start_of_the_fade()
    {
        //Arrange - a one-second crossfade: the incoming piece starts a second before the bar line
        using var rig = Build(SessionTempoPolicy.Carry, SegmentPriming.Fresh, null, false, Piece(120.0), Piece(180.0));
        rig.Engine.SeamCrossfade = TimeSpan.FromSeconds(1.0);

        //Act
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);
        rig.Host.HeadPosition = TimeSpan.FromSeconds(3.0);
        var start = rig.Engine.SegmentStartTicks[1];
        await rig.RunUntil(() => Committed(rig).OfType<NoteOnEvent>().Any(note => note.AbsoluteTime >= start));

        //Assert - the fade started at 120's own seconds, and the incoming piece is at 120 from there
        start.Should().Be(PieceTicks - (2L * Resolution));
        Tempos(rig).Where(tempo => tempo.Tick >= start && tempo.Tick < start + PieceTicks)
            .Should().Equal((start, 120.0));
    }

    [Theory]
    [InlineData(192.0)]
    [InlineData(80.0)]
    public async Task a_crossfaded_fresh_piece_outside_the_band_is_carried_not_adopted(double written)
    {
        //Arrange - THE LIVE DEFECT: the decision was taken on a pump before the incoming piece had
        //written anything, found no tempo, and adopted the tempo already playing
        using var rig = Build(SessionTempoPolicy.CarryOutsideBand, SegmentPriming.Fresh, null, false,
            Piece(120.0), Piece(written));
        rig.Engine.SeamCrossfade = TimeSpan.FromSeconds(1.0);
        rig.Engine.KeepsCrossfadeRecord = true;
        rig.Generator.FirstItemDelay = TimeSpan.FromSeconds(1.0);
        rig.Generator.Clock = rig.Time;

        //Act
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);
        rig.Host.HeadPosition = TimeSpan.FromSeconds(3.0);
        var start = rig.Engine.SegmentStartTicks[1];
        await rig.RunUntil(() => Committed(rig).OfType<NoteOnEvent>().Any(note => note.AbsoluteTime >= start));

        //Assert
        Tempos(rig).Where(tempo => tempo.Tick >= start && tempo.Tick < start + PieceTicks)
            .Should().Equal((start, 120.0));
        rig.Engine.CarriedTempoCount.Should().Be(1);
        rig.Engine.AdoptedTempoCount.Should().Be(0);
        rig.Engine.TempoDecisions.Should().Contain(line => line.Contains("carried to the session tempo of 120.0"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task the_decision_uses_the_tempo_in_force_where_the_piece_first_sounds(bool crossfaded)
    {
        //Arrange - a piece that states a default 120 at its first tick, is silent for a bar, and
        //states its real tempo of 190 where its first note is: that is the tempo it sounds at
        using var rig = Build(SessionTempoPolicy.CarryOutsideBand, SegmentPriming.Fresh, null, !crossfaded,
            Piece(120.0), DefaultThenRealTempo(190.0));

        if (crossfaded)
        {
            rig.Engine.SeamCrossfade = TimeSpan.FromSeconds(1.0);
        }

        //Act
        if (crossfaded)
        {
            await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);
            rig.Host.HeadPosition = TimeSpan.FromSeconds(3.0);
        }

        await rig.RunUntil(() => rig.Engine.CarriedTempoCount + rig.Engine.AdoptedTempoCount >= 1 &&
            Committed(rig).OfType<NoteOnEvent>().Count(note => note.NoteNumber >= 90) >= 4);

        //Assert - carried: no 190 anywhere, and the default 120 does not count as its tempo
        Tempos(rig).Should().NotContain(tempo => tempo.Bpm > 180.0);
        rig.Engine.CarriedTempoCount.Should().Be(1);
        rig.Engine.AdoptedTempoCount.Should().Be(0);
    }

    [Fact]
    public async Task a_crossfaded_fresh_piece_that_never_sounds_a_note_is_still_placed()
    {
        //Arrange - THE HANG: a fresh piece that settles bar after bar of silence (a controller
        //every bar, never a note) until its pull limit stops it - so it never becomes "ready" by
        //its music, and nothing else would ever place it while the head stands still
        using var rig = Build(SessionTempoPolicy.CarryOutsideBand, SegmentPriming.Fresh, null, false,
            Piece(120.0), LongSilence(60), Piece(180.0));
        rig.Engine.SeamCrossfade = TimeSpan.FromSeconds(1.0);

        //Act - bounded: the rig's pump gives up long before "for ever"
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 2 && !rig.Engine.HasPendingFreshSegment);

        //Assert - placed at the bar line with a hard join, since there was nothing to fade in
        rig.Engine.SegmentStartTicks[1].Should().Be(PieceTicks);
        rig.Engine.ShortenedCrossfadeCount.Should().Be(1);
    }

    [Fact]
    public async Task a_fresh_piece_that_opens_with_long_silence_does_not_hold_the_music_for_ever()
    {
        //Arrange - a hard join, and a piece whose first note never comes before its pull pauses
        using var rig = Build(SessionTempoPolicy.CarryOutsideBand, Piece(120.0), LongSilence(60), Piece(180.0));

        //Act - the tempo decision must be taken without a note, and the music must move on
        await rig.RunUntil(() => rig.Engine.CarriedTempoCount + rig.Engine.AdoptedTempoCount >= 1 &&
                                 rig.Engine.WrittenThroughTick >= PieceTicks + (8L * 4L * Resolution));

        //Assert
        rig.Engine.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task a_primed_segment_is_never_touched()
    {
        //Arrange - primed continuations of a generator that writes its second pass at 180
        using var rig = Build(SessionTempoPolicy.Carry, SegmentPriming.Primed, null, true, Piece(120.0), Piece(180.0));

        //Act
        await rig.RunUntil(() => Committed(rig).OfType<NoteOnEvent>().Any(note => note.AbsoluteTime >= PieceTicks));

        //Assert
        Tempos(rig).Should().Contain((PieceTicks, 180.0));
        rig.Engine.CarriedTempoCount.Should().Be(0);
    }

    // --- the rig ------------------------------------------------------------------------------

    internal static MidiEventCollection Piece(double beatsPerMinute, double? changeTo = null)
    {
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();
        music.AddEvent(new TempoEvent((int)Math.Round(60000000.0 / beatsPerMinute), 0L), 0);
        music.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);

        if (changeTo.HasValue)
        {
            music.AddEvent(new TempoEvent((int)Math.Round(60000000.0 / changeTo.Value), PieceTicks / 2L), 0);
        }

        for (var beat = 0; beat < 8; beat++)
        {
            var note = new NoteOnEvent(beat * (long)Resolution, 1, 60 + beat, 100, Resolution);
            music.AddEvent(note, 0);
            music.AddEvent(note.OffEvent, 0);
        }

        return music;
    }

    // Bars of silence: the tempo and metre at the start, and a controller on every bar after that,
    // so the music settles bar by bar without a single note.
    internal static MidiEventCollection LongSilence(int bars)
    {
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();
        music.AddEvent(new TempoEvent(400000, 0L), 0);
        music.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);

        for (var bar = 1; bar <= bars; bar++)
        {
            music.AddEvent(new ControlChangeEvent(bar * 4L * Resolution, 1, MidiController.MainVolume, 100), 0);
        }

        return music;
    }

    // A default tempo of 120 at the first tick, a silent bar, then the real tempo where the first
    // note is - the shape a model's stream can have.
    private static MidiEventCollection DefaultThenRealTempo(double real)
    {
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();
        music.AddEvent(new TempoEvent(500000, 0L), 0);
        music.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);
        music.AddEvent(new TempoEvent((int)Math.Round(60000000.0 / real), 4L * Resolution), 0);

        for (var beat = 4; beat < 12; beat++)
        {
            var note = new NoteOnEvent(beat * (long)Resolution, 1, 90 + (beat % 8), 100, Resolution);
            music.AddEvent(note, 0);
            music.AddEvent(note.OffEvent, 0);
        }

        return music;
    }

    private static IReadOnlyList<MidiEvent> Committed(TempoRig rig)
    {
        var recording = rig.Stream.ToMidiEventCollection();
        var events = new List<MidiEvent>();

        for (var track = 0; track < recording.Tracks; track++)
        {
            foreach (var midiEvent in recording.GetTrackEvents(track))
            {
                if (!MidiEvent.IsEndTrack(midiEvent) && !MidiEvent.IsNoteOff(midiEvent))
                {
                    events.Add(midiEvent);
                }
            }
        }

        return events.OrderBy(midiEvent => midiEvent.AbsoluteTime).ToArray();
    }

    private static IReadOnlyList<(long Tick, double Bpm)> Tempos(TempoRig rig) =>
        Committed(rig).OfType<TempoEvent>()
            .Select(tempo => (tempo.AbsoluteTime, Math.Round(60000000.0 / tempo.MicrosecondsPerQuarterNote, 1)))
            .ToArray();

    private static TempoRig Build(SessionTempoPolicy policy, params MidiEventCollection[] pieces) =>
        Build(policy, SegmentPriming.Fresh, null, true, pieces);

    private static TempoRig Build(SessionTempoPolicy policy, MusicRequest request,
        params MidiEventCollection[] pieces) =>
        Build(policy, SegmentPriming.Fresh, request, true, pieces);

    private static TempoRig Build(SessionTempoPolicy policy, SegmentPriming priming, MusicRequest request,
        bool followTheMusic, params MidiEventCollection[] pieces)
    {
        var time = new ManualTimeProvider();
        var generator = new SequenceMusicGenerator("Tempos",
            pieces.Select(piece => ReplayOnTestClock.On(ReplayMusicGenerator.FromMidiEvents("Tempos", piece), time, 8.0))
                .ToArray(),
            MusicRequestFeatures.Continuation | MusicRequestFeatures.Seed | MusicRequestFeatures.Tempo);
        var stream = new MidiStream(Resolution);
        var host = new FakeMusicHost(SampleRate);
        var library = TestInstrumentLibrary.Complete("TempoParts");
        var rendition = MusicRenditionRegistry.Resolve(BuiltInRenditions.Automatic).Clone();
        var voicer = new RenditionVoicer(rendition, library, SampleRate, null, 1.0F);
        var mixer = new SeamCrossfadeMixer(voicer);

        host.Load(stream, _ => mixer, (synthesizer, channel, command, data1, data2) =>
            synthesizer.ProcessMidiMessage(channel, command, data1, data2));

        var with = request == null ? new MusicRequest() : request.Clone();
        with.TicksPerQuarterNote = Resolution;

        var engine = new MusicEngine(generator, with, stream, voicer, host, TimeSpan.FromSeconds(1.0), time, 0L)
        {
            EndOfPiece = EndOfPiecePolicy.KeepGenerating,
            GenerateAhead = TimeSpan.FromSeconds(12.0),
            SegmentPriming = priming,
            TempoPolicy = policy,
            SeamCrossfadeCurve = MusicFadeCurve.EqualPower,
            Mixer = mixer,
            CreateVoicer = () => new RenditionVoicer(rendition, library, SampleRate, null, 1.0F)
        };

        host.EndOfTheMusic = () => engine.WrittenThroughTime;

        if (followTheMusic)
        {
            host.FollowTheMusic();
        }

        return new TempoRig(time, stream, host, engine, generator);
    }

    private sealed class TempoRig : IDisposable
    {
        public TempoRig(ManualTimeProvider time, MidiStream stream, FakeMusicHost host, MusicEngine engine,
            SequenceMusicGenerator generator)
        {
            Generator = generator;
            Time = time;
            Stream = stream;
            Host = host;
            Engine = engine;
        }

        public ManualTimeProvider Time { get; }

        public MidiStream Stream { get; }

        public FakeMusicHost Host { get; }

        public MusicEngine Engine { get; }

        public SequenceMusicGenerator Generator { get; }

        public Task RunUntil(Func<bool> until)
        {
            if (!started)
            {
                started = true;
                Engine.Start();
            }

            return EnginePump.RunUntilAsync(Time, until);
        }

        private bool started;

        public void Dispose() => Engine.Dispose();
    }
}
