using System;
using System.Collections.Generic;
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
/// THE LIFE OF A STREAM: that it keeps going, that it runs only as far ahead as it should, that it
/// can be re-prompted while it plays, that it degrades gracefully when the generator cannot keep
/// up, and that it says what it is doing.
/// </summary>
/// <remarks>
/// Everything here runs on a clock the test moves by hand - the engine's pump AND the generator's
/// pacing, which is the whole point: two clocks would mean an engine measuring hours against a bar
/// of music. Nothing sleeps and nothing depends on how busy the machine is.
/// </remarks>
public class MusicEngineLifecycleTests
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;
    private const long TicksPerBar = 4L * Resolution;
    private static readonly TimeSpan Bar = TimeSpan.FromSeconds(2.0);

    // --- KEEPING GOING ------------------------------------------------------------------------

    [Fact]
    public async Task the_music_carries_on_past_the_end_of_a_pass_by_asking_for_the_next_segment()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(1, Resolution));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 4);

        //Assert
        rig.Generator.PassCount.Should().BeGreaterThanOrEqualTo(4);
        rig.Stream.IsCompleted.Should().BeFalse();
        rig.Engine.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task every_seam_falls_on_a_bar_line_with_no_gap_and_no_overlap()
    {
        //Arrange - one bar of four quarter notes, looped
        using var rig = Build(TestMusic.Bars(1, Resolution));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 4);

        //Assert - the twelfth note of a looping one-bar piece is at tick 11 x 480, not 11 x 480 + a seam
        var ticks = NoteTicks(rig.Stream).Take(12).ToArray();
        ticks.Should().Equal(Enumerable.Range(0, 12).Select(i => i * (long)Resolution));
        ticks.Where(tick => tick % TicksPerBar == 0L).Should().HaveCount(3);
    }

    [Fact]
    public async Task a_seam_does_not_say_again_what_the_music_is_already_saying()
    {
        //Arrange - a piece that sets up everything at its start, played several times over
        using var rig = Build(TestMusic.OneBarWithAFullSetup(Resolution));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 4);

        //Assert - said once, at tick 0, and never again at a seam
        var committed = Committed(rig.Stream);
        committed.OfType<TempoEvent>().Should().HaveCount(1);
        committed.OfType<TimeSignatureEvent>().Should().HaveCount(1);
        committed.OfType<KeySignatureEvent>().Should().HaveCount(1);
        committed.OfType<PatchChangeEvent>().Should().HaveCount(1);
        committed.OfType<ControlChangeEvent>().Should().HaveCount(2);
        committed.Where(IsState).Should().AllSatisfy(state => state.AbsoluteTime.Should().Be(0L));
    }

    [Fact]
    public async Task a_change_that_really_is_a_change_still_goes_through_at_a_seam()
    {
        //Arrange - the piece ends at a different tempo from the one it started at, so the next
        //pass really does have to set the tempo back
        var music = TestMusic.Bars(1, Resolution);
        music.AddEvent(new TempoEvent(300000, TicksPerBar - 1L), 0);

        using var rig = Build(music);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act - wait for the tempo changes to reach the TIMELINE, which is what is asserted; a
        //segment having been started is not the same thing as its music having been committed
        await rig.RunUntil(() => Committed(rig.Stream).OfType<TempoEvent>().Count() > 2);

        //Assert
        var tempos = Committed(rig.Stream).OfType<TempoEvent>().ToArray();
        tempos.Length.Should().BeGreaterThan(2);
        tempos.Should().Contain(tempo => tempo.AbsoluteTime == TicksPerBar &&
            tempo.MicrosecondsPerQuarterNote == 500000);
    }

    [Fact]
    public async Task nothing_is_cut_short_at_a_seam()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(1, Resolution));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 4);

        //Assert - every note keeps the length the generator gave it, and nothing else was added
        var notes = Committed(rig.Stream).OfType<NoteOnEvent>().ToArray();
        notes.Should().NotBeEmpty();
        notes.Should().AllSatisfy(note => note.NoteLength.Should().Be(Resolution));
        Recording(rig.Stream).Where(MidiEvent.IsNoteOff).Should().HaveCount(notes.Length);
    }

    [Fact]
    public async Task a_continuation_hands_the_generator_the_music_so_far()
    {
        //Arrange
        using var rig = Build(TestMusic.OneBarWithAFullSetup(Resolution));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 2);

        //Assert
        var second = rig.Generator.Requests[1];
        second.Continuation.Should().NotBeNull();
        second.Continuation.BeatsPerMinute.Should().Be(120.0);
        second.Continuation.Meter.Should().Be(new MusicMeter(4, 4));
        second.Continuation.Key.Should().Be("C");
        second.Continuation.Mode.Should().Be(MusicMode.Major);
        second.Continuation.ChannelPrograms[1].Should().Be(GeneralMidiProgram.Celesta);
        second.Continuation.Tail.Should().NotBeNull();
        second.Continuation.Tail.GetTrackEvents(0).OfType<NoteOnEvent>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task the_first_request_carries_no_continuation_because_there_is_nothing_to_continue()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(1, Resolution));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 2);

        //Assert
        rig.Generator.Requests[0].Continuation.Should().BeNull();
    }

    [Fact]
    public async Task every_segment_gets_its_own_seed_and_exactly_the_same_sampling_controls()
    {
        //Arrange
        var request = new MusicRequest { Seed = 20260918 };
        request.Controls.Temperature = 0.65;
        request.Controls.TopK = 24;

        using var rig = Build(TestMusic.Bars(1, Resolution), request: request);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 4);

        //Assert - a new seed each time, and the character never shifts mid-piece
        var asked = rig.Generator.Requests.Take(4).ToArray();
        asked.Select(one => one.Seed).Should().OnlyHaveUniqueItems();
        asked.Should().AllSatisfy(one =>
        {
            one.Controls.Temperature.Should().Be(0.65);
            one.Controls.TopK.Should().Be(24);
            one.Controls.TopP.Should().Be(MusicGenerationControls.DefaultTopP);
        });
    }

    [Fact]
    public async Task a_generator_that_was_never_given_a_seed_is_never_given_one()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(1, Resolution));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 3);

        //Assert
        rig.Generator.Requests.Should().AllSatisfy(one => one.Seed.Should().BeNull());
    }

    [Fact]
    public async Task the_stop_policy_ends_the_music_when_the_pass_ends()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(1, Resolution), policy: EndOfPiecePolicy.Stop);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Stream.IsCompleted);

        //Assert
        rig.Engine.SegmentCount.Should().Be(1);
        rig.Generator.PassCount.Should().Be(1);
        rig.Engine.IsFinished.Should().BeTrue();
    }

    [Fact]
    public async Task stopping_ends_the_music_rather_than_starting_another_segment()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(1, Resolution));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 2);

        //Act
        rig.Engine.Stop();
        var segments = rig.Engine.SegmentCount;
        rig.Engine.Pump();
        rig.Engine.Pump();

        //Assert
        rig.Engine.SegmentCount.Should().Be(segments);
    }

    // --- HOW FAR AHEAD ------------------------------------------------------------------------

    [Fact]
    public async Task pulling_stops_when_the_lead_reaches_the_generate_ahead_window()
    {
        //Arrange - the head never moves, so the lead only ever grows
        using var rig = Build(TestMusic.Bars(32, Resolution), pacingRate: 8.0,
            generateAhead: TimeSpan.FromSeconds(10.0));
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.Lead >= TimeSpan.FromSeconds(10.0));
        await rig.Settle(20);
        var pulled = rig.Generator.ItemCount;
        await rig.Settle(200);

        //Assert - nothing more was pulled, and the lead did not run away
        rig.Generator.ItemCount.Should().Be(pulled);
        rig.Engine.Lead.Should().BeLessThan(TimeSpan.FromSeconds(14.0));
    }

    [Fact]
    public async Task pulling_starts_again_once_the_lead_has_fallen_to_half_the_window()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(32, Resolution), pacingRate: 8.0,
            generateAhead: TimeSpan.FromSeconds(10.0));
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.Lead >= TimeSpan.FromSeconds(10.0));
        await rig.Settle(20);
        var pulled = rig.Generator.ItemCount;

        //Act - the head plays six seconds, which takes the lead under five
        rig.Host.HeadPosition = TimeSpan.FromSeconds(6.0);
        await rig.RunUntil(() => rig.Generator.ItemCount > pulled);

        //Assert
        rig.Generator.ItemCount.Should().BeGreaterThan(pulled);
    }

    [Fact]
    public async Task a_head_that_has_moved_a_little_does_not_start_the_generator_again()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(32, Resolution), pacingRate: 8.0,
            generateAhead: TimeSpan.FromSeconds(10.0));
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.Lead >= TimeSpan.FromSeconds(10.0));
        await rig.Settle(20);
        var pulled = rig.Generator.ItemCount;

        //Act - two seconds is not half the window, so the gap between the marks still holds
        rig.Host.HeadPosition = TimeSpan.FromSeconds(2.0);
        await rig.Settle(200);

        //Assert
        rig.Generator.ItemCount.Should().Be(pulled);
    }

    [Fact]
    public async Task the_window_is_seconds_of_music_and_holds_across_a_tempo_change()
    {
        //Arrange - the music doubles in speed half-way through every bar, so ticks and seconds
        //part company completely
        var music = TestMusic.Bars(32, Resolution);
        music.AddEvent(new TempoEvent(250000, 2L * Resolution), 0);

        using var rig = Build(music, pacingRate: 8.0, generateAhead: TimeSpan.FromSeconds(10.0));
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.Lead >= TimeSpan.FromSeconds(10.0));
        await rig.Settle(200);

        //Assert - measured in SECONDS, the lead stopped part-way through a piece that is 32.5
        //seconds long. A window counted in TICKS would have let all of it through, because after
        //the first bar there are twice as many ticks to the second.
        //
        //THE UPPER BOUND IS "NOT THE WHOLE PIECE", NOT A TIGHT ONE, and it says so from two
        //directions. The window is soft - the pull loop only looks at it between items and the
        //pump only decides every hundred milliseconds - and on a BUSY machine the test's own clock
        //can run on while the generation is waiting for a thread, so the generator releases a
        //burst the moment it is next scheduled. An idle machine stops at about 11 seconds; four
        //suites at once has been seen to reach 30.
        rig.Engine.Lead.Should().BeLessThan(TimeSpan.FromSeconds(32.5));
        rig.Generator.ItemCount.Should().BeLessThan(130);
    }

    // --- STARVATION AND DIAGNOSTICS -----------------------------------------------------------

    [Fact]
    public async Task an_ordinary_start_is_not_counted_as_a_gap()
    {
        //Arrange - a generator that keeps up easily, and a head that is not waiting on anything
        using var rig = Build(TestMusic.Bars(4, Resolution), pacingRate: 8.0);

        //Act - the wait for the first pre-roll, and then some music
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.WrittenThroughTick > 0L);
        await rig.Settle(20);

        //Assert - a gap is the music RUNNING OUT, and it has not run out: it has only just begun
        rig.Engine.StarvationGapCount.Should().Be(0);
    }

    [Fact]
    public async Task a_gap_is_counted_once_however_long_the_head_waits_in_it()
    {
        //Arrange - the music has to have STARTED before it can run out, so the count begins at 0
        using var rig = Build(TestMusic.Bars(1, Resolution));
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.WrittenThroughTick > 0L);
        await rig.Settle(20);
        rig.Engine.StarvationGapCount.Should().Be(0);

        //Act
        rig.Host.Starved = true;
        rig.Engine.Pump();
        rig.Engine.Pump();
        rig.Engine.Pump();
        var afterOneGap = rig.Engine.StarvationGapCount;

        rig.Host.Starved = false;
        rig.Engine.Pump();
        rig.Host.Starved = true;
        rig.Engine.Pump();

        //Assert
        afterOneGap.Should().Be(1);
        rig.Engine.StarvationGapCount.Should().Be(2);
    }

    [Fact]
    public async Task the_real_time_factor_is_measured_from_music_produced_against_time_spent()
    {
        //Arrange - a replay at four times real time is a generator four times faster than the music
        using var rig = Build(TestMusic.Bars(8, Resolution), pacingRate: 4.0,
            generateAhead: TimeSpan.FromSeconds(60.0));
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.RealTimeFactor > 0.0 &&
            rig.Engine.Lead > TimeSpan.FromSeconds(8.0));

        //Assert
        rig.Engine.RealTimeFactor.Should().BeApproximately(4.0, 0.5);
        rig.Engine.Mode.Should().Be(MusicDeliveryMode.Streaming);
    }

    [Fact]
    public async Task nothing_is_measured_before_the_generator_has_written_anything()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(1, Resolution));

        //Act
        rig.Engine.Pump();
        rig.Time.Advance(TimeSpan.FromSeconds(30.0));
        rig.Engine.Pump();
        await Task.CompletedTask;

        //Assert - half a minute of doing nothing is not evidence that the machine cannot keep up,
        //and "not measured yet" is null rather than a nought a game loop would read as "too slow"
        rig.Engine.RealTimeFactor.Should().BeNull();
        rig.Engine.Mode.Should().Be(MusicDeliveryMode.Streaming);
    }

    // --- THE SEGMENT-AT-A-TIME FALLBACK -------------------------------------------------------

    [Fact]
    public async Task a_generator_slower_than_real_time_delivers_a_whole_segment_at_a_time()
    {
        //Arrange - a quarter of real time, and segments of two bars
        using var rig = Build(TestMusic.Bars(8, Resolution), pacingRate: 0.25,
            generateAhead: TimeSpan.FromSeconds(4.0));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.Mode == MusicDeliveryMode.SegmentAtATime &&
            rig.Stream.HorizonTicks >= 2L * TicksPerBar);

        //Assert
        rig.Engine.RealTimeFactor.Should().BeLessThan(1.0);
        rig.Engine.Mode.Should().Be(MusicDeliveryMode.SegmentAtATime);
    }

    [Fact]
    public async Task in_that_mode_the_music_only_ever_stops_on_a_bar_line()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(8, Resolution), pacingRate: 0.25,
            generateAhead: TimeSpan.FromSeconds(4.0));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        var horizons = new List<long>();
        var lastSeen = -1L;

        //Act - where the stream happened to be when the mode changed is not the fallback's doing;
        //every move AFTER that is.
        await rig.RunUntil(() =>
        {
            if (rig.Engine.Mode != MusicDeliveryMode.SegmentAtATime)
            {
                return false;
            }

            if (lastSeen < 0L)
            {
                lastSeen = rig.Engine.CommittedThroughTick;

                return false;
            }

            if (rig.Engine.CommittedThroughTick != lastSeen)
            {
                lastSeen = rig.Engine.CommittedThroughTick;
                horizons.Add(lastSeen);
            }

            return horizons.Count >= 3;
        });

        //Assert - a phrase then a rest, never a stall in the middle of a phrase
        horizons.Should().AllSatisfy(tick => (tick % TicksPerBar).Should().Be(0L));
    }

    [Fact]
    public async Task a_generator_that_keeps_up_never_leaves_the_streaming_mode()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(8, Resolution), pacingRate: 4.0);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        var everFellBack = false;

        //Act
        await rig.RunUntil(() =>
        {
            everFellBack |= rig.Engine.Mode == MusicDeliveryMode.SegmentAtATime;

            return rig.Engine.SegmentCount >= 3;
        });

        //Assert
        everFellBack.Should().BeFalse();
        rig.Engine.Mode.Should().Be(MusicDeliveryMode.Streaming);
    }

    [Fact]
    public async Task the_fallback_gives_way_again_when_the_generator_catches_up()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(16, Resolution), pacingRate: 0.25,
            generateAhead: TimeSpan.FromSeconds(4.0));
        rig.Host.FollowTheMusic();
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.Mode == MusicDeliveryMode.SegmentAtATime);

        //Act - the machine frees up and the generator starts keeping up again
        rig.Generator.Inner.PacingRate = 8.0;
        await rig.RunUntil(() => rig.Engine.Mode == MusicDeliveryMode.Streaming);

        //Assert
        rig.Engine.Mode.Should().Be(MusicDeliveryMode.Streaming);
        rig.Engine.RealTimeFactor.Should().BeGreaterThan(1.0);
    }

    // --- A FOLLOW-UP PROMPT -------------------------------------------------------------------

    [Fact]
    public async Task a_follow_up_takes_over_at_a_bar_line_beyond_what_was_already_committed()
    {
        //Arrange - the head does not move, so what is committed is exactly the commit window
        using var rig = Build(TestMusic.Bars(4, Resolution), pacingRate: 4.0);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var committedAtTheCall = rig.Stream.HorizonTicks;
        var second = Generator("Second", NewMusic(), rig.Time, 4.0);

        //Act
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => rig.Engine.ActiveGenerator == second);

        // The head plays on, so the music beyond the switch reaches the timeline in its turn.
        rig.Host.FollowTheMusic();
        await rig.RunUntil(() => FirstTickOfTheNewMusic(rig.Stream) >= 0L);

        //Assert
        var switchTick = FirstTickOfTheNewMusic(rig.Stream);
        switchTick.Should().BeGreaterThan(committedAtTheCall);
        (switchTick % TicksPerBar).Should().Be(0L);
    }

    [Fact]
    public async Task the_old_lookahead_never_reaches_the_timeline()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(8, Resolution), pacingRate: 8.0);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.UncommittedCount > 0 &&
            rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var second = Generator("Second", NewMusic(), rig.Time, 8.0);

        //Act
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => rig.Engine.ActiveGenerator == second);

        rig.Host.FollowTheMusic();
        await rig.RunUntil(() => FirstTickOfTheNewMusic(rig.Stream) >= 0L);
        await rig.Settle(200);

        //Assert - not one note of the music that was replaced is heard at or after the switch
        var switchTick = FirstTickOfTheNewMusic(rig.Stream);
        Committed(rig.Stream).OfType<NoteOnEvent>()
            .Where(note => note.AbsoluteTime >= switchTick)
            .Should().AllSatisfy(note => note.NoteNumber.Should().BeGreaterThanOrEqualTo(90));
    }

    [Fact]
    public async Task the_switch_costs_no_more_than_the_commit_window_and_the_new_pre_roll()
    {
        //Arrange
        var preroll = TimeSpan.FromSeconds(1.0);
        using var rig = Build(TestMusic.Bars(8, Resolution), pacingRate: 4.0, preroll: preroll);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var second = Generator("Second", NewMusic(), rig.Time, 4.0);
        var startedAt = rig.Time.GetUtcNow();

        //Act
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => rig.Engine.ActiveGenerator == second);

        //Assert - both the waiting and the point in the music where it lands are bounded
        var waited = rig.Time.GetUtcNow() - startedAt;
        waited.Should().BeLessThanOrEqualTo(rig.Engine.CommitWindow + preroll);

        rig.Host.FollowTheMusic();
        await rig.RunUntil(() => FirstTickOfTheNewMusic(rig.Stream) >= 0L);
        var switchAt = rig.Buffer.TimeOfTick(FirstTickOfTheNewMusic(rig.Stream));
        switchAt.Should().BeLessThanOrEqualTo(rig.Engine.CommitWindow + preroll + Bar);
    }

    [Fact]
    public async Task a_second_follow_up_replaces_one_that_has_not_taken_over_yet()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(8, Resolution), pacingRate: 8.0);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var abandoned = Generator("Abandoned", NewMusic(), rig.Time, 0.02);
        var wanted = Generator("Wanted", NewMusic(), rig.Time, 8.0);

        //Act - the first is still gathering its pre-roll when the second arrives
        rig.Engine.FollowUp(abandoned, new MusicRequest { TicksPerQuarterNote = Resolution });
        rig.Engine.FollowUp(wanted, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => rig.Engine.ActiveGenerator == wanted);

        //Assert
        rig.Engine.ActiveGenerator.Should().BeSameAs(wanted);
        rig.Engine.HasPendingFollowUp.Should().BeFalse();
    }

    [Fact]
    public async Task the_active_generator_only_changes_at_the_switch_and_not_at_the_call()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(8, Resolution), pacingRate: 8.0);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var second = Generator("Second", NewMusic(), rig.Time, 0.5);

        //Act
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });
        var atTheCall = rig.Engine.ActiveGenerator;

        //Assert
        atTheCall.Should().BeSameAs(rig.Generator);
        rig.Engine.HasPendingFollowUp.Should().BeTrue();

        await rig.RunUntil(() => rig.Engine.ActiveGenerator == second);
        rig.Engine.ActiveGenerator.Should().BeSameAs(second);
    }

    [Fact]
    public async Task a_note_still_sounding_at_the_switch_keeps_its_whole_length()
    {
        //Arrange - one note held for eight bars, so the switch lands in the middle of it
        using var rig = Build(TestMusic.OneLongNote(8, Resolution), pacingRate: 8.0);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var second = Generator("Second", NewMusic(), rig.Time, 8.0);

        //Act
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => rig.Engine.ActiveGenerator == second);

        rig.Host.FollowTheMusic();
        await rig.RunUntil(() => FirstTickOfTheNewMusic(rig.Stream) >= 0L);

        //Assert - nothing was cut: the note still runs past the seam, exactly as written
        var switchTick = FirstTickOfTheNewMusic(rig.Stream);
        var held = Committed(rig.Stream).OfType<NoteOnEvent>().First(note => note.NoteNumber == 60);
        held.NoteLength.Should().Be(8 * (int)TicksPerBar);
        (held.AbsoluteTime + held.NoteLength).Should().BeGreaterThan(switchTick);
    }

    [Fact]
    public async Task a_follow_up_after_the_music_has_ended_changes_nothing()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(1, Resolution), policy: EndOfPiecePolicy.Stop);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.IsCompleted);

        var second = Generator("Second", NewMusic(), rig.Time, 8.0);

        //Act
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });

        //Assert
        rig.Engine.HasPendingFollowUp.Should().BeFalse();
        rig.Engine.ActiveGenerator.Should().BeSameAs(rig.Generator);
    }

    // --- A FOLLOW-UP OVER THE GENERATOR THAT IS ALREADY GENERATING ----------------------------

    [Fact]
    public async Task a_follow_up_over_the_same_generator_never_asks_it_for_two_pieces_at_once()
    {
        //Arrange - a generator that refuses a second generation, the way a model does
        using var rig = Build(TestMusic.Bars(16, Resolution), pacingRate: 8.0,
            generateAhead: TimeSpan.FromSeconds(6.0), oneGenerationAtATime: true);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var segmentsAtTheCall = rig.Engine.SegmentCount;

        //Act - the same instance, a new prompt
        rig.Engine.FollowUp(rig.Engine.ActiveGenerator,
            new MusicRequest { TicksPerQuarterNote = Resolution, Text = "something else" });

        await rig.RunUntil(() => !rig.Engine.HasPendingFollowUp &&
                                 rig.Engine.SegmentCount > segmentsAtTheCall);

        //Assert - it handed over: two generations, never both at once, nothing refused
        rig.OneAtATime.HighestConcurrentGenerations.Should().Be(1);
        rig.OneAtATime.RefusalCount.Should().Be(0);
        rig.OneAtATime.StartCount.Should().BeGreaterThanOrEqualTo(2);
        rig.Generator.Requests[rig.Generator.Requests.Count - 1].Text.Should().Be("something else");
        rig.Engine.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task the_music_already_settled_keeps_playing_while_the_hand_over_happens()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(16, Resolution), pacingRate: 8.0,
            generateAhead: TimeSpan.FromSeconds(6.0), oneGenerationAtATime: true);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var committedAtTheCall = rig.Engine.CommittedThroughTick;
        var segmentsAtTheCall = rig.Engine.SegmentCount;

        //Act
        rig.Engine.FollowUp(rig.Engine.ActiveGenerator,
            new MusicRequest { TicksPerQuarterNote = Resolution, Text = "something else" });

        await rig.RunUntil(() => !rig.Engine.HasPendingFollowUp &&
                                 rig.Engine.SegmentCount > segmentsAtTheCall);

        //Assert - music went on reaching the timeline through the hand-over, in time and in order
        rig.Engine.CommittedThroughTick.Should().BeGreaterThan(committedAtTheCall);
        rig.Stream.LateEventCount.Should().Be(0);

        var switchTick = rig.Engine.SegmentStartTicks[rig.Engine.SegmentStartTicks.Count - 1];
        var heard = NoteTicks(rig.Stream).Where(tick => tick < switchTick).ToArray();

        heard.Length.Should().BeGreaterThan(3);
        heard.Should().Equal(Enumerable.Range(0, heard.Length).Select(i => i * (long)Resolution));
    }

    [Fact]
    public async Task the_hand_over_still_takes_over_at_a_bar_line()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(16, Resolution), pacingRate: 8.0,
            generateAhead: TimeSpan.FromSeconds(6.0), oneGenerationAtATime: true);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var segmentsAtTheCall = rig.Engine.SegmentCount;

        //Act
        rig.Engine.FollowUp(rig.Engine.ActiveGenerator,
            new MusicRequest { TicksPerQuarterNote = Resolution, Text = "something else" });

        await rig.RunUntil(() => !rig.Engine.HasPendingFollowUp &&
                                 rig.Engine.SegmentCount > segmentsAtTheCall);

        //Assert - the switch is where the new segment starts, and that is a bar line
        var switchTick = rig.Engine.SegmentStartTicks[rig.Engine.SegmentStartTicks.Count - 1];
        switchTick.Should().BeGreaterThan(0L);
        (switchTick % TicksPerBar).Should().Be(0L);
    }

    [Fact]
    public async Task a_follow_up_naming_a_different_generator_still_generates_alongside_the_old_one()
    {
        //Arrange
        using var rig = Build(TestMusic.Bars(16, Resolution), pacingRate: 8.0,
            generateAhead: TimeSpan.FromSeconds(6.0), oneGenerationAtATime: true);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Stream.HorizonTime >= rig.Engine.CommitWindow);

        var second = new OneAtATimeMusicGenerator(Generator("Second", NewMusic(), rig.Time, 8.0));

        //Act
        rig.Engine.FollowUp(second, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => second.StartCount > 0);

        //Assert - nothing was cancelled to make room for it: both are generating
        rig.OneAtATime.IsGenerating.Should().BeTrue();
        second.RefusalCount.Should().Be(0);
    }

    // --- SEEDS --------------------------------------------------------------------------------

    [Fact]
    public void a_segments_seed_is_derived_from_the_pieces_seed_and_is_always_the_same() =>
        SegmentSeed.Derive(20260918, 3).Should().Be(SegmentSeed.Derive(20260918, 3));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(17)]
    public void a_segments_seed_differs_from_the_pieces_own(int segment) =>
        SegmentSeed.Derive(20260918, segment).Should().NotBe(20260918);

    [Fact]
    public void every_segment_of_a_long_piece_gets_a_distinct_seed() =>
        Enumerable.Range(1, 200).Select(segment => SegmentSeed.Derive(20260918, segment))
            .Should().OnlyHaveUniqueItems();

    [Fact]
    public void a_derived_seed_is_never_negative() =>
        Enumerable.Range(1, 200).Select(segment => SegmentSeed.Derive(int.MinValue, segment))
            .Should().AllSatisfy(seed => seed.Should().BeGreaterThanOrEqualTo(0));

    // --- the rig ------------------------------------------------------------------------------

    private static bool IsState(MidiEvent midiEvent) =>
        midiEvent is TempoEvent || midiEvent is TimeSignatureEvent || midiEvent is KeySignatureEvent ||
        midiEvent is PatchChangeEvent || midiEvent is ControlChangeEvent;

    private static MidiEventCollection NewMusic()
    {
        // Notes a test can tell apart from the music that was playing at a glance.
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();
        music.AddEvent(new TempoEvent(500000, 0L), 0);
        music.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);

        for (var beat = 0; beat < 16; beat++)
        {
            var note = new NoteOnEvent(beat * (long)Resolution, 2, 90 + (beat % 8), 100, Resolution);
            music.AddEvent(note, 0);
            music.AddEvent(note.OffEvent, 0);
        }

        return music;
    }

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
        Committed(stream).OfType<NoteOnEvent>().Select(note => note.AbsoluteTime);

    private static RecordingMusicGenerator Generator(string name, MidiEventCollection music,
        ManualTimeProvider time, double pacingRate) =>
        new RecordingMusicGenerator(name,
            ReplayOnTestClock.On(ReplayMusicGenerator.FromMidiEvents(name, music), time, pacingRate));

    private static LifecycleRig Build(MidiEventCollection music, double pacingRate = 8.0,
        TimeSpan? preroll = null, EndOfPiecePolicy policy = EndOfPiecePolicy.KeepGenerating,
        TimeSpan? generateAhead = null, MusicRequest request = null,
        bool oneGenerationAtATime = false)
    {
        var time = new ManualTimeProvider();
        var generator = Generator("Lifecycle", music, time, pacingRate);
        var oneAtATime = oneGenerationAtATime ? new OneAtATimeMusicGenerator(generator) : null;
        IMusicGenerator asked = oneAtATime == null ? (IMusicGenerator)generator : oneAtATime;
        var stream = new MidiStream(Resolution);
        var host = new FakeMusicHost(SampleRate);
        var voicer = new RenditionVoicer(
            MusicRenditionRegistry.Resolve(BuiltInRenditions.Automatic).Clone(),
            TestInstrumentLibrary.Complete("LifecycleParts"), SampleRate, null, 1.0F);

        host.Load(stream, _ => voicer.Router, (synthesizer, channel, command, data1, data2) =>
            synthesizer.ProcessMidiMessage(channel, command, data1, data2));

        var with = request == null ? new MusicRequest() : request.Clone();
        with.TicksPerQuarterNote = Resolution;

        var engine = new MusicEngine(asked, with, stream, voicer, host,
            preroll ?? TimeSpan.FromSeconds(1.0), time, 0L)
        {
            EndOfPiece = policy,
            GenerateAhead = generateAhead ?? MusicEngine.DefaultGenerateAhead
        };

        // A HEAD THAT NEVER FALLS BEHIND SITS AT THE END OF THE MUSIC THAT HAS BEEN WRITTEN - see
        // FakeMusicHost - and the engine is what knows where that is.
        host.EndOfTheMusic = () => engine.WrittenThroughTime;

        return new LifecycleRig(time, stream, host, engine, generator,
            new SettledMusicBuffer(Resolution), oneAtATime);
    }

    private sealed class LifecycleRig : IDisposable
    {
        public LifecycleRig(ManualTimeProvider time, MidiStream stream, FakeMusicHost host,
            MusicEngine engine, RecordingMusicGenerator generator, SettledMusicBuffer buffer,
            OneAtATimeMusicGenerator oneAtATime)
        {
            Time = time;
            Stream = stream;
            Host = host;
            Engine = engine;
            Generator = generator;
            Buffer = buffer;
            OneAtATime = oneAtATime;
        }

        /// The generator the engine was given, when the rig was built with one that refuses a
        /// second concurrent generation; null otherwise.
        public OneAtATimeMusicGenerator OneAtATime { get; }

        public ManualTimeProvider Time { get; }

        public MidiStream Stream { get; }

        public FakeMusicHost Host { get; }

        public MusicEngine Engine { get; }

        public RecordingMusicGenerator Generator { get; }

        public SettledMusicBuffer Buffer { get; }

        public Task RunUntil(Func<bool> until) => EnginePump.RunUntilAsync(Time, until);

        /// Lets the engine and the pull loop come to rest, so that a count taken afterwards is the
        /// count the engine settled on rather than whatever was in flight at the instant a
        /// condition first became true.
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
