using System;
using CodeBrix.Audio.MusicGeneration.Generation;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The sampling settings, whose defaults are the ones a listening session settled on for MUSIC -
/// which are not the ones a text model is usually run with.
/// </summary>
public class MusicGenerationControlsTests
{
    [Fact]
    public void the_defaults_are_the_music_defaults()
    {
        //Act
        var controls = new MusicGenerationControls();

        //Assert
        controls.Temperature.Should().Be(0.8);
        controls.TopK.Should().Be(40);
        controls.TopP.Should().Be(0.92);
        controls.RepetitionPenalty.Should().Be(1.0);
        controls.MaximumEvents.Should().BeNull();
    }

    [Fact]
    public void there_is_no_repetition_penalty_by_default_because_music_repeats_on_purpose()
        => new MusicGenerationControls().RepetitionPenalty
            .Should().Be(MusicGenerationControls.NoRepetitionPenalty);

    [Fact]
    public void SamplingIsDefault_is_true_until_a_sampling_setting_is_changed()
        => new MusicGenerationControls().SamplingIsDefault.Should().BeTrue();

    [Fact]
    public void SamplingIsDefault_is_false_once_the_temperature_moves()
    {
        //Arrange
        var controls = new MusicGenerationControls();

        //Act
        controls.Temperature = 1.1;

        //Assert
        controls.SamplingIsDefault.Should().BeFalse();
    }

    [Fact]
    public void SamplingIsDefault_ignores_the_length_cap_because_it_is_not_a_sampling_setting()
    {
        //Arrange
        var controls = new MusicGenerationControls();

        //Act
        controls.MaximumEvents = 512;

        //Assert
        controls.SamplingIsDefault.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    public void Temperature_rejects_a_value_that_is_not_positive(double temperature)
    {
        //Arrange
        var controls = new MusicGenerationControls();
        Action act = () => controls.Temperature = temperature;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TopK_rejects_a_negative_count()
    {
        //Arrange
        var controls = new MusicGenerationControls();
        Action act = () => controls.TopK = -1;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TopK_takes_zero_to_switch_it_off()
    {
        //Arrange
        var controls = new MusicGenerationControls();

        //Act
        controls.TopK = 0;

        //Assert
        controls.TopK.Should().Be(0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    public void TopP_rejects_a_value_outside_the_probability_mass(double topP)
    {
        //Arrange
        var controls = new MusicGenerationControls();
        Action act = () => controls.TopP = topP;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MaximumEvents_rejects_a_cap_that_is_not_positive()
    {
        //Arrange
        var controls = new MusicGenerationControls();
        Action act = () => controls.MaximumEvents = 0;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Clone_carries_every_setting()
    {
        //Arrange
        var controls = new MusicGenerationControls
        {
            Temperature = 1.2,
            TopK = 10,
            TopP = 0.5,
            RepetitionPenalty = 1.1,
            MaximumEvents = 256
        };

        //Act
        var copy = controls.Clone();

        //Assert
        copy.Temperature.Should().Be(1.2);
        copy.TopK.Should().Be(10);
        copy.TopP.Should().Be(0.5);
        copy.RepetitionPenalty.Should().Be(1.1);
        copy.MaximumEvents.Should().Be(256);
    }
}
