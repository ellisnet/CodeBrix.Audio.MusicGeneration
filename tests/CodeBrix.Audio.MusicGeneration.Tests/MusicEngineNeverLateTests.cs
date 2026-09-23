using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// NOTHING IS EVER COMMITTED AT OR BEHIND THE PLAY HEAD, AND THE MUSIC ONLY EVER WAITS IN WHOLE
/// BARS - under a rest, under a stall, under a follow-up prompt, and whatever the metre is.
/// </summary>
/// <remarks>
/// <para>
/// WHY THESE NEED FIXTURES OF THEIR OWN. A note written with a duration pushes the timeline's
/// HORIZON out to where that note ENDS, and the play head may run as far as the horizon - so a head
/// can run on past the music the engine has written, under a note that is still sounding, and music
/// committed after that would land behind it. The replay generators cannot show it: a replay rounds
/// its pass up to a whole bar AFTER its last note has finished, so nothing of one is ever still
/// ringing when the next segment is placed. <see cref="StallingMusicGenerator"/> writes music whose
/// notes ring across the seam, and stops writing on demand.
/// </para>
/// <para>
/// THE HEAD IS THE OTHER HALF OF IT: <c>FakeMusicHost.RunTheHead</c> runs in real time on the
/// test's clock and holds at the horizon, which is the only head that can get past the end of the
/// written music.
/// </para>
/// </remarks>
public class MusicEngineNeverLateTests
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;
    private const long TicksPerBar = 4L * Resolution;
    private const long TwoBarPass = 2L * TicksPerBar;
    private const long EightBarPass = 8L * TicksPerBar;

    private const int SegmentMarkerNote = 84;

    private static readonly TimeSpan Preroll = TimeSpan.FromSeconds(1.0);

    // --- A: THE FALLBACK'S REST ---------------------------------------------------------------

    [Fact]
    public async Task a_segment_after_a_rest_lands_on_a_bar_line_ahead_of_the_head()
    {
        //Arrange - two-bar passes, each with a pad that rings a whole bar past the end of the pass,
        //from a generator at a quarter of real time: the engine falls back to a segment at a time
        using var rig = Build(TwoBarsWithARingingPad(), TwoBarPass, pacingRate: 0.25,
            generateAhead: TimeSpan.FromSeconds(4.0));
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.Mode == MusicDeliveryMode.SegmentAtATime);
        await rig.RunUntil(() => rig.Engine.HoldCount >= 1);
        await rig.RunUntil(() => SegmentStarts(rig.Stream).Count >= 2);

        //Assert - the second segment landed on a bar line, a whole number of bars after the first
        //one ended, and NOT ONE EVENT reached the timeline behind the head, which is what
        //LateEventCount counts and the whole point of the rule
        var starts = SegmentStarts(rig.Stream);
        var rested = starts[1] - TwoBarPass;

        rig.Stream.LateEventCount.Should().Be(0);
        rig.Engine.Mode.Should().Be(MusicDeliveryMode.SegmentAtATime);
        (starts[1] % TicksPerBar).Should().Be(0L);
        rested.Should().BeGreaterThan(0L);
        (rested % TicksPerBar).Should().Be(0L);
        rig.Engine.HoldCount.Should().BeGreaterThanOrEqualTo(1);
        rig.Engine.HeldBarCount.Should().BeGreaterThanOrEqualTo((int)(rested / TicksPerBar));
    }

    [Fact]
    public async Task the_notes_ringing_across_the_rest_keep_every_tick_of_their_length()
    {
        //Arrange
        using var rig = Build(TwoBarsWithARingingPad(), TwoBarPass, pacingRate: 0.25,
            generateAhead: TimeSpan.FromSeconds(4.0));
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.HoldCount >= 1);
        await rig.RunUntil(() => SegmentStarts(rig.Stream).Count >= 2);

        //Assert - the pad is two whole bars long, still, and it really does ring past the seam
        var pads = Committed(rig.Stream).OfType<NoteOnEvent>()
            .Where(note => note.Channel == 2).ToArray();

        pads.Should().NotBeEmpty();
        pads.Should().AllSatisfy(pad => pad.NoteLength.Should().Be((int)(2L * TicksPerBar)));
        (pads[0].AbsoluteTime + pads[0].NoteLength).Should().BeGreaterThan(TwoBarPass);
        rig.Engine.HoldCount.Should().BeGreaterThanOrEqualTo(1);
        rig.Engine.HeldBarCount.Should().BeGreaterThanOrEqualTo(1);
        rig.Stream.LateEventCount.Should().Be(0);
    }

    // --- B: A STALL WHILE STREAMING -----------------------------------------------------------

    [Fact]
    public async Task a_stall_under_a_held_note_rests_in_whole_bars_and_resumes_in_time()
    {
        //Arrange - a generator that keeps up, and a pad from bar 2 that sounds for four bars
        using var rig = Build(EightBarsWithALongPad(), EightBarPass);
        rig.Generator.StallFrom(4L * TicksPerBar);
        rig.Engine.Start();

        //Act - the head runs on under the pad, past the music that has been written
        await rig.RunUntil(() => rig.Engine.HoldCount >= 1);

        var heldBars = rig.Engine.HeldBarCount;

        rig.Generator.Resume();
        await rig.RunUntil(() => NoteTicks(rig.Stream).Any(tick => tick >= 4L * TicksPerBar));
        await rig.Settle(40);

        //Assert - what was already committed is where it was written, what came after it is a
        //whole number of bars later, and nothing arrived behind the head
        var ticks = MelodyTicks(rig.Stream);
        var before = ticks.Where(tick => tick < 4L * TicksPerBar).ToArray();
        var after = ticks.Where(tick => tick >= 4L * TicksPerBar).ToArray();

        rig.Stream.LateEventCount.Should().Be(0);
        rig.Engine.HoldCount.Should().Be(1);
        heldBars.Should().BeGreaterThanOrEqualTo(1);
        before.Should().Equal(Enumerable.Range(0, before.Length).Select(i => i * (long)Resolution));
        after.Should().NotBeEmpty();
        ((after[0] - before[before.Length - 1] - Resolution) % TicksPerBar).Should().Be(0L);
        (after[0] - before[before.Length - 1] - Resolution).Should()
            .Be(heldBars * TicksPerBar);
    }

    [Fact]
    public async Task the_gap_is_counted_although_the_stream_itself_never_reported_starving()
    {
        //Arrange
        using var rig = Build(EightBarsWithALongPad(), EightBarPass);
        rig.Generator.StallFrom(4L * TicksPerBar);
        rig.Engine.Start();

        var streamSaidItHadRunDry = false;

        //Act - watch what the timeline says about itself while the head runs under the held note
        await rig.RunUntil(() =>
        {
            if (rig.Host.Position > TimeSpan.Zero)
            {
                streamSaidItHadRunDry |= rig.Host.IsStarved;
            }

            return rig.Engine.HoldCount >= 1;
        });

        //Assert - the pad kept the stream from ever calling itself starved; the engine counted the
        //gap all the same, because the head had run out of MUSIC
        streamSaidItHadRunDry.Should().BeFalse();
        rig.Engine.StarvationGapCount.Should().BeGreaterThan(0);
        rig.Engine.HoldCount.Should().Be(1);
        rig.Engine.HeldBarCount.Should().BeGreaterThanOrEqualTo(1);
        rig.Stream.LateEventCount.Should().Be(0);
    }

    // --- A GENERATOR THAT NEVER FALLS BEHIND --------------------------------------------------

    [Fact]
    public async Task a_generator_that_never_falls_behind_is_never_held_back_by_one_tick()
    {
        //Arrange - eight times real time, with the same pad ringing across every seam
        using var rig = Build(TwoBarsWithARingingPad(), TwoBarPass, pacingRate: 8.0);
        rig.Engine.Start();

        //Act - four bars of melody have to REACH THE TIMELINE, which takes as long as playing
        //them: the commit window is short and the head moves in real time
        await rig.RunUntil(() => MelodyTicks(rig.Stream).Count >= 16);
        await rig.Settle(40);

        //Assert - the committed timeline is tick for tick what the generator wrote, segment after
        //segment, and nothing was ever moved
        var melody = MelodyTicks(rig.Stream).Take(16).ToArray();

        rig.Engine.HoldCount.Should().Be(0);
        rig.Engine.HeldBarCount.Should().Be(0);
        rig.Stream.LateEventCount.Should().Be(0);
        melody.Should().Equal(Enumerable.Range(0, 16).Select(i => i * (long)Resolution));
    }

    // --- A FOLLOW-UP PROMPT -------------------------------------------------------------------

    [Fact]
    public async Task a_follow_up_during_a_rest_switches_cleanly_and_late_free()
    {
        //Arrange - the fallback's rest, and a new prompt while the music is resting
        using var rig = Build(TwoBarsWithARingingPad(), TwoBarPass, pacingRate: 0.25,
            generateAhead: TimeSpan.FromSeconds(4.0));
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.Mode == MusicDeliveryMode.SegmentAtATime);
        await rig.RunUntil(() => rig.Engine.HoldCount >= 1);

        var second = NewGenerator(rig, "SecondDuringARest", 4.0);

        //Act
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => rig.Engine.ActiveGenerator == second);
        await rig.RunUntil(() => FirstTickOfTheNewMusic(rig.Stream) >= 0L);
        await rig.Settle(40);

        //Assert
        var switchTick = FirstTickOfTheNewMusic(rig.Stream);

        rig.Stream.LateEventCount.Should().Be(0);
        (switchTick % TicksPerBar).Should().Be(0L);
        rig.Engine.ActiveGenerator.Should().BeSameAs(second);
        rig.Engine.HoldCount.Should().BeGreaterThanOrEqualTo(1);
        rig.Engine.HeldBarCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task a_follow_up_during_a_stall_switches_cleanly_and_late_free()
    {
        //Arrange
        using var rig = Build(EightBarsWithALongPad(), EightBarPass);
        rig.Generator.StallFrom(4L * TicksPerBar);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.HoldCount >= 1);

        var second = NewGenerator(rig, "SecondDuringAStall", 4.0);

        //Act - the old generator never comes back; the new one takes over
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => rig.Engine.ActiveGenerator == second);
        await rig.RunUntil(() => FirstTickOfTheNewMusic(rig.Stream) >= 0L);
        await rig.Settle(40);

        //Assert
        var switchTick = FirstTickOfTheNewMusic(rig.Stream);

        rig.Stream.LateEventCount.Should().Be(0);
        (switchTick % TicksPerBar).Should().Be(0L);
        rig.Engine.ActiveGenerator.Should().BeSameAs(second);
        rig.Engine.HoldCount.Should().BeGreaterThanOrEqualTo(1);
        rig.Engine.HeldBarCount.Should().BeGreaterThanOrEqualTo(1);
    }

    // --- THE METRE AND THE TEMPO IN FORCE -----------------------------------------------------

    [Fact]
    public async Task the_rest_is_whole_bars_of_the_metre_in_force_and_the_seconds_still_add_up()
    {
        //Arrange - two bars of 4/4 at 120, then 3/4 at 60 with a pad over it, and a stall in the
        //middle of the second metre
        using var rig = Build(CommonTimeThenThreeFour(), 3840L + (6L * 1440L));
        rig.Generator.StallFrom(5280L);
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.HoldCount >= 1);

        var heldBars = rig.Engine.HeldBarCount;

        rig.Generator.Resume();
        await rig.RunUntil(() => NoteTicks(rig.Stream).Any(tick => tick >= 5280L));
        await rig.Settle(40);

        //Assert - the rest is bars of 3/4 (1440 ticks), which is not a whole number of the 4/4 bars
        //the piece began in, and the engine's own clock still turns those ticks into the right
        //seconds afterwards
        var resumed = NoteTicks(rig.Stream).First(tick => tick >= 5280L);
        var moved = resumed - 5280L;

        rig.Stream.LateEventCount.Should().Be(0);
        rig.Engine.HoldCount.Should().Be(1);
        heldBars.Should().BeGreaterThanOrEqualTo(1);
        moved.Should().Be(heldBars * 1440L);
        (moved % 1440L).Should().Be(0L);
        (moved % TicksPerBar).Should().NotBe(0L);
        rig.Engine.WrittenThroughTime.TotalSeconds.Should()
            .BeApproximately(SecondsOfTick(rig.Engine.WrittenThroughTick), 0.01);
    }

    // --- WHAT THE NEXT SEGMENT IS TOLD --------------------------------------------------------

    [Fact]
    public async Task the_continuation_after_a_rest_describes_the_music_and_not_the_silence()
    {
        //Arrange - the generator stalls under the pad and then decides the pass is over, so the
        //bars immediately before the next segment are the REST the engine inserted
        using var rig = Build(EightBarsWithALongPad(), EightBarPass);
        rig.Generator.StallFrom(2L * TicksPerBar);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.HoldCount >= 1);

        //Act
        rig.Generator.ResumeByEndingThePass();
        await rig.RunUntil(() => rig.Generator.PassCount >= 2);

        //Assert - the tail handed to the next segment is music
        var next = rig.Generator.Requests[1];

        rig.Engine.HoldCount.Should().Be(1);
        rig.Engine.HeldBarCount.Should().BeGreaterThanOrEqualTo(4);
        next.Continuation.Should().NotBeNull();
        next.Continuation.Tail.Should().NotBeNull();
        next.Continuation.Tail.GetTrackEvents(0).OfType<NoteOnEvent>().Should().NotBeEmpty();
        next.Continuation.BeatsPerMinute.Should().Be(120.0);
        next.Continuation.Meter.Should().Be(new MusicMeter(4, 4));
    }

    [Fact]
    public async Task the_seam_after_a_rest_still_says_nothing_the_music_is_already_saying()
    {
        //Arrange
        using var rig = Build(EightBarsWithALongPad(), EightBarPass);
        rig.Generator.StallFrom(2L * TicksPerBar);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.HoldCount >= 1);

        //Act - end the pass where it stands, so the next segment starts just after the rest and
        //opens by setting the tempo and the metre it is already playing at
        rig.Generator.ResumeByEndingThePass();
        await rig.RunUntil(() => rig.Generator.PassCount >= 2);
        await rig.RunUntil(() => NoteTicks(rig.Stream).Any(tick => tick > rig.Engine.HeldBarCount * TicksPerBar));
        await rig.Settle(40);

        //Assert - said once, at tick 0, and never again at the seam after the rest
        var committed = Committed(rig.Stream);

        committed.OfType<TempoEvent>().Should().HaveCount(1);
        committed.OfType<TimeSignatureEvent>().Should().HaveCount(1);
        rig.Stream.LateEventCount.Should().Be(0);
        rig.Engine.HoldCount.Should().Be(1);
        rig.Engine.HeldBarCount.Should().BeGreaterThanOrEqualTo(1);
    }

    // --- WHERE THERE IS NO PLAY HEAD AT ALL ---------------------------------------------------

    [Fact]
    public async Task nothing_is_ever_held_back_where_there_is_no_play_head()
    {
        //Arrange - what an offline render does: there is nothing to be late for, so the rule is off
        using var rig = Build(EightBarsWithALongPad(), EightBarPass);
        rig.Engine.KeepsMusicAheadOfTheHead = false;
        rig.Generator.StallFrom(4L * TicksPerBar);
        rig.Engine.Start();

        //Act - the head runs right past the music, which in a render it cannot do any harm by
        await rig.RunUntil(() => rig.Host.Position >= TimeSpan.FromSeconds(6.0));
        rig.Generator.Resume();
        await rig.RunUntil(() => NoteTicks(rig.Stream).Any(tick => tick >= 4L * TicksPerBar));
        await rig.Settle(40);

        //Assert - tick for tick what the generator wrote, with no rest inserted anywhere
        var melody = MelodyTicks(rig.Stream).Take(20).ToArray();

        rig.Engine.HoldCount.Should().Be(0);
        rig.Engine.HeldBarCount.Should().Be(0);
        melody.Should().Equal(Enumerable.Range(0, 20).Select(i => i * (long)Resolution));
    }

    // --- the music ----------------------------------------------------------------------------

    /// Two bars of 4/4 at 120, and a pad from bar 2 that sounds for TWO bars - so it rings a whole
    /// bar past the end of the pass, which is where the next segment goes.
    private static IReadOnlyList<MidiEvent> TwoBarsWithARingingPad()
    {
        var music = new List<MidiEvent>
        {
            new TempoEvent(500000, 0L),
            new TimeSignatureEvent(0L, 4, 2, 24, 8)
        };

        for (var beat = 0; beat < 8; beat++)
        {
            // THE FIRST NOTE OF A PASS IS MARKED, so a test can see exactly where each segment was
            // placed however many of them have gone by.
            var note = beat == 0 ? SegmentMarkerNote : 60 + beat;

            music.Add(new NoteOnEvent(beat * (long)Resolution, 1, note, 100, Resolution));
        }

        music.Add(new NoteOnEvent(TicksPerBar, 2, 48, 90, (int)(2L * TicksPerBar)));

        return InTickOrder(music);
    }

    /// Eight bars of 4/4 at 120, and a pad from bar 2 that sounds for FOUR bars.
    private static IReadOnlyList<MidiEvent> EightBarsWithALongPad()
    {
        var music = new List<MidiEvent>
        {
            new TempoEvent(500000, 0L),
            new TimeSignatureEvent(0L, 4, 2, 24, 8)
        };

        for (var beat = 0; beat < 32; beat++)
        {
            music.Add(new NoteOnEvent(beat * (long)Resolution, 1, 60 + (beat % 12), 100, Resolution));
        }

        music.Add(new NoteOnEvent(TicksPerBar, 2, 48, 90, (int)(4L * TicksPerBar)));

        return InTickOrder(music);
    }

    /// Two bars of 4/4 at 120 beats to the minute, and then 3/4 at 60 - where a bar is 1440 ticks
    /// and three seconds long - with a pad ringing across the whole of it.
    private static IReadOnlyList<MidiEvent> CommonTimeThenThreeFour()
    {
        var music = new List<MidiEvent>
        {
            new TempoEvent(500000, 0L),
            new TimeSignatureEvent(0L, 4, 2, 24, 8)
        };

        for (var beat = 0; beat < 8; beat++)
        {
            music.Add(new NoteOnEvent(beat * (long)Resolution, 1, 60 + beat, 100, Resolution));
        }

        music.Add(new TempoEvent(1000000, 3840L));
        music.Add(new TimeSignatureEvent(3840L, 3, 2, 24, 8));
        music.Add(new NoteOnEvent(3840L, 2, 48, 90, 3 * 1440));

        for (var beat = 0; beat < 9; beat++)
        {
            music.Add(new NoteOnEvent(3840L + (beat * (long)Resolution), 1, 72 + beat, 100, Resolution));
        }

        return InTickOrder(music);
    }

    private static IReadOnlyList<MidiEvent> InTickOrder(List<MidiEvent> music) =>
        music.OrderBy(midiEvent => midiEvent.AbsoluteTime).ToArray();

    /// Where a tick falls in seconds for CommonTimeThenThreeFour: 120 to the minute up to tick
    /// 3840, and 60 from there on.
    private static double SecondsOfTick(long tick) =>
        tick <= 3840L ? tick / 960.0 : 4.0 + ((tick - 3840L) / 480.0);

    // --- the rig ------------------------------------------------------------------------------

    private static IReadOnlyList<MidiEvent> Recording(MidiStream stream)
    {
        var recording = stream.ToMidiEventCollection();
        var events = new List<MidiEvent>();

        for (var track = 0; track < recording.Tracks; track++)
        {
            foreach (var midiEvent in recording.GetTrackEvents(track))
            {
                if (midiEvent != null && !MidiEvent.IsEndTrack(midiEvent))
                {
                    events.Add(midiEvent);
                }
            }
        }

        return events.OrderBy(midiEvent => midiEvent.AbsoluteTime).ToArray();
    }

    private static IReadOnlyList<MidiEvent> Committed(MidiStream stream) =>
        Recording(stream)
            .Where(midiEvent => !MidiEvent.IsNoteOff(midiEvent))
            .ToArray();

    private static IEnumerable<long> NoteTicks(MidiStream stream) =>
        Committed(stream).OfType<NoteOnEvent>().Select(note => note.AbsoluteTime).Distinct()
            .OrderBy(tick => tick);

    /// Every tick the melody part sounds at, in order. The melody is a quarter note on every beat,
    /// so consecutive ticks are one quarter note apart unless a rest has been inserted.
    private static IReadOnlyList<long> MelodyTicks(MidiStream stream) =>
        Committed(stream).OfType<NoteOnEvent>().Where(note => note.Channel == 1)
            .Select(note => note.AbsoluteTime).Distinct().OrderBy(tick => tick).ToArray();

    /// Where every segment landed, in order: the marked first note of each pass.
    private static IReadOnlyList<long> SegmentStarts(MidiStream stream) =>
        Committed(stream).OfType<NoteOnEvent>()
            .Where(note => note.Channel == 1 && note.NoteNumber == SegmentMarkerNote)
            .Select(note => note.AbsoluteTime).Distinct().OrderBy(tick => tick).ToArray();

    /// The first tick of music from a follow-up generator, which writes notes nothing else does.
    private static long FirstTickOfTheNewMusic(MidiStream stream)
    {
        foreach (var note in Committed(stream).OfType<NoteOnEvent>())
        {
            if (note.NoteNumber >= 90)
            {
                return note.AbsoluteTime;
            }
        }

        return -1L;
    }

    private static StallingMusicGenerator NewGenerator(NeverLateRig rig, string name, double rate)
    {
        var music = new List<MidiEvent>
        {
            new TempoEvent(500000, 0L),
            new TimeSignatureEvent(0L, 4, 2, 24, 8)
        };

        for (var beat = 0; beat < 16; beat++)
        {
            music.Add(new NoteOnEvent(beat * (long)Resolution, 3, 90 + (beat % 8), 100, Resolution));
        }

        return new StallingMusicGenerator(name, rig.Time, InTickOrder(music), 4L * TicksPerBar,
            Resolution)
        {
            PacingRate = rate
        };
    }

    private static NeverLateRig Build(IReadOnlyList<MidiEvent> music, long passTicks,
        double pacingRate = 4.0, TimeSpan? generateAhead = null,
        EndOfPiecePolicy policy = EndOfPiecePolicy.KeepGenerating)
    {
        var time = new ManualTimeProvider();
        var generator = new StallingMusicGenerator("NeverLate", time, music, passTicks, Resolution)
        {
            PacingRate = pacingRate
        };

        var stream = new MidiStream(Resolution);
        var host = new FakeMusicHost(SampleRate);
        var voicer = new RenditionVoicer(
            MusicRenditionRegistry.Resolve(BuiltInRenditions.Automatic).Clone(),
            TestInstrumentLibrary.Complete("NeverLateParts"), SampleRate, null, 1.0F);

        host.Load(stream, _ => voicer.Router, (synthesizer, channel, command, data1, data2) =>
            synthesizer.ProcessMidiMessage(channel, command, data1, data2));

        var engine = new MusicEngine(generator,
            new MusicRequest { TicksPerQuarterNote = Resolution }, stream, voicer, host, Preroll,
            time, 0L)
        {
            EndOfPiece = policy,
            GenerateAhead = generateAhead ?? MusicEngine.DefaultGenerateAhead
        };

        // THE HEAD RUNS, which is the whole point of these tests: it advances in real time on the
        // test's clock and can therefore get past the end of the music under a ringing note.
        host.RunTheHead(time, Preroll);

        return new NeverLateRig(time, stream, host, engine, generator);
    }

    private sealed class NeverLateRig : IDisposable
    {
        public NeverLateRig(ManualTimeProvider time, MidiStream stream, FakeMusicHost host,
            MusicEngine engine, StallingMusicGenerator generator)
        {
            Time = time;
            Stream = stream;
            Host = host;
            Engine = engine;
            Generator = generator;
        }

        public ManualTimeProvider Time { get; }

        public MidiStream Stream { get; }

        public FakeMusicHost Host { get; }

        public MusicEngine Engine { get; }

        public StallingMusicGenerator Generator { get; }

        public Task RunUntil(Func<bool> until) => EnginePump.RunUntilAsync(Time, until);

        /// Lets the engine and the pull loop come to rest, so that what is asserted afterwards is
        /// what the engine settled on rather than whatever was in flight.
        public async Task Settle(int steps)
        {
            for (var step = 0; step < steps; step++)
            {
                await EnginePump.StepAsync(Time);
            }
        }

        public void Dispose() => Engine.Dispose();
    }
}
