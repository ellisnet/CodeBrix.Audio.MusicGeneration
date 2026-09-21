using System;
using CodeBrix.Audio.MusicGeneration.Rendering;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The fade itself, as a function: where its default lives, and what each shipped curve really
/// does between full level and silence.
/// </summary>
/// <remarks>
/// It touches nothing process-wide and renders nothing, so it needs no collection and no files.
/// The SHAPE OF A FADE IN A RENDERED FILE is asserted against these same functions in
/// <see cref="MusicRenderFadeTests"/>.
/// </remarks>
public class MusicFadeTests
{
    [Fact]
    public void the_default_fade_is_six_seconds_of_eased_decibels_and_lives_in_one_place()
    {
        //Assert - PROVISIONAL, and settled by ear: these two lines are the whole of the default
        MusicFade.DefaultLength.Should().Be(TimeSpan.FromSeconds(6.0));
        MusicFade.DefaultCurve.Should().Be(MusicFadeCurve.EasedDecibels);
        new MusicRenderOptions().FadeLength.Should().Be(MusicFade.DefaultLength);
        new MusicRenderOptions().FadeCurve.Should().Be(MusicFade.DefaultCurve);
    }

    [Theory]
    [InlineData(MusicFadeCurve.EasedDecibels)]
    [InlineData(MusicFadeCurve.StraightLine)]
    [InlineData(MusicFadeCurve.EqualPower)]
    public void every_curve_starts_at_full_level_and_ends_in_silence(MusicFadeCurve curve)
    {
        //Assert
        MusicFade.GainAt(curve, 0.0).Should().Be(1.0F);
        MusicFade.GainAt(curve, 1.0).Should().Be(0.0F);
    }

    [Theory]
    [InlineData(MusicFadeCurve.EasedDecibels)]
    [InlineData(MusicFadeCurve.StraightLine)]
    [InlineData(MusicFadeCurve.EqualPower)]
    public void every_curve_only_ever_falls(MusicFadeCurve curve)
    {
        //Arrange
        var previous = MusicFade.GainAt(curve, 0.0);

        //Act and assert
        for (var step = 1; step <= 1000; step++)
        {
            var gain = MusicFade.GainAt(curve, step / 1000.0);

            gain.Should().BeLessThanOrEqualTo(previous);
            previous = gain;
        }

        previous.Should().Be(0.0F);
    }

    [Theory]
    [InlineData(MusicFadeCurve.EasedDecibels)]
    [InlineData(MusicFadeCurve.StraightLine)]
    [InlineData(MusicFadeCurve.EqualPower)]
    public void anything_outside_the_fade_is_clamped_to_its_ends(MusicFadeCurve curve)
    {
        //Assert
        MusicFade.GainAt(curve, -0.5).Should().Be(1.0F);
        MusicFade.GainAt(curve, 4.0).Should().Be(0.0F);
        MusicFade.GainAt(curve, double.NaN).Should().Be(1.0F);
    }

    [Fact]
    public void a_straight_line_fade_is_half_way_down_in_amplitude_half_way_through()
    {
        //Assert - six decibels down, which is why it seems to hang and then drop
        MusicFade.GainAt(MusicFadeCurve.StraightLine, 0.5).Should().BeApproximately(0.5F, 0.0001F);
        MusicFade.DecibelsAt(MusicFadeCurve.StraightLine, 0.5).Should().BeApproximately(-6.02, 0.01);
    }

    [Fact]
    public void an_equal_power_fade_is_three_decibels_down_half_way_through()
    {
        //Assert
        MusicFade.GainAt(MusicFadeCurve.EqualPower, 0.5)
            .Should().BeApproximately(0.70710678F, 0.0001F);
        MusicFade.DecibelsAt(MusicFadeCurve.EqualPower, 0.5).Should().BeApproximately(-3.01, 0.01);
    }

    [Fact]
    public void the_default_curve_falls_evenly_in_decibels()
    {
        //Assert - the level is the floor times how far through the fade it is, eased at both ends,
        //so a quarter of the way through is a quarter of the way down IN DECIBELS
        Eased(0.25).Should().BeApproximately(MusicFade.FloorDecibels * Smoothstep(0.25), 0.05);
        Eased(0.5).Should().BeApproximately(MusicFade.FloorDecibels * Smoothstep(0.5), 0.5);
        Eased(0.5).Should().BeApproximately(-30.0, 0.5);
    }

    [Fact]
    public void the_default_curve_is_eased_at_both_ends()
    {
        //Assert - AT THE START, IN DECIBELS: the level barely moves in the first hundredth of the
        //fade, where the middle of it is falling steadily, so a fade does not lurch as it begins
        (Eased(0.0) - Eased(0.01)).Should().BeLessThan(Eased(0.49) - Eased(0.51));

        //AT THE END, IN AMPLITUDE: measured in decibels the last hundredth plunges, because that
        //is what reaching TRUE silence means; what is eased there is the amplitude a listener
        //actually hears, and it settles onto silence rather than stepping down to it
        var atTheEnd = Gain(0.98) - Gain(0.99);
        var inTheMiddle = Gain(0.49) - Gain(0.51);

        atTheEnd.Should().BeLessThan(inTheMiddle);
    }

    [Fact]
    public void the_three_curves_really_are_three_different_shapes()
    {
        //Arrange
        var eased = MusicFade.GainAt(MusicFadeCurve.EasedDecibels, 0.5);
        var straight = MusicFade.GainAt(MusicFadeCurve.StraightLine, 0.5);
        var equalPower = MusicFade.GainAt(MusicFadeCurve.EqualPower, 0.5);

        //Assert - half way through, the eased-decibel curve is already far down and the
        //equal-power one has barely moved
        eased.Should().BeLessThan(straight);
        straight.Should().BeLessThan(equalPower);
    }

    private static double Eased(double progress) =>
        MusicFade.DecibelsAt(MusicFadeCurve.EasedDecibels, progress);

    private static float Gain(double progress) =>
        MusicFade.GainAt(MusicFadeCurve.EasedDecibels, progress);

    private static double Smoothstep(double progress) =>
        progress * progress * (3.0 - (2.0 * progress));
}
