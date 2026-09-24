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
/// THE SEAMS OF A STREAM THAT KEEPS GOING: whether each segment is primed with the music so far or
/// starts fresh, and - at a fresh seam - how the outgoing and incoming pieces are overlapped and
/// where on the timeline everything lands.
/// </summary>
/// <remarks>
/// Everything runs on a clock the test moves by hand. The piece is two bars of quarter notes at 120
/// beats per minute, so a bar is two seconds, a beat half a second, and a one-second crossfade is
/// exactly two beats - 960 ticks at this resolution.
/// </remarks>
public class MusicEngineSeamTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;
    private const long TicksPerBar = 4L * Resolution;
    private const long PieceTicks = 2L * TicksPerBar;
    private const long FadeTicks = 2L * Resolution;
    private static readonly TimeSpan Fade = TimeSpan.FromSeconds(1.0);

    // --- PRIMING ------------------------------------------------------------------------------

    [Fact]
    public async Task primed_is_the_default_and_every_segment_is_asked_for_with_the_music_so_far()
    {
        //Arrange
        using var rig = Build();
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 4);

        //Assert
        rig.Engine.SegmentPriming.Should().Be(SegmentPriming.Primed);
        rig.Generator.Requests.Skip(1).Take(3)
            .Should().AllSatisfy(request => request.Continuation.Should().NotBeNull());
        rig.Engine.PrimedSegmentCount.Should().BeGreaterThanOrEqualTo(3);
        rig.Engine.FreshSegmentCount.Should().Be(0);
    }

    [Fact]
    public async Task fresh_asks_for_every_segment_without_the_music_so_far()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Fresh);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 4);

        //Assert
        rig.Generator.Requests.Take(4)
            .Should().AllSatisfy(request => request.Continuation.Should().BeNull());
        rig.Engine.FreshSegmentCount.Should().BeGreaterThanOrEqualTo(3);
        rig.Engine.PrimedSegmentCount.Should().Be(0);
    }

    [Fact]
    public async Task alternate_takes_turns_starting_with_a_primed_segment()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Alternate);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 5);

        //Assert - the first piece, then primed, fresh, primed, fresh
        rig.Generator.Requests.Take(5).Select(request => request.Continuation != null)
            .Should().Equal(false, true, false, true, false);
    }

    [Fact]
    public async Task a_generator_that_cannot_be_shown_the_music_so_far_starts_every_segment_fresh()
    {
        //Arrange - primed asked for, but the generator does not honour a continuation
        using var rig = Build(honours: MusicRequestFeatures.Seed);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 3);

        //Assert
        rig.Generator.Requests.Should().AllSatisfy(request => request.Continuation.Should().BeNull());
        rig.Engine.PrimedSegmentCount.Should().Be(0);
        rig.Engine.FreshSegmentCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task a_fresh_segment_still_gets_a_new_seed_derived_from_the_pieces_own()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Fresh, request: new MusicRequest { Seed = 20260923 });
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Generator.PassCount >= 3);

        //Assert
        var seeds = rig.Generator.Requests.Take(3).Select(request => request.Seed).ToArray();
        seeds.Should().Equal(20260923, SegmentSeed.Derive(20260923, 2), SegmentSeed.Derive(20260923, 3));
    }

    [Fact]
    public async Task without_a_crossfade_a_fresh_seam_is_a_hard_join_at_the_bar_line()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Fresh);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 4);

        //Assert - the same grid a primed stream has: every seam on a bar line, no gap
        rig.Engine.SegmentStartTicks.Take(4).Should().Equal(0L, PieceTicks, 2L * PieceTicks, 3L * PieceTicks);
        NoteTicks(rig.Stream).Take(20).Should().Equal(Enumerable.Range(0, 20).Select(i => i * (long)Resolution));
        rig.Engine.CrossfadeCount.Should().Be(0);
    }

    [Fact]
    public async Task the_kind_of_segment_being_generated_is_reported_as_it_changes()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Alternate);
        rig.Host.FollowTheMusic();

        //Act
        var beforeTheStart = rig.Engine.GeneratingSegmentKind;
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.SegmentCount == 2);
        var second = rig.Engine.GeneratingSegmentKind;
        await rig.RunUntil(() => rig.Engine.SegmentCount == 3);
        var third = rig.Engine.GeneratingSegmentKind;

        //Assert
        beforeTheStart.Should().Be(MusicSegmentKind.FirstPiece);
        second.Should().Be(MusicSegmentKind.Primed);
        third.Should().Be(MusicSegmentKind.Fresh);
    }

    [Fact]
    public async Task a_follow_up_starts_the_alternation_again()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Alternate);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Generator.PassCount >= 2);

        //Act - a follow-up on the same generator, and the segment after it
        rig.Engine.FollowUp(rig.Generator, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => !rig.Engine.HasPendingFollowUp);
        var followUpPass = rig.Generator.PassCount;
        await rig.RunUntil(() => rig.Generator.PassCount >= followUpPass + 1);

        //Assert - the first segment after the follow-up is primed
        rig.Generator.Requests[followUpPass].Continuation.Should().NotBeNull();
    }

    // --- CROSSFADE ----------------------------------------------------------------------------

    [Fact]
    public async Task a_fresh_segment_is_placed_a_whole_fade_before_the_bar_line_the_outgoing_piece_ends_at()
    {
        //Arrange - the head stands still, so nothing is hurried and the whole fade is available
        using var rig = Build(priming: SegmentPriming.Fresh, crossfade: Fade);
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);

        //Assert
        rig.Engine.SegmentStartTicks[1].Should().Be(PieceTicks - FadeTicks);
        rig.Engine.ShortenedCrossfadeCount.Should().Be(0);
        rig.Engine.HasPendingFreshSegment.Should().BeFalse();
    }

    [Fact]
    public async Task each_fresh_segment_keeps_its_own_bar_lines_from_where_it_really_starts()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Fresh, crossfade: Fade);
        rig.Engine.Start();

        //Act - one crossfade is handed to the mixer at a time, so the head has to reach the first
        //one's switch before the second is placed
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);
        rig.Host.HeadPosition = TimeSpan.FromSeconds(3.0);
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 2);

        //Assert - each piece is two of ITS OWN bars long, and the next one starts a fade before its end
        rig.Engine.SegmentStartTicks.Take(3).Should().Equal(0L, PieceTicks - FadeTicks,
            (2L * PieceTicks) - (2L * FadeTicks));
    }

    [Fact]
    public async Task the_outgoing_pieces_last_moments_leave_the_timeline_and_a_cue_marks_the_switch()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Fresh, crossfade: Fade, keepRecord: true);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);
        var firstVoicer = rig.Engine.Voicer;

        //Act - let the head move on, so the commit passes the switch
        rig.Host.HeadPosition = TimeSpan.FromSeconds(6.0);
        await rig.RunUntil(() => rig.Engine.WrittenCues.Count >= 1);

        //Assert
        var start = PieceTicks - FadeTicks;
        var cue = rig.Engine.WrittenCues[0];
        cue.Key.Should().Be(start - 1L);
        Recording(rig.Stream).Should().Contain(midiEvent => SeamCrossfadePlan.IsCue(midiEvent, cue.Key, cue.Value));

        // The notes on the timeline carry on every beat: the outgoing piece up to the switch, the
        // incoming piece from it. The outgoing piece's last two beats are not on it at all.
        NoteTicks(rig.Stream).Take(10).Should().Equal(Enumerable.Range(0, 10).Select(i => i * (long)Resolution));
        rig.Engine.CrossfadedTailEvents.OfType<NoteOnEvent>().Select(note => note.AbsoluteTime)
            .Should().Equal(start, start + Resolution);

        // From the cue on, the music is voiced through the incoming piece's own instruments.
        rig.Engine.Voicer.Should().NotBeSameAs(firstVoicer);
    }

    [Fact]
    public async Task the_incoming_piece_is_handed_the_metre_and_the_programs_a_hard_join_would_have_kept()
    {
        //Arrange - a piece that states no metre, and changes its instrument part-way through, so
        //the incoming piece says nothing at its start that the engine did not say for it
        using var rig = Build(ProgramChangedPartWay(), priming: SegmentPriming.Fresh, crossfade: Fade);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);
        var start = rig.Engine.SegmentStartTicks[1];

        //Act
        rig.Host.HeadPosition = TimeSpan.FromSeconds(4.0);
        await rig.RunUntil(() => NoteTicks(rig.Stream).Contains(start));

        //Assert - NOTHING DEDUPLICATED at a crossfaded seam: the new instruments remember nothing
        var atTheStart = Committed(rig.Stream).Where(midiEvent => midiEvent.AbsoluteTime == start).ToArray();
        start.Should().Be(PieceTicks - FadeTicks);
        atTheStart.OfType<TimeSignatureEvent>().Should().ContainSingle()
            .Which.Numerator.Should().Be(4);
        atTheStart.OfType<PatchChangeEvent>().Should().ContainSingle()
            .Which.Patch.Should().Be((int)GeneralMidiProgram.Celesta);
    }

    [Fact]
    public async Task a_primed_seam_is_never_crossfaded()
    {
        //Arrange
        using var rig = Build(crossfade: Fade);
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 3);

        //Assert
        rig.Engine.SegmentStartTicks.Take(3).Should().Equal(0L, PieceTicks, 2L * PieceTicks);
        rig.Engine.CrossfadeCount.Should().Be(0);
        rig.Engine.HasPendingFreshSegment.Should().BeFalse();
    }

    [Fact]
    public async Task alternating_crossfades_only_the_fresh_seams()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Alternate, crossfade: Fade);
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1 && rig.Engine.SegmentCount >= 3);

        //Assert - segment two is primed and joins at the bar line; segment three is fresh and overlaps
        rig.Engine.SegmentStartTicks.Take(3).Should().Equal(0L, PieceTicks, (2L * PieceTicks) - FadeTicks);
    }

    [Fact]
    public async Task with_no_music_ready_in_time_the_fade_is_shortened_to_a_hard_join_rather_than_waiting()
    {
        //Arrange - the head sits at the end of what has been written, so the outgoing music is
        //always due at once, and the generator writes slowly
        using var rig = Build(priming: SegmentPriming.Fresh, crossfade: Fade, pacingRate: 1.0);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 2 && !rig.Engine.HasPendingFreshSegment);

        //Assert
        rig.Engine.SegmentStartTicks[1].Should().Be(PieceTicks);
        rig.Engine.ShortenedCrossfadeCount.Should().Be(1);
        rig.Engine.CrossfadeCount.Should().Be(0);
    }

    [Fact]
    public async Task a_fade_shortened_to_nothing_keeps_every_note_of_the_outgoing_piece()
    {
        //Arrange - the head sits at the end of what has been written and the generator is slow,
        //so the fade is cut all the way to a hard join
        using var rig = Build(priming: SegmentPriming.Fresh, crossfade: Fade, pacingRate: 1.0,
            keepRecord: true);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => NoteTicks(rig.Stream).Count() >= 12);

        //Assert - every one of the outgoing piece's eight notes is on the timeline, then the
        //incoming piece at the bar line, exactly as a hard join always was
        rig.Engine.ShortenedCrossfadeCount.Should().Be(1);
        rig.Engine.CrossfadeCount.Should().Be(0);
        rig.Engine.CrossfadedTailEvents.Should().BeEmpty();
        rig.Engine.WrittenCues.Should().BeEmpty();
        NoteTicks(rig.Stream).Take(12).Should().Equal(Enumerable.Range(0, 12).Select(i => i * (long)Resolution));
        Recording(rig.Stream).Where(MidiEvent.IsNoteOff).Select(off => off.AbsoluteTime).Take(8)
            .Should().Equal(Enumerable.Range(1, 8).Select(i => i * (long)Resolution));
    }

    [Fact]
    public async Task a_pass_that_ends_fifteen_seconds_ahead_of_the_head_still_gets_its_whole_fade()
    {
        //Arrange - THE FIRST FRESH SEAM AS A MODEL MEETS IT: the default five-second pre-roll (so
        //a ten-second commit window), a head running in real time, and a generator at two and a
        //half times real time - nine seconds of music in under four. Eleven bars of 120 bpm is 22
        //seconds; the outgoing pass ends with about 15 seconds of it still ahead of the head.
        using var rig = Build(TestMusic.Bars(11, Resolution), priming: SegmentPriming.Fresh,
            crossfade: TimeSpan.FromSeconds(4.0), pacingRate: 2.5, preroll: TimeSpan.FromSeconds(5.0),
            generateAhead: TimeSpan.FromSeconds(30.0));
        rig.Host.RunTheHead(rig.Time, rig.Engine.Stream.Preroll);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.HasPendingFreshSegment);
        var leadWhenThePassEnded = TimeSpan.FromSeconds(22.0) - rig.Host.Position;

        //Act
        await rig.RunUntil(() => !rig.Engine.HasPendingFreshSegment);

        //Assert - a whole four-second fade, placed eight beats before the bar line at 22 s
        leadWhenThePassEnded.Should().BeGreaterThan(TimeSpan.FromSeconds(14.0));
        leadWhenThePassEnded.Should().BeLessThan(TimeSpan.FromSeconds(17.0));
        rig.Engine.CrossfadeCount.Should().Be(1);
        rig.Engine.ShortenedCrossfadeCount.Should().Be(0);
        rig.Engine.SegmentStartTicks[1].Should().Be((11L * TicksPerBar) - (8L * Resolution));
        rig.Engine.StarvationGapCount.Should().Be(0);
    }

    // --- SILENT OPENING BARS ------------------------------------------------------------------
    //
    // The piece below is six bars: two silent ones, then four bars of quarter notes. Its seam is
    // at 11520 ticks; a one-second fade starts at 10560.

    [Fact]
    public async Task under_a_crossfade_a_fresh_piece_starts_sounding_at_the_start_of_the_fade()
    {
        //Arrange
        using var rig = Build(TwoSilentBarsThenMusic(), priming: SegmentPriming.Fresh, crossfade: Fade);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);
        var start = rig.Engine.SegmentStartTicks[1];

        //Act
        rig.Host.HeadPosition = TimeSpan.FromSeconds(9.5);
        await rig.RunUntil(() => NoteTicks(rig.Stream).Any(tick => tick >= start));

        //Assert - the silent bars were skipped; the state they carried is still at the start
        start.Should().Be(SilentPieceTicks - FadeTicks);
        rig.Engine.SkippedLeadingBarCount.Should().Be(2);
        NoteTicks(rig.Stream).First(tick => tick >= start).Should().Be(start);
        Committed(rig.Stream).OfType<PatchChangeEvent>()
            .Should().Contain(patch => patch.AbsoluteTime == start && patch.Patch == (int)GeneralMidiProgram.Celesta);
        Committed(rig.Stream).OfType<TempoEvent>().Should().Contain(tempo => tempo.AbsoluteTime == start);
    }

    [Fact]
    public async Task under_a_hard_join_a_fresh_pieces_silent_bars_are_played_as_they_always_were()
    {
        //Arrange - no crossfade asked for
        using var rig = Build(TwoSilentBarsThenMusic(), priming: SegmentPriming.Fresh);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => NoteTicks(rig.Stream).Any(tick => tick >= SilentPieceTicks));

        //Assert - joined at the bar line, and its first note two bars after it
        rig.Engine.SegmentStartTicks[1].Should().Be(SilentPieceTicks);
        NoteTicks(rig.Stream).First(tick => tick >= SilentPieceTicks)
            .Should().Be(SilentPieceTicks + (2L * TicksPerBar));
        rig.Engine.SkippedLeadingBarCount.Should().Be(0);
    }

    [Fact]
    public async Task a_crossfade_shortened_to_a_hard_join_skips_nothing_either()
    {
        //Arrange - the head at the end of what is written and a slow generator: a hard join
        using var rig = Build(TwoSilentBarsThenMusic(), priming: SegmentPriming.Fresh, crossfade: Fade,
            pacingRate: 1.0);
        rig.Host.FollowTheMusic();
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => NoteTicks(rig.Stream).Any(tick => tick >= SilentPieceTicks));

        //Assert
        rig.Engine.ShortenedCrossfadeCount.Should().Be(1);
        rig.Engine.SegmentStartTicks[1].Should().Be(SilentPieceTicks);
        NoteTicks(rig.Stream).First(tick => tick >= SilentPieceTicks)
            .Should().Be(SilentPieceTicks + (2L * TicksPerBar));
        rig.Engine.SkippedLeadingBarCount.Should().Be(0);
    }

    [Fact]
    public async Task PrepareCrossfades_waits_for_the_music_then_warms_up_in_the_background()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Fresh, crossfade: Fade);
        rig.Host.RunTheHead(rig.Time, rig.Engine.Stream.Preroll);

        //Act
        rig.Engine.PrepareCrossfades();
        var startedBeforeTheMusic = rig.Engine.CrossfadePreparationStarted;
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.CrossfadePreparationStarted);
        await rig.Engine.CrossfadePreparation.WaitAsync(TimeSpan.FromSeconds(30.0),
            TestContext.Current.CancellationToken);

        //Assert - NOT on the way to the first sound, NOT on the thread pool the generator is
        //pulled from, and below normal priority
        startedBeforeTheMusic.Should().BeFalse();
        rig.Engine.WrittenThroughTick.Should().BeGreaterThanOrEqualTo(0L);
        rig.Engine.Mixer.IsWarmedUp.Should().BeTrue();
        rig.Engine.Mixer.WarmedUpOnThreadPool.Should().BeFalse();
        rig.Engine.Mixer.WarmUpPriority.Should().Be(System.Threading.ThreadPriority.BelowNormal);
        rig.Engine.HasSpareVoicer.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareCrossfades_does_nothing_when_no_crossfade_is_asked_for()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Fresh);

        //Act
        rig.Engine.PrepareCrossfades();
        await rig.Engine.CrossfadePreparation.WaitAsync(TimeSpan.FromSeconds(30.0),
            TestContext.Current.CancellationToken);

        //Assert
        rig.Engine.Mixer.IsWarmedUp.Should().BeFalse();
        rig.Engine.HasSpareVoicer.Should().BeFalse();
        rig.Engine.CrossfadePreparationStarted.Should().BeFalse();
    }

    [Fact]
    public async Task a_running_head_hears_every_fade_with_no_gap_and_no_late_event()
    {
        //Arrange
        using var rig = Build(priming: SegmentPriming.Fresh, crossfade: Fade);
        rig.Host.RunTheHead(rig.Time, rig.Engine.Stream.Preroll);
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 3 && rig.Host.Position > TimeSpan.FromSeconds(12.0));

        //Assert
        rig.Engine.StarvationGapCount.Should().Be(0);
        rig.Stream.LateEventCount.Should().Be(0);
        rig.Engine.ShortenedCrossfadeCount.Should().Be(0);
        rig.Engine.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task without_a_mixer_to_play_through_a_fresh_seam_is_an_ordinary_hard_join()
    {
        //Arrange - a crossfade asked for, but nothing that could play one
        using var rig = Build(priming: SegmentPriming.Fresh, crossfade: Fade, withMixer: false);
        rig.Engine.Start();

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 3);

        //Assert
        rig.Engine.SegmentStartTicks.Take(3).Should().Equal(0L, PieceTicks, 2L * PieceTicks);
        rig.Engine.CrossfadeCount.Should().Be(0);
        rig.Engine.ShortenedCrossfadeCount.Should().Be(0);
    }

    [Fact]
    public async Task a_follow_up_drops_a_crossfade_that_has_not_reached_the_timeline()
    {
        //Arrange - a crossfade placed at the end of a four-bar piece, its cue two bars beyond the
        //bar line a follow-up can take over at
        using var rig = Build(TestMusic.Bars(4, Resolution), priming: SegmentPriming.Fresh, crossfade: Fade,
            keepRecord: true);
        rig.Engine.Start();
        await rig.RunUntil(() => rig.Engine.CrossfadeCount >= 1);
        var firstVoicer = rig.Engine.Voicer;

        //Act
        rig.Engine.FollowUp(rig.Generator, new MusicRequest { TicksPerQuarterNote = Resolution });
        await rig.RunUntil(() => !rig.Engine.HasPendingFollowUp);
        rig.Host.HeadPosition = TimeSpan.FromSeconds(5.0);
        await rig.Settle(40);

        //Assert - no cue ever reached the timeline, and the music is still voiced as it was
        rig.Engine.WrittenCues.Should().BeEmpty();
        rig.Engine.Voicer.Should().BeSameAs(firstVoicer);
        rig.Engine.GenerationError.Should().BeNull();
    }

    // --- the rig ------------------------------------------------------------------------------

    private const long SilentPieceTicks = 6L * TicksPerBar;

    private static MidiEventCollection TwoSilentBarsThenMusic()
    {
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();
        music.AddEvent(new TempoEvent(500000, 0L), 0);
        music.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);
        music.AddEvent(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.Celesta), 0);

        for (var beat = 8; beat < 24; beat++)
        {
            var note = new NoteOnEvent(beat * (long)Resolution, 1, 60 + (beat % 12), 100, Resolution);
            music.AddEvent(note, 0);
            music.AddEvent(note.OffEvent, 0);
        }

        return music;
    }

    private static MidiEventCollection ProgramChangedPartWay()
    {
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();
        music.AddEvent(new TempoEvent(500000, 0L), 0);
        music.AddEvent(new PatchChangeEvent(1000L, 1, (int)GeneralMidiProgram.Celesta), 0);

        for (var beat = 0; beat < 8; beat++)
        {
            var note = new NoteOnEvent(beat * (long)Resolution, 1, 60 + beat, 100, Resolution);
            music.AddEvent(note, 0);
            music.AddEvent(note.OffEvent, 0);
        }

        return music;
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
        Recording(stream).Where(midiEvent => !MidiEvent.IsNoteOff(midiEvent)).ToArray();

    private static IEnumerable<long> NoteTicks(MidiStream stream) =>
        Committed(stream).OfType<NoteOnEvent>().Select(note => note.AbsoluteTime);

    private static SeamRig Build(MidiEventCollection music = null,
        SegmentPriming priming = SegmentPriming.Primed, TimeSpan? crossfade = null,
        bool withMixer = true, MusicRequestFeatures honours = RecordingMusicGenerator.Everything,
        MusicRequest request = null, double pacingRate = 8.0, bool keepRecord = false,
        TimeSpan? preroll = null, TimeSpan? generateAhead = null)
    {
        var time = new ManualTimeProvider();
        var generator = new RecordingMusicGenerator("Seams",
            ReplayOnTestClock.On(ReplayMusicGenerator.FromMidiEvents("Seams",
                music ?? TestMusic.Bars(2, Resolution)), time, pacingRate), honours);
        var stream = new MidiStream(Resolution);
        var host = new FakeMusicHost(SampleRate);
        var library = TestInstrumentLibrary.Complete("SeamParts");
        var rendition = MusicRenditionRegistry.Resolve(BuiltInRenditions.Automatic).Clone();
        var voicer = new RenditionVoicer(rendition, library, SampleRate, null, 1.0F);
        var mixer = withMixer ? new SeamCrossfadeMixer(voicer) : null;

        host.Load(stream, _ => mixer ?? (IMidiSynthesizer)voicer.Router,
            (synthesizer, channel, command, data1, data2) =>
                synthesizer.ProcessMidiMessage(channel, command, data1, data2));

        var with = request == null ? new MusicRequest() : request.Clone();
        with.TicksPerQuarterNote = Resolution;

        var engine = new MusicEngine(generator, with, stream, voicer, host,
            preroll ?? TimeSpan.FromSeconds(1.0), time, 0L)
        {
            EndOfPiece = EndOfPiecePolicy.KeepGenerating,
            GenerateAhead = generateAhead ?? TimeSpan.FromSeconds(12.0),
            SegmentPriming = priming,
            SeamCrossfade = crossfade ?? TimeSpan.Zero,
            SeamCrossfadeCurve = MusicFadeCurve.EqualPower,
            Mixer = mixer,
            CreateVoicer = () => new RenditionVoicer(rendition, library, SampleRate, null, 1.0F),
            KeepsCrossfadeRecord = keepRecord
        };

        host.EndOfTheMusic = () => engine.WrittenThroughTime;

        return new SeamRig(time, stream, host, engine, generator);
    }

    private sealed class SeamRig : IDisposable
    {
        public SeamRig(ManualTimeProvider time, MidiStream stream, FakeMusicHost host,
            MusicEngine engine, RecordingMusicGenerator generator)
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

        public RecordingMusicGenerator Generator { get; }

        public Task RunUntil(Func<bool> until) => EnginePump.RunUntilAsync(Time, until);

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
