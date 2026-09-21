using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Streaming;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The first stage of the engine: events arrive as the generator writes them, possibly out of tick
/// order, and leave in tick order and never before the generator has settled their tick.
/// </summary>
public class ReorderBufferTests
{
    private const int Resolution = 480;

    [Fact]
    public void nothing_is_released_before_the_generator_has_settled_anything()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);

        //Act
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(0L, 1, 60),
            GeneratedMusicEvent.NothingSettled));

        //Assert
        buffer.TakeReleased().Should().BeEmpty();
        buffer.HasSettled.Should().BeFalse();
        buffer.HeldCount.Should().Be(1);
    }

    [Fact]
    public void an_event_is_held_until_the_settled_tick_reaches_its_own_tick()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(960L, 1, 60),
            GeneratedMusicEvent.NothingSettled));

        //Act
        buffer.Add(GeneratedMusicEvent.SettledThrough(480L));
        var early = buffer.TakeReleased();
        buffer.Add(GeneratedMusicEvent.SettledThrough(960L));
        var released = buffer.TakeReleased();

        //Assert
        early.Should().BeEmpty();
        released.Should().HaveCount(1);
        released[0].AbsoluteTime.Should().Be(960L);
    }

    [Fact]
    public void events_written_out_of_tick_order_come_out_in_tick_order()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);

        //Act
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(960L, 1, 67),
            GeneratedMusicEvent.NothingSettled));
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(0L, 2, 48),
            GeneratedMusicEvent.NothingSettled));
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(480L, 1, 62), 960L));

        //Assert
        buffer.TakeReleased().Select(midiEvent => midiEvent.AbsoluteTime)
            .Should().Equal(0L, 480L, 960L);
    }

    [Fact]
    public void events_at_one_tick_come_out_meta_first_then_controls_then_notes()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);

        //Act
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(0L, 1, 60),
            GeneratedMusicEvent.NothingSettled));
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(new PatchChangeEvent(0L, 1, 8),
            GeneratedMusicEvent.NothingSettled));
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(new TempoEvent(500000, 0L), 0L));

        //Assert
        buffer.TakeReleased().Select(midiEvent => midiEvent.GetType().Name)
            .Should().Equal(nameof(TempoEvent), nameof(PatchChangeEvent), nameof(NoteOnEvent));
    }

    [Fact]
    public void two_notes_at_one_tick_keep_the_order_the_generator_wrote_them_in()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);

        //Act
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(0L, 1, 60),
            GeneratedMusicEvent.NothingSettled));
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(0L, 1, 64),
            GeneratedMusicEvent.NothingSettled));
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(0L, 1, 67), 0L));

        //Assert
        buffer.TakeReleased().OfType<NoteOnEvent>().Select(note => note.NoteNumber)
            .Should().Equal(60, 64, 67);
    }

    [Fact]
    public void a_horizon_only_update_settles_the_music_without_adding_anything()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);

        //Act
        buffer.Add(GeneratedMusicEvent.SettledThrough(7680L));

        //Assert
        buffer.SettledThroughTick.Should().Be(7680L);
        buffer.HasSettled.Should().BeTrue();
        buffer.TakeReleased().Should().BeEmpty();
    }

    [Fact]
    public void the_settled_tick_never_goes_backwards()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);
        buffer.Add(GeneratedMusicEvent.SettledThrough(960L));

        //Act
        buffer.Add(GeneratedMusicEvent.SettledThrough(480L));

        //Assert
        buffer.SettledThroughTick.Should().Be(960L);
    }

    [Fact]
    public void a_segment_is_placed_where_the_engine_puts_it()
    {
        //Arrange
        var buffer = new ReorderBuffer(7680L);

        //Act
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(0L, 1, 60), 0L));
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(480L, 1, 62), 480L));

        //Assert
        buffer.SegmentStartTick.Should().Be(7680L);
        buffer.SettledThroughTick.Should().Be(8160L);
        buffer.TakeReleased().Select(midiEvent => midiEvent.AbsoluteTime)
            .Should().Equal(7680L, 8160L);
    }

    [Fact]
    public void placing_a_note_keeps_its_length_rather_than_stretching_it()
    {
        //Arrange
        var buffer = new ReorderBuffer(7680L);

        //Act
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(0L, 1, 60), 0L));
        var placed = buffer.TakeReleased().OfType<NoteOnEvent>().Single();

        //Assert
        placed.AbsoluteTime.Should().Be(7680L);
        placed.NoteLength.Should().Be(Resolution);
        placed.OffEvent.AbsoluteTime.Should().Be(7680L + Resolution);
    }

    [Fact]
    public void the_generators_own_event_is_never_the_one_that_is_released()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);
        var original = new PatchChangeEvent(0L, 3, 40);

        //Act
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(original, 0L));
        var released = buffer.TakeReleased().Single();

        //Assert
        released.Should().NotBeSameAs(original);
        ((PatchChangeEvent)released).Patch.Should().Be(40);
    }

    [Fact]
    public void what_is_still_held_can_be_thrown_away()
    {
        //Arrange
        var buffer = new ReorderBuffer(0L);
        buffer.Add(GeneratedMusicEvent.FromMidiEvent(Note(9600L, 1, 60),
            GeneratedMusicEvent.NothingSettled));

        //Act
        buffer.DiscardHeld();
        buffer.Add(GeneratedMusicEvent.SettledThrough(9600L));

        //Assert
        buffer.HeldCount.Should().Be(0);
        buffer.TakeReleased().Should().BeEmpty();
    }

    private static NoteOnEvent Note(long tick, int channel, int noteNumber) =>
        new NoteOnEvent(tick, channel, noteNumber, 100, Resolution);
}
