using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
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
/// The engine: what it commits to the timeline, how far ahead of the play head it commits it, how
/// a settled rest still moves the horizon, and how a piece ends.
/// </summary>
/// <remarks>
/// Every test here runs on a clock it moves by hand, and the play head is moved by hand too, so
/// nothing sleeps and nothing depends on how busy the machine is.
/// </remarks>
public class MusicEngineTests
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;

    [Fact]
    public async Task the_timeline_never_holds_more_than_the_commit_window_ahead_of_the_head()
    {
        //Arrange
        using var rig = Rig(RealTime(), TimeSpan.FromSeconds(1.0));
        rig.Engine.Start();

        //Act - the head never moves, so the window never moves either
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Engine.UncommittedCount > 0 &&
            rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        //Assert
        var limit = rig.Buffer.TickAtTime(rig.Engine.CommitWindow);
        CommittedStartTicks(rig.Stream).Should().AllSatisfy(tick => tick.Should().BeLessThanOrEqualTo(limit));
        rig.Engine.UncommittedCount.Should().BeGreaterThan(0);
        rig.Stream.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task moving_the_head_forward_lets_more_music_onto_the_timeline()
    {
        //Arrange
        using var rig = Rig(RealTime(), TimeSpan.FromSeconds(1.0));
        rig.Engine.Start();
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Engine.UncommittedCount > 0);
        var before = rig.Stream.HorizonTicks;

        //Act
        rig.Host.HeadPosition = TimeSpan.FromSeconds(4.0);
        rig.Engine.Pump();

        //Assert
        rig.Stream.HorizonTicks.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task a_settled_stretch_with_nothing_in_it_still_moves_the_horizon()
    {
        //Arrange - one note at the start, and the rest of the bar settled silence
        using var rig = Rig(Sparse(), TimeSpan.FromSeconds(1.0));
        rig.Engine.Start();

        //Act
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Stream.IsCompleted);

        //Assert - the note ends at tick 480 but the settled bar runs to 1920
        rig.Stream.HorizonTicks.Should().Be(4L * Resolution);
        rig.Stream.ToMidiEventCollection().GetTrackEvents(0)
            .Should().NotContain(midiEvent => midiEvent is TextEvent);
    }

    [Fact]
    public async Task settled_silence_does_not_add_messages_to_the_timeline()
    {
        //Arrange
        using var rig = Rig(Sparse(), TimeSpan.FromSeconds(1.0));
        rig.Engine.Start();

        //Act
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Stream.IsCompleted);

        //Assert - advancing past the last note adds no synthetic event for a player to dispatch
        Committed(rig.Stream).Should().Equal(await GeneratedAsync(Sparse()));
        rig.Stream.HorizonTicks.Should().Be(4L * Resolution);
    }

    [Fact]
    public async Task what_was_committed_is_what_the_generator_produced_tick_for_tick()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromAbc("Tune", TestMusic.SimpleAbc);
        var expected = await GeneratedAsync(generator);

        using var rig = Rig(generator, TimeSpan.FromSeconds(1.0));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Stream.IsCompleted);

        //Assert
        Committed(rig.Stream).Should().Equal(expected);
    }

    [Fact]
    public async Task the_piece_ends_by_completing_the_timeline()
    {
        //Arrange
        using var rig = Rig(Fast(), TimeSpan.FromSeconds(1.0));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Stream.IsCompleted);

        //Assert
        rig.Stream.IsCompleted.Should().BeTrue();
        rig.Engine.IsFinished.Should().BeTrue();
        rig.Engine.UncommittedCount.Should().Be(0);
        rig.Engine.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task nothing_arrives_late_while_the_head_stays_behind_the_music()
    {
        //Arrange
        using var rig = Rig(Fast(), TimeSpan.FromSeconds(1.0));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Stream.IsCompleted);

        //Assert
        rig.Stream.LateEventCount.Should().Be(0);
    }

    [Fact]
    public async Task the_commit_window_is_twice_the_pre_roll()
    {
        //Arrange
        using var rig = Rig(Fast(), TimeSpan.FromSeconds(5.0));

        //Act
        await Task.CompletedTask;

        //Assert
        rig.Engine.CommitWindow.Should().Be(TimeSpan.FromSeconds(10.0));
        rig.Stream.Preroll.Should().Be(TimeSpan.FromSeconds(5.0));
    }

    [Fact]
    public async Task a_very_short_pre_roll_still_gets_a_workable_commit_window()
    {
        //Arrange
        using var rig = Rig(Fast(), TimeSpan.Zero);

        //Act
        await Task.CompletedTask;

        //Assert
        rig.Engine.CommitWindow.Should().Be(MusicEngine.MinimumCommitWindow);
    }

    [Fact]
    public async Task not_pulling_is_pausing_and_pulling_again_resumes()
    {
        //Arrange
        var pulling = false;
        using var rig = Rig(RealTime(), TimeSpan.FromSeconds(1.0));
        rig.Host.FollowTheMusic();
        rig.Engine.CanPull = () => pulling;
        rig.Engine.Start();

        //Act
        for (var step = 0; step < 200; step++)
        {
            rig.Time.Advance(TimeSpan.FromMilliseconds(100.0));
            await Task.Yield();
        }

        var whilePaused = rig.Stream.EventCount;
        pulling = true;
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Stream.IsCompleted);

        //Assert
        whilePaused.Should().BeLessThan(rig.Stream.EventCount);
        rig.Stream.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task the_lookahead_can_be_thrown_away_without_touching_what_was_committed()
    {
        //Arrange
        using var rig = Rig(Fast(), TimeSpan.FromSeconds(1.0));
        rig.Engine.Start();
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Engine.UncommittedCount > 0);
        var committed = rig.Stream.EventCount;

        //Act
        rig.Engine.DiscardUncommitted();

        //Assert
        rig.Engine.UncommittedCount.Should().Be(0);
        rig.Stream.EventCount.Should().Be(committed);
    }

    [Fact]
    public async Task the_next_segment_would_start_where_this_one_settled()
    {
        //Arrange
        using var rig = Rig(Fast(), TimeSpan.FromSeconds(1.0));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Stream.IsCompleted);

        //Assert - four bars of 4/4 at 480 ticks to the quarter note
        rig.Engine.NextSegmentStartTick.Should().Be(16L * Resolution);
    }

    [Fact]
    public async Task a_generator_that_fails_stops_the_music_rather_than_leaving_it_waiting()
    {
        //Arrange
        var generator = new FailingGenerator();
        using var rig = Rig(generator, TimeSpan.FromSeconds(1.0));
        rig.Engine.Start();

        //Act
        await EnginePump.RunUntilAsync(rig.Time, () => rig.Stream.IsCompleted);

        //Assert
        rig.Engine.GenerationError.Should().NotBeNull();
        rig.Stream.IsCompleted.Should().BeTrue();
    }

    private static ReplayMusicGenerator Fast() =>
        ReplayMusicGenerator.FromAbc("Tune", TestMusic.SimpleAbc);

    // One note at the start of a bar of 4/4 and nothing else: the music ends at tick 480 and the
    // pass ends at tick 1920, so there is settled silence with nothing in it to carry across.
    private static ReplayMusicGenerator Sparse()
    {
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();

        var note = new NoteOnEvent(0L, 1, 60, 100, Resolution);
        music.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);
        music.AddEvent(note, 0);
        music.AddEvent(note.OffEvent, 0);

        return ReplayMusicGenerator.FromMidiEvents("Sparse", music);
    }

    // A replay that releases its music at exactly the speed it plays, so that the engine's own
    // measurement puts it squarely in the streaming mode and these tests are about the commit
    // window rather than about the segment-at-a-time fallback, which has tests of its own.
    private static ReplayMusicGenerator RealTime()
    {
        var generator = ReplayMusicGenerator.FromAbc("PacedTune", TestMusic.SimpleAbc);
        generator.PacingRate = 1.0;

        return generator;
    }

    private static async Task<IReadOnlyList<string>> GeneratedAsync(IMusicGenerator generator)
    {
        var produced = new List<string>();
        var request = new MusicRequest { PaceInRealTime = false };

        await foreach (var item in generator.GenerateAsync(request, TestContext.Current.CancellationToken))
        {
            if (item.HasEvent)
            {
                produced.Add(Describe(item.Event));
            }
        }

        produced.Sort(StringComparer.Ordinal);

        return produced;
    }

    private static IReadOnlyList<string> Committed(MidiStream stream)
    {
        var recording = stream.ToMidiEventCollection();
        var events = new List<MidiEvent>();

        for (var track = 0; track < recording.Tracks; track++)
        {
            foreach (var midiEvent in recording.GetTrackEvents(track))
            {
                if (midiEvent == null || MidiEvent.IsNoteOff(midiEvent) ||
                    MidiEvent.IsEndTrack(midiEvent))
                {
                    continue;
                }

                events.Add(midiEvent);
            }
        }

        return events.Select(Describe).OrderBy(text => text, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<long> CommittedStartTicks(MidiStream stream)
    {
        var recording = stream.ToMidiEventCollection();
        var ticks = new List<long>();

        for (var track = 0; track < recording.Tracks; track++)
        {
            foreach (var midiEvent in recording.GetTrackEvents(track))
            {
                if (midiEvent != null && !MidiEvent.IsNoteOff(midiEvent) && !MidiEvent.IsEndTrack(midiEvent))
                {
                    ticks.Add(midiEvent.AbsoluteTime);
                }
            }
        }

        return ticks;
    }

    private static string Describe(MidiEvent midiEvent)
    {
        if (midiEvent is NoteOnEvent note)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|note|{1}|{2}|{3}|{4}",
                note.AbsoluteTime, note.Channel, note.NoteNumber, note.Velocity, note.NoteLength);
        }

        if (midiEvent is PatchChangeEvent patch)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|patch|{1}|{2}",
                patch.AbsoluteTime, patch.Channel, patch.Patch);
        }

        if (midiEvent is TempoEvent tempo)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|tempo|{1}",
                tempo.AbsoluteTime, tempo.MicrosecondsPerQuarterNote);
        }

        if (midiEvent is TimeSignatureEvent meter)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|meter|{1}/{2}",
                meter.AbsoluteTime, meter.Numerator, meter.Denominator);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", midiEvent.AbsoluteTime,
            midiEvent.GetType().Name, midiEvent.Channel);
    }

    private static EngineRig Rig(IMusicGenerator generator, TimeSpan preroll)
    {
        var time = new ManualTimeProvider();

        if (generator is ReplayMusicGenerator replay)
        {
            replay.TimeProvider = time;
        }

        var stream = new MidiStream(Resolution);
        var host = new FakeMusicHost(SampleRate);
        var voicer = new RenditionVoicer(MusicRenditionRegistry.Resolve(null).Clone(),
            TestInstrumentLibraries.Complete(), SampleRate, null, 1.0F);

        host.Load(stream, _ => voicer.Router, (synthesizer, channel, command, data1, data2) =>
            synthesizer.ProcessMidiMessage(channel, command, data1, data2));

        var engine = new MusicEngine(generator, new MusicRequest(), stream, voicer, host, preroll,
            time, 0L);

        // A HEAD THAT NEVER FALLS BEHIND SITS AT THE END OF THE MUSIC THAT HAS BEEN WRITTEN - see
        // FakeMusicHost - and the engine is what knows where that is.
        host.EndOfTheMusic = () => engine.WrittenThroughTime;

        return new EngineRig(time, stream, host, engine, new SettledMusicBuffer(Resolution));
    }

    private sealed class EngineRig : IDisposable
    {
        public EngineRig(ManualTimeProvider time, MidiStream stream, FakeMusicHost host,
            MusicEngine engine, SettledMusicBuffer buffer)
        {
            Time = time;
            Stream = stream;
            Host = host;
            Engine = engine;
            Buffer = buffer;
        }

        public ManualTimeProvider Time { get; }

        public MidiStream Stream { get; }

        public FakeMusicHost Host { get; }

        public MusicEngine Engine { get; }

        public SettledMusicBuffer Buffer { get; }

        public void Dispose() => Engine.Dispose();
    }

    private sealed class FailingGenerator : IMusicGenerator
    {
        public string Name => "Failing";

        public string Family => "Test";

        public string Description => "A generator that gives up part-way through a piece.";

        public MusicRequestFeatures Honours => MusicRequestFeatures.None;

        public bool IsLoaded => true;

        public Task PreloadAsync(System.Threading.CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Release()
        {
        }

        public async IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            System.Threading.CancellationToken cancellationToken)
        {
            await Task.Yield();

            yield return GeneratedMusicEvent.FromMidiEvent(new NoteOnEvent(0L, 1, 60, 100, Resolution), 0L);

            throw new InvalidOperationException("The model stopped.");
        }
    }
}
