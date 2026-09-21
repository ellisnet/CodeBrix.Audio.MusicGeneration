using System;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Streaming;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The second stage of the engine: settled music waits here, in memory, where it can still be
/// thrown away - and where the tempo of what has gone by is remembered, because the streaming
/// rules are all measured in seconds of music.
/// </summary>
public class SettledMusicBufferTests
{
    private const int Resolution = 480;

    [Fact]
    public void music_comes_out_in_the_order_it_went_in()
    {
        //Arrange
        var buffer = new SettledMusicBuffer(Resolution);

        //Act
        buffer.Add(Note(0L));
        buffer.Add(Note(480L));
        buffer.Add(Note(960L));

        //Assert
        buffer.TakeThrough(960L).Select(midiEvent => midiEvent.AbsoluteTime)
            .Should().Equal(0L, 480L, 960L);
    }

    [Fact]
    public void TakeThrough_leaves_behind_what_is_beyond_the_tick_it_was_given()
    {
        //Arrange
        var buffer = new SettledMusicBuffer(Resolution);
        buffer.Add(Note(0L));
        buffer.Add(Note(9600L));

        //Act
        var taken = buffer.TakeThrough(480L);

        //Assert
        taken.Should().HaveCount(1);
        buffer.Count.Should().Be(1);
        buffer.NextTick.Should().Be(9600L);
    }

    [Fact]
    public void at_the_default_tempo_a_quarter_note_lasts_half_a_second()
    {
        //Arrange
        var buffer = new SettledMusicBuffer(Resolution);

        //Act
        var time = buffer.TimeOfTick(Resolution);

        //Assert
        time.Should().Be(TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void a_tempo_change_retimes_everything_after_it()
    {
        //Arrange
        var buffer = new SettledMusicBuffer(Resolution);

        //Act
        buffer.Add(new TempoEvent(250000, 960L));

        //Assert
        buffer.TimeOfTick(960L).Should().Be(TimeSpan.FromSeconds(1.0));
        buffer.TimeOfTick(1920L).Should().Be(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void a_moment_turns_back_into_the_tick_it_came_from()
    {
        //Arrange
        var buffer = new SettledMusicBuffer(Resolution);
        buffer.Add(new TempoEvent(250000, 960L));

        //Act
        var tick = buffer.TickAtTime(TimeSpan.FromSeconds(1.5));

        //Assert
        tick.Should().Be(1920L);
    }

    [Fact]
    public void the_settled_tick_never_goes_backwards()
    {
        //Arrange
        var buffer = new SettledMusicBuffer(Resolution);

        //Act
        buffer.SettleThrough(960L);
        buffer.SettleThrough(480L);

        //Assert
        buffer.SettledThroughTick.Should().Be(960L);
    }

    [Fact]
    public void what_is_not_committed_can_be_thrown_away_and_the_tempo_map_survives_it()
    {
        //Arrange
        var buffer = new SettledMusicBuffer(Resolution);
        buffer.Add(new TempoEvent(250000, 0L));
        buffer.TakeThrough(0L);
        buffer.Add(Note(9600L));

        //Act
        buffer.DiscardUncommitted();

        //Assert
        buffer.IsEmpty.Should().BeTrue();
        buffer.TimeOfTick(Resolution).Should().Be(TimeSpan.FromSeconds(0.25));
    }

    private static NoteOnEvent Note(long tick) => new NoteOnEvent(tick, 1, 60, 100, Resolution);
}
