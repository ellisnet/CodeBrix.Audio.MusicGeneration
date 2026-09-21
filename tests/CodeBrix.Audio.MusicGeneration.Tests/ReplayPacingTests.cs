using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The pacing: a replay releases its music as though it were being written, and a request can
/// switch that off. Every test here runs on a clock the test moves by hand, so nothing sleeps and
/// nothing depends on how busy the machine is.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class ReplayPacingTests
{
    // One bar of four quarter notes at 120 beats per minute: two seconds of music, an event every
    // half second, and a bar-rounded end at the two-second mark.
    private const int Resolution = 480;

    /// <summary>
    /// Starts every test from freshly built built-ins: one of these tests gives a built-in replay
    /// a clock of its own, and that must not leak into another test.
    /// </summary>
    public ReplayPacingTests() => MusicGeneratorRegistry.ResetForTesting();

    [Fact]
    public async Task at_real_time_every_item_is_released_at_its_own_musical_moment()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = PacedGenerator(time, 1.0);
        var start = time.GetUtcNow();

        //Act
        var released = await GenerationPump.DrainAsync(
            generator.GenerateAsync(Paced(), TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);

        //Assert
        Offsets(released, start).Should().Equal(
            TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(0.0),
            TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.5),
            TimeSpan.FromSeconds(2.0));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(8.0)]
    [InlineData(0.25)]
    public async Task an_item_is_never_released_before_its_musical_time_divided_by_the_rate(
        double rate)
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = PacedGenerator(time, rate);
        var start = time.GetUtcNow();

        //Act
        var released = await GenerationPump.DrainAsync(
            generator.GenerateAsync(Paced(), TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);

        //Assert
        foreach (var item in released)
        {
            var musicalSeconds = TickSecondsOf(item.Item);
            var earliest = TimeSpan.FromSeconds(musicalSeconds / rate);

            (item.At - start).Should().BeGreaterThanOrEqualTo(earliest - TimeSpan.FromTicks(1L));
        }
    }

    [Fact]
    public async Task a_rate_below_one_really_is_slower_than_the_music()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var slow = PacedGenerator(time, 0.25);
        var start = time.GetUtcNow();

        //Act
        var released = await GenerationPump.DrainAsync(
            slow.GenerateAsync(Paced(), TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);

        //Assert
        var wholePass = released[released.Count - 1].At - start;
        wholePass.Should().Be(TimeSpan.FromSeconds(8.0));
    }

    [Fact]
    public async Task the_default_rate_is_comfortably_faster_than_real_time()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = PacedGenerator(time, ReplayMusicGenerator.DefaultPacingRate);
        var start = time.GetUtcNow();

        //Act
        var released = await GenerationPump.DrainAsync(
            generator.GenerateAsync(Paced(), TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);

        //Assert
        ReplayMusicGenerator.DefaultPacingRate.Should().BeGreaterThan(1.0);
        (released[released.Count - 1].At - start).Should().Be(TimeSpan.FromSeconds(0.25));
    }

    [Fact]
    public async Task with_pacing_switched_off_a_whole_pass_arrives_without_the_clock_moving()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = PacedGenerator(time, 1.0);
        var start = time.GetUtcNow();
        var request = new MusicRequest
        {
            TicksPerQuarterNote = Resolution,
            PaceInRealTime = false
        };

        //Act
        var released = await GenerationPump.DrainAsync(
            generator.GenerateAsync(request, TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);

        //Assert
        released.Should().HaveCount(7);
        time.GetUtcNow().Should().Be(start);
        released.Should().OnlyContain(item => item.At == start);
    }

    [Fact]
    public async Task pacing_off_and_pacing_on_produce_the_very_same_music()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = PacedGenerator(time, 1.0);

        //Act
        var paced = await GenerationPump.DrainAsync(
            generator.GenerateAsync(Paced(), TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);
        var unpaced = await GenerationPump.DrainAsync(
            generator.GenerateAsync(
                new MusicRequest { TicksPerQuarterNote = Resolution, PaceInRealTime = false },
                TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);

        //Assert
        Describe(unpaced).Should().Equal(Describe(paced));
    }

    [Fact]
    public async Task cancelling_stops_the_generation()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = PacedGenerator(time, 1.0);
        using var cancellation = new CancellationTokenSource();

        //Act
        var enumerator = generator.GenerateAsync(Paced(), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        await GenerationPump.MoveNextAsync(enumerator, time);
        cancellation.Cancel();

        Func<Task> act = async () =>
        {
            while (await GenerationPump.MoveNextAsync(enumerator, time))
            {
            }
        };

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task a_built_in_replay_paces_its_own_music_over_its_own_length()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var generator = (ReplayMusicGenerator)MusicGeneratorRegistry.Resolve(EmbeddedReplay.AbcSecond);
        generator.TimeProvider = time;
        generator.PacingRate = 4.0;
        var expected = TimeSpan.FromTicks((long)(generator.PassDuration(Resolution).Ticks / 4.0));
        var start = time.GetUtcNow();

        //Act
        var released = await GenerationPump.DrainAsync(
            generator.GenerateAsync(Paced(), TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);

        //Assert
        released.Should().NotBeEmpty();
        expected.Should().BeGreaterThan(TimeSpan.Zero);

        var wholePass = released[released.Count - 1].At - start;
        wholePass.Should().BeGreaterThanOrEqualTo(expected);
        wholePass.Should().BeLessThan(expected + TimeSpan.FromMilliseconds(1.0));
    }

    [Fact]
    public async Task a_tune_in_abc_is_paced_over_the_length_the_notation_asks_for()
    {
        //Arrange - four bars of 4/4 at 120 beats per minute is eight seconds of music
        var time = new ManualTimeProvider();
        var generator = ReplayMusicGenerator.FromAbc("PacedTune", TestMusic.SimpleAbc);
        generator.TimeProvider = time;
        generator.PacingRate = 2.0;
        var start = time.GetUtcNow();

        //Act
        var released = await GenerationPump.DrainAsync(
            generator.GenerateAsync(Paced(), TestContext.Current.CancellationToken), time,
            TestContext.Current.CancellationToken);

        //Assert
        (released[released.Count - 1].At - start).Should().Be(TimeSpan.FromSeconds(4.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void PacingRate_rejects_a_rate_that_is_not_a_positive_multiple_of_real_time(double rate)
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromAbc("Rejects", TestMusic.SimpleAbc);
        Action act = () => generator.PacingRate = rate;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TimeProvider_rejects_null()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromAbc("Rejects", TestMusic.SimpleAbc);
        Action act = () => generator.TimeProvider = null;

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void a_replay_uses_the_system_clock_until_a_test_replaces_it()
        => ReplayMusicGenerator.FromAbc("SystemClock", TestMusic.SimpleAbc).TimeProvider
            .Should().BeSameAs(TimeProvider.System);

    [Fact]
    public void the_default_pacing_rate_is_eight_times_real_time()
        => ReplayMusicGenerator.FromAbc("Default", TestMusic.SimpleAbc).PacingRate
            .Should().Be(ReplayMusicGenerator.DefaultPacingRate);

    private static ReplayMusicGenerator PacedGenerator(ManualTimeProvider time, double rate)
    {
        var generator = ReplayMusicGenerator.FromMidiEvents("Paced",
            TestMusic.OneBarOfQuarterNotes(Resolution));
        generator.TimeProvider = time;
        generator.PacingRate = rate;

        return generator;
    }

    private static MusicRequest Paced() =>
        new MusicRequest { TicksPerQuarterNote = Resolution, PaceInRealTime = true };

    private static double TickSecondsOf(GeneratedMusicEvent item)
    {
        var tick = item.HasEvent ? item.Event.AbsoluteTime : item.SettledThroughTick;

        // 120 beats per minute at 480 ticks to the beat: half a second a beat.
        return (double)tick / Resolution * 0.5;
    }

    private static TimeSpan[] Offsets(IReadOnlyList<ReleasedItem> released, DateTimeOffset start) =>
        released.Select(item => item.At - start).ToArray();

    private static string[] Describe(IReadOnlyList<ReleasedItem> released) =>
        released.Select(item => item.Item.HasEvent
                ? $"{item.Item.Event.AbsoluteTime}|{item.Item.Event}|{item.Item.SettledThroughTick}"
                : $"settled|{item.Item.SettledThroughTick}")
            .ToArray();
}
