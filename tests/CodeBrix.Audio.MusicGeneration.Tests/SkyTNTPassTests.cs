using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;
using ModelMidiEvent = CodeBrix.Ollama.ModelRunner.MidiEvent;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A pass's bar arithmetic, its settled ticks and its prompt, on events built by hand - no model
/// needed.
/// </summary>
public class SkyTNTPassTests
{
    private const int ModelResolution = 480;
    private const int Resolution = 480;
    private const long TicksPerBar = 4L * Resolution;

    [Fact]
    public void nothing_is_let_out_until_a_whole_bar_has_settled()
    {
        //Arrange
        var pass = SkyTNTPass.For(new MusicRequest(), new SkyTNTGeneratorOptions(), ModelResolution);

        //Act - three beats of a four-beat bar
        var produced = new List<GeneratedMusicEvent>();

        for (var beat = 0; beat < 3; beat++)
        {
            produced.AddRange(pass.Accept(Note(beat * 480L)));
        }

        //Assert
        produced.Should().BeEmpty();
    }

    [Fact]
    public void a_bar_is_let_out_once_the_horizon_has_passed_its_end()
    {
        //Arrange
        var pass = SkyTNTPass.For(new MusicRequest(), new SkyTNTGeneratorOptions(), ModelResolution);
        var produced = new List<GeneratedMusicEvent>();

        for (var beat = 0; beat < 4; beat++)
        {
            produced.AddRange(pass.Accept(Note(beat * 480L)));
        }

        //Act - the first event of the next bar is what says the first bar is finished
        produced.AddRange(pass.Accept(Note(TicksPerBar)));

        //Assert - the four notes of the first bar, and the settled tick one short of the bar line
        produced.Count.Should().Be(4);
        produced.Select(item => item.Event.AbsoluteTime).Should().Equal(0L, 480L, 960L, 1440L);
        produced[3].SettledThroughTick.Should().Be(TicksPerBar - 1L);
    }

    [Fact]
    public void only_the_last_item_of_a_group_says_the_tick_has_settled()
    {
        //Arrange
        var pass = SkyTNTPass.For(new MusicRequest(), new SkyTNTGeneratorOptions(), ModelResolution);
        var produced = new List<GeneratedMusicEvent>();

        for (var beat = 0; beat < 4; beat++)
        {
            produced.AddRange(pass.Accept(Note(beat * 480L)));
        }

        //Act
        produced.AddRange(pass.Accept(Note(TicksPerBar)));

        //Assert
        produced.Take(3).Should().OnlyContain(item =>
            item.SettledThroughTick == GeneratedMusicEvent.NothingSettled);
    }

    [Fact]
    public void the_ragged_last_bar_is_held_back_and_the_pass_ends_on_the_bar_line()
    {
        //Arrange - two whole bars and a beat
        var pass = SkyTNTPass.For(new MusicRequest(), new SkyTNTGeneratorOptions(), ModelResolution);
        var produced = new List<GeneratedMusicEvent>();

        for (var beat = 0; beat < 9; beat++)
        {
            produced.AddRange(pass.Accept(Note(beat * 480L)));
        }

        //Act
        produced.AddRange(pass.Finish());

        //Assert - the ninth note is inside the third bar and is not played
        var events = produced.Where(item => item.HasEvent).ToArray();
        events.Length.Should().Be(8);
        pass.DroppedEventCount.Should().Be(1);
        produced[produced.Count - 1].HasEvent.Should().BeFalse();
        produced[produced.Count - 1].SettledThroughTick.Should().Be(2L * TicksPerBar);
    }

    [Fact]
    public void a_pass_shorter_than_one_bar_is_rounded_up_to_one_rather_than_yielding_nothing()
    {
        //Arrange
        var pass = SkyTNTPass.For(new MusicRequest(), new SkyTNTGeneratorOptions(), ModelResolution);
        var produced = new List<GeneratedMusicEvent>(pass.Accept(Note(0L)));

        //Act
        produced.AddRange(pass.Finish());

        //Assert - the music moves forward by a bar; a pass that yielded nothing would end the piece
        produced.Count(item => item.HasEvent).Should().Be(1);
        produced[produced.Count - 1].SettledThroughTick.Should().Be(TicksPerBar);
        pass.DroppedEventCount.Should().Be(0);
    }

    [Fact]
    public void a_pass_that_produced_nothing_at_all_yields_nothing()
    {
        //Arrange
        var pass = SkyTNTPass.For(new MusicRequest(), new SkyTNTGeneratorOptions(), ModelResolution);

        //Act and Assert
        pass.Finish().Should().BeEmpty();
    }

    [Fact]
    public void no_event_ever_arrives_at_or_before_a_tick_already_announced_as_settled()
    {
        //Arrange - events out of tick order within a beat, the way the model writes them
        var pass = SkyTNTPass.For(new MusicRequest(), new SkyTNTGeneratorOptions(), ModelResolution);
        var produced = new List<GeneratedMusicEvent>();

        foreach (var tick in new[] { 0L, 240L, 120L, 480L, 960L, 1440L, 1920L, 2400L })
        {
            produced.AddRange(pass.Accept(Note(tick)));
        }

        produced.AddRange(pass.Finish());

        //Act
        var settled = GeneratedMusicEvent.NothingSettled;
        var broken = false;

        foreach (var item in produced)
        {
            if (item.HasEvent && item.Event.AbsoluteTime <= settled)
            {
                broken = true;
            }

            if (item.HasSettled && item.SettledThroughTick > settled)
            {
                settled = item.SettledThroughTick;
            }
        }

        //Assert
        broken.Should().BeFalse();
    }

