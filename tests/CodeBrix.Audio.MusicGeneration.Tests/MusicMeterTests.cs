using System;
using CodeBrix.Audio.MusicGeneration.Generation;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>The metre, and the bar length everything about segment boundaries rests on.</summary>
public class MusicMeterTests
{
    [Theory]
    [InlineData(4, 4, 480, 1920)]
    [InlineData(3, 4, 480, 1440)]
    [InlineData(6, 8, 480, 1440)]
    [InlineData(2, 2, 480, 1920)]
    [InlineData(4, 4, 960, 3840)]
    [InlineData(7, 8, 96, 336)]
    public void TicksPerBar_is_the_beats_times_the_beat_length(int beats, int noteValue,
        int ticksPerQuarterNote, long expected)
        => new MusicMeter(beats, noteValue).TicksPerBar(ticksPerQuarterNote).Should().Be(expected);

    [Fact]
    public void CommonTime_is_four_four()
    {
        //Act
        var meter = MusicMeter.CommonTime;

        //Assert
        meter.BeatsPerBar.Should().Be(4);
        meter.BeatNoteValue.Should().Be(4);
    }

    [Fact]
    public void ToString_writes_it_the_way_it_is_read()
        => new MusicMeter(6, 8).ToString().Should().Be("6/8");

    [Fact]
    public void two_metres_written_the_same_way_are_equal()
        => new MusicMeter(3, 4).Equals(new MusicMeter(3, 4)).Should().BeTrue();

    [Fact]
    public void metres_written_differently_are_not_equal()
        => (new MusicMeter(3, 4) == new MusicMeter(6, 8)).Should().BeFalse();

    [Theory]
    [InlineData(0, 4)]
    [InlineData(-1, 4)]
    public void a_bar_must_have_at_least_one_beat(int beats, int noteValue)
    {
        //Arrange
        Action act = () => new MusicMeter(beats, noteValue);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(4, 3)]
    [InlineData(4, 6)]
    public void the_beat_note_value_must_be_a_power_of_two(int beats, int noteValue)
    {
        //Arrange
        Action act = () => new MusicMeter(beats, noteValue);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TicksPerBar_rejects_a_resolution_that_is_not_positive()
    {
        //Arrange
        var meter = MusicMeter.CommonTime;
        Action act = () => meter.TicksPerBar(0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_note_value_is_written_as_a_fraction_of_a_whole_note()
        => new MusicNoteLength(1, 8).ToString().Should().Be("1/8");

    [Theory]
    [InlineData(1, 4, 480, 480)]
    [InlineData(1, 8, 480, 240)]
    [InlineData(3, 8, 480, 720)]
    public void a_note_value_measures_in_ticks(int numerator, int denominator,
        int ticksPerQuarterNote, long expected)
        => new MusicNoteLength(numerator, denominator).Ticks(ticksPerQuarterNote)
            .Should().Be(expected);
}
