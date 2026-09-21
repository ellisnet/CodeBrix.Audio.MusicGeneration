using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Rendition;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>The rendition and the voice: data types, and the rules they hold themselves to.</summary>
public class MusicRenditionTests
{
    [Fact]
    public void a_rendition_starts_with_no_voices_which_is_the_automatic_rule()
    {
        //Arrange
        var rendition = new MusicRendition("Mine", "A rendition of my own");

        //Assert
        rendition.Voices.Should().BeEmpty();
        rendition.MasterGain.Should().Be(MusicRendition.DefaultMasterGain);
        rendition.PercussionGain.Should().Be(MusicRendition.DefaultPercussionGain);
        rendition.FollowsProgramChanges.Should().BeFalse();
    }

    [Fact]
    public void a_rendition_must_have_a_name() =>
        ((Action)(() => new MusicRendition(" ", "no name"))).Should().Throw<ArgumentException>();

    [Fact]
    public void a_rendition_must_have_a_description() =>
        ((Action)(() => new MusicRendition("Mine", null))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void a_voice_defaults_to_a_gain_below_unity() =>
        new RenditionVoice(8).Gain.Should().Be(0.8F);

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void a_voice_refuses_a_program_outside_the_General_MIDI_set(int program) =>
        ((Action)(() => new RenditionVoice(program))).Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData(-0.1F)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void a_voice_refuses_a_gain_that_is_not_a_level(float gain) =>
        ((Action)(() => new RenditionVoice(8, gain))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void a_voice_can_carry_one_layer_under_it()
    {
        //Arrange
        var voice = new RenditionVoice(GeneralMidiProgram.ElectricPiano2, 0.85F,
            GeneralMidiProgram.Pad2Warm, 0.45F);

        //Assert
        voice.HasLayer.Should().BeTrue();
        voice.Program.Should().Be(5);
        voice.LayerProgram.Should().Be(89);
        voice.LayerGain.Should().Be(0.45F);
    }

    [Fact]
    public void cloning_a_rendition_copies_its_voices_rather_than_sharing_them()
    {
        //Arrange
        var rendition = new MusicRendition("Mine", "A rendition of my own");
        rendition.Voices.Add(new RenditionVoice(8, 0.9F));

        //Act
        var copy = rendition.Clone();
        rendition.Voices[0].Gain = 0.1F;

        //Assert
        copy.Voices[0].Gain.Should().Be(0.9F);
        copy.Name.Should().Be("Mine");
    }

    [Fact]
    public void no_built_in_voicing_leaves_a_part_on_the_piano_at_unity_gain()
    {
        //Arrange
        var renditions = MusicRenditionRegistry.Registered;

        //Assert
        foreach (var rendition in renditions)
        {
            foreach (var voice in rendition.Voices)
            {
                voice.Gain.Should().BeLessThan(1.0F);
                voice.Program.Should().NotBe((int)GeneralMidiProgram.AcousticGrandPiano);
            }
        }
    }
}