    [Fact]
    public void the_metre_the_model_writes_decides_where_the_bar_lines_fall()
    {
        //Arrange - three four, so a bar is three beats
        var pass = SkyTNTPass.For(new MusicRequest(), new SkyTNTGeneratorOptions(), ModelResolution);
        var produced = new List<GeneratedMusicEvent>(
            pass.Accept(ModelMidiEvent.TimeSignature(0L, 0, 3, 4)));

        //Act
        for (var beat = 0; beat < 4; beat++)
        {
            produced.AddRange(pass.Accept(Note(beat * 480L)));
        }

        //Assert - the bar line is at three beats, not four
        produced.Last(item => item.HasSettled).SettledThroughTick.Should().Be((3L * 480L) - 1L);
    }

    [Fact]
    public void a_continuation_writes_its_segment_from_the_end_of_the_tail()
    {
        //Arrange - four bars of tail, whose last note is a beat before the bar line
        var request = Continuing(out var tailTicks);
        var pass = SkyTNTPass.For(request, new SkyTNTGeneratorOptions(), ModelResolution);

        //Act
        var produced = new List<GeneratedMusicEvent>();

        for (var beat = 0; beat < 5; beat++)
        {
            produced.AddRange(pass.Accept(Note(tailTicks + (beat * 480L))));
        }

        //Assert - the first new note is at tick 0 of the segment, not at the tail's own tick
        pass.OriginModelTicks.Should().Be(tailTicks);
        produced.First(item => item.HasEvent).Event.AbsoluteTime.Should().Be(0L);
    }

    [Fact]
    public void an_event_the_model_places_inside_the_tail_is_moved_to_the_seam()
    {
        //Arrange
        var request = Continuing(out var tailTicks);
        var pass = SkyTNTPass.For(request, new SkyTNTGeneratorOptions(), ModelResolution);

        //Act - the model carries on from the last event it was shown, a beat before the tail's end
        var produced = new List<GeneratedMusicEvent>(pass.Accept(Note(tailTicks - 480L)));

        for (var beat = 0; beat < 5; beat++)
        {
            produced.AddRange(pass.Accept(Note(tailTicks + (beat * 480L))));
        }

        //Assert - nothing is lost, and nothing lands before the segment starts
        produced.Where(item => item.HasEvent).Select(item => item.Event.AbsoluteTime)
            .Should().OnlyContain(tick => tick >= 0L);
        produced.Count(item => item.HasEvent).Should().Be(5);
    }

    [Fact]
    public void the_tail_handed_to_the_model_is_cut_to_its_LAST_bars()
    {
        //Arrange - eight bars of tail, but only two are wanted
        var request = Continuing(out var tailTicks, bars: 8);
        var options = new SkyTNTGeneratorOptions { MaximumPromptBars = 2 };

        //Act
        var pass = SkyTNTPass.For(request, options, ModelResolution);

        //Assert - two bars of prompt, and the segment still starts at the tail's own end
        pass.Prompt.Should().NotBeNull();
        pass.OriginModelTicks.Should().Be(2L * TicksPerBar);
        pass.Prompt.Events.Should().OnlyContain(item => item.Tick < 2L * TicksPerBar);
        tailTicks.Should().Be(8L * TicksPerBar);
    }

    [Fact]
    public void CutAt_keeps_the_whole_tail_when_it_is_already_short_enough() =>
        SkyTNTPrompt.CutAt(2L * TicksPerBar, TicksPerBar, 4).Should().Be(0L);

    [Fact]
    public void CutAt_drops_the_earliest_bars_when_the_tail_is_too_long() =>
        SkyTNTPrompt.CutAt(8L * TicksPerBar, TicksPerBar, 3).Should().Be(5L * TicksPerBar);

    private static MusicRequest Continuing(out long tailTicks, int bars = 4)
    {
        tailTicks = bars * TicksPerBar;

        var tail = new MidiEventCollection(1, Resolution);
        var track = tail.AddTrack();

        for (var bar = 0; bar < bars; bar++)
        {
            // A bar of two notes, the last of them a beat before the bar line.
            track.Add(new NoteOnEvent(bar * TicksPerBar, 1, 60, 100, 480));
            track.Add(new NoteOnEvent((bar * TicksPerBar) + (3L * 480L), 1, 64, 100, 480));
        }

        var request = new MusicRequest
        {
            Continuation = new MusicContinuation
            {
                Tail = tail,
                TailTicks = tailTicks,
                Meter = MusicMeter.CommonTime
            }
        };

        return request;
    }

    private static ModelMidiEvent Note(long tick) =>
        ModelMidiEvent.Note(tick, 1, 0, 60, 100, 240L);
}
