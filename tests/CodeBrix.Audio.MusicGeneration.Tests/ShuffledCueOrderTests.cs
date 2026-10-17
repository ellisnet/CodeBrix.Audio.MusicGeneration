using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.MusicGeneration.Internal;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The shuffled bag the default replay draws its cues from: every cue once before any cue again,
/// and never the same cue twice in a row - not even across a reshuffle.
/// </summary>
public class ShuffledCueOrderTests
{
    private const int Cues = 10;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(20261017)]
    public void the_first_ten_draws_are_a_permutation_of_all_ten(int seed)
    {
        //Arrange
        var order = new ShuffledCueOrder(Cues, new Random(seed));

        //Act
        var drawn = Draw(order, Cues);

        //Assert
        drawn.OrderBy(cue => cue).Should().Equal(Enumerable.Range(0, Cues));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(29)]
    [InlineData(11)]
    public void over_thirty_draws_every_cue_appears_exactly_three_times(int seed)
    {
        //Arrange
        var order = new ShuffledCueOrder(Cues, new Random(seed));

        //Act
        var drawn = Draw(order, 3 * Cues);

        //Assert
        drawn.GroupBy(cue => cue).Should().HaveCount(Cues);
        drawn.GroupBy(cue => cue).Should().OnlyContain(group => group.Count() == 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    [InlineData(1234)]
    [InlineData(99999)]
    public void no_cue_is_drawn_twice_in_a_row_across_a_hundred_draws(int seed)
    {
        //Arrange
        var order = new ShuffledCueOrder(Cues, new Random(seed));

        //Act
        var drawn = Draw(order, 100);

        //Assert
        for (var i = 1; i < drawn.Count; i++)
        {
            drawn[i].Should().NotBe(drawn[i - 1]);
        }
    }

    [Fact]
    public void no_cue_is_drawn_twice_in_a_row_across_many_seeds()
    {
        //Arrange - every bag boundary over many seeds, where a repeat could happen
        for (var seed = 0; seed < 500; seed++)
        {
            var order = new ShuffledCueOrder(Cues, new Random(seed));

            //Act
            var drawn = Draw(order, 3 * Cues);

            //Assert
            drawn[Cues].Should().NotBe(drawn[Cues - 1]);
            drawn[2 * Cues].Should().NotBe(drawn[(2 * Cues) - 1]);
        }
    }

    [Fact]
    public void a_seeded_order_is_reproducible()
    {
        //Act
        var first = Draw(new ShuffledCueOrder(Cues, new Random(20260918)), 40);
        var second = Draw(new ShuffledCueOrder(Cues, new Random(20260918)), 40);

        //Assert
        second.Should().Equal(first);
    }

    [Fact]
    public void different_seeds_give_different_orders()
    {
        //Act
        var first = Draw(new ShuffledCueOrder(Cues, new Random(1)), 40);
        var second = Draw(new ShuffledCueOrder(Cues, new Random(2)), 40);

        //Assert
        second.Should().NotEqual(first);
    }

    [Fact]
    public void Peek_says_what_Next_will_draw_and_does_not_draw_it()
    {
        //Arrange
        var order = new ShuffledCueOrder(Cues, new Random(5));
        var reference = new ShuffledCueOrder(Cues, new Random(5));

        //Act and Assert
        for (var i = 0; i < 25; i++)
        {
            var peeked = order.Peek();
            order.Peek().Should().Be(peeked);
            order.Next().Should().Be(peeked);
            reference.Next().Should().Be(peeked);
        }
    }

    [Fact]
    public void a_single_cue_is_drawn_every_time()
        => Draw(new ShuffledCueOrder(1, new Random(3)), 5).Should().OnlyContain(cue => cue == 0);

    [Fact]
    public void the_constructor_refuses_no_cues_and_a_null_random()
    {
        //Arrange
        Action noCues = () => _ = new ShuffledCueOrder(0, new Random(1));
        Action noRandom = () => _ = new ShuffledCueOrder(Cues, null);

        //Assert
        noCues.Should().Throw<ArgumentOutOfRangeException>();
        noRandom.Should().Throw<ArgumentNullException>();
    }

    private static List<int> Draw(ShuffledCueOrder order, int count)
    {
        var drawn = new List<int>(count);

        for (var i = 0; i < count; i++)
        {
            drawn.Add(order.Next());
        }

        return drawn;
    }
}
