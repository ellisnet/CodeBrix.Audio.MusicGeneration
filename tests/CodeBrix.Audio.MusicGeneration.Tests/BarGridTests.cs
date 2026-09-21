using System.Collections.Generic;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Where the bar lines fall. A pass that ends on one is a pass the next can start against without
/// a mid-bar join.
/// </summary>
public class BarGridTests
{
    [Theory]
    [InlineData(1L, 1920L)]
    [InlineData(1919L, 1920L)]
    [InlineData(1920L, 1920L)]
    [InlineData(1921L, 3840L)]
    [InlineData(7680L, 7680L)]
    public void common_time_rounds_up_to_a_whole_bar(long tick, long expected)
        => BarGrid.RoundUpToBar(new List<MeterChange>(), 480, tick).Should().Be(expected);

    [Fact]
    public void a_tick_at_the_very_start_stays_where_it_is()
        => BarGrid.RoundUpToBar(new List<MeterChange>(), 480, 0L).Should().Be(0L);

    [Fact]
    public void a_negative_tick_reads_as_the_start()
        => BarGrid.RoundUpToBar(new List<MeterChange>(), 480, -5L).Should().Be(0L);

    [Theory]
    [InlineData(1L, 1440L)]
    [InlineData(1440L, 1440L)]
    [InlineData(1441L, 2880L)]
    public void three_four_rounds_up_to_a_bar_of_three_beats(long tick, long expected)
    {
        //Arrange
        var changes = new List<MeterChange> { new MeterChange(0L, new MusicMeter(3, 4)) };

        //Act
        var rounded = BarGrid.RoundUpToBar(changes, 480, tick);

        //Assert
        rounded.Should().Be(expected);
    }

    [Fact]
    public void a_metre_change_starts_a_bar()
    {
        //Arrange - two bars of 4/4, then 3/4 from tick 3840
        var changes = new List<MeterChange>
        {
            new MeterChange(0L, MusicMeter.CommonTime),
            new MeterChange(3840L, new MusicMeter(3, 4))
        };

        //Act
        var withinTheFirstMetre = BarGrid.RoundUpToBar(changes, 480, 2000L);
        var onTheChange = BarGrid.RoundUpToBar(changes, 480, 3840L);
        var afterTheChange = BarGrid.RoundUpToBar(changes, 480, 3841L);

        //Assert
        withinTheFirstMetre.Should().Be(3840L);
        onTheChange.Should().Be(3840L);
        afterTheChange.Should().Be(5280L);
    }

    [Fact]
    public void a_metre_that_changes_part_way_through_a_bar_rounds_to_the_change()
    {
        //Arrange - the metre changes 1.5 bars into 4/4, so the bar line is the change itself
        var changes = new List<MeterChange>
        {
            new MeterChange(0L, MusicMeter.CommonTime),
            new MeterChange(2880L, new MusicMeter(3, 4))
        };

        //Act
        var rounded = BarGrid.RoundUpToBar(changes, 480, 2000L);

        //Assert
        rounded.Should().Be(2880L);
    }

    [Fact]
    public void a_metre_that_starts_after_the_beginning_leaves_common_time_before_it()
    {
        //Arrange
        var changes = new List<MeterChange> { new MeterChange(1920L, new MusicMeter(6, 8)) };

        //Act
        var beforeTheChange = BarGrid.RoundUpToBar(changes, 480, 100L);
        var afterTheChange = BarGrid.RoundUpToBar(changes, 480, 1921L);

        //Assert
        beforeTheChange.Should().Be(1920L);
        afterTheChange.Should().Be(3360L);
    }

    [Fact]
    public void the_resolution_decides_how_long_a_bar_is()
        => BarGrid.RoundUpToBar(new List<MeterChange>(), 960, 1L).Should().Be(3840L);

    [Theory]
    [InlineData(1L, 0L)]
    [InlineData(1919L, 0L)]
    [InlineData(1920L, 1920L)]
    [InlineData(1921L, 1920L)]
    [InlineData(7681L, 7680L)]
    public void common_time_rounds_down_to_the_bar_line_it_is_in(long tick, long expected)
        => BarGrid.RoundDownToBar(new List<MeterChange>(), 480, tick).Should().Be(expected);

    [Fact]
    public void rounding_down_from_the_very_start_stays_there()
        => BarGrid.RoundDownToBar(new List<MeterChange>(), 480, 0L).Should().Be(0L);

    [Fact]
    public void a_negative_tick_rounds_down_to_the_start()
        => BarGrid.RoundDownToBar(new List<MeterChange>(), 480, -5L).Should().Be(0L);

    [Theory]
    [InlineData(1439L, 0L)]
    [InlineData(1440L, 1440L)]
    [InlineData(2879L, 1440L)]
    public void three_four_rounds_down_to_a_bar_of_three_beats(long tick, long expected)
    {
        //Arrange
        var changes = new List<MeterChange> { new MeterChange(0L, new MusicMeter(3, 4)) };

        //Act
        var rounded = BarGrid.RoundDownToBar(changes, 480, tick);

        //Assert
        rounded.Should().Be(expected);
    }

    [Fact]
    public void rounding_down_counts_bars_from_the_metre_change_rather_than_from_the_start()
    {
        //Arrange - common time to tick 1920, then six-eight bars of 1440 ticks
        var changes = new List<MeterChange> { new MeterChange(1920L, new MusicMeter(6, 8)) };

        //Act
        var beforeTheChange = BarGrid.RoundDownToBar(changes, 480, 1900L);
        var afterTheChange = BarGrid.RoundDownToBar(changes, 480, 3400L);

        //Assert
        beforeTheChange.Should().Be(0L);
        afterTheChange.Should().Be(3360L);
    }

    [Fact]
    public void rounding_up_and_rounding_down_agree_on_a_tick_that_is_already_a_bar_line()
    {
        //Arrange
        var changes = new List<MeterChange> { new MeterChange(0L, new MusicMeter(5, 4)) };

        //Act
        var up = BarGrid.RoundUpToBar(changes, 480, 4800L);
        var down = BarGrid.RoundDownToBar(changes, 480, 4800L);

        //Assert
        up.Should().Be(4800L);
        down.Should().Be(4800L);
    }
}
