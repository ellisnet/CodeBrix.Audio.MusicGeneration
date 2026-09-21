using System;
using CodeBrix.Audio.MusicGeneration.Rendition;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The rendition table: six built-in voicings that are always there, and room for a consumer's own.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class MusicRenditionRegistryTests
{
    /// <summary>Starts every test from freshly built built-ins, because the table is process-wide.</summary>
    public MusicRenditionRegistryTests() => MusicRenditionRegistry.ResetForTesting();

    [Fact]
    public void the_six_built_in_renditions_are_always_there() =>
        MusicRenditionRegistry.RegisteredNames.Should().Equal(BuiltInRenditions.Automatic,
            BuiltInRenditions.AmbientDuet, BuiltInRenditions.MelodyOverPad,
            BuiltInRenditions.VibesAndStrings, BuiltInRenditions.HarpAndCello,
            BuiltInRenditions.Neutral);

    [Fact]
    public void asking_for_nothing_gives_the_automatic_rendition() =>
        MusicRenditionRegistry.Resolve(null).Name.Should().Be(BuiltInRenditions.Automatic);

    [Fact]
    public void the_automatic_rendition_has_no_voices_of_its_own() =>
        MusicRenditionRegistry.Resolve(null).Voices.Should().BeEmpty();

    [Theory]
    [InlineData("ambientduet")]
    [InlineData("AMBIENTDUET")]
    public void a_name_is_matched_without_regard_to_case(string name) =>
        MusicRenditionRegistry.Resolve(name).Name.Should().Be(BuiltInRenditions.AmbientDuet);

    [Fact]
    public void a_name_that_is_not_registered_is_an_error_listing_what_is()
    {
        //Arrange
        Action act = () => MusicRenditionRegistry.Resolve("Nope");

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Be(
            "No rendition named 'Nope' is registered. Registered renditions: Automatic, " +
            "AmbientDuet, MelodyOverPad, VibesAndStrings, HarpAndCello, Neutral. Register it " +
            "before asking for it by name.");
    }

    [Fact]
    public void a_consumer_registers_their_own_and_then_asks_for_it_by_name()
    {
        //Arrange
        var mine = new MusicRendition("MyGame", "The voicing my game ships with");
        mine.Voices.Add(new RenditionVoice(8, 0.9F));

        //Act
        mine.Register();

        //Assert
        MusicRenditionRegistry.Resolve("mygame").Should().BeSameAs(mine);
        MusicRenditionRegistry.IsRegistered("MyGame").Should().BeTrue();
        MusicRenditionRegistry.RegisteredNames.Should().Contain("MyGame");
    }

    [Fact]
    public void registering_the_same_rendition_again_is_a_no_op()
    {
        //Arrange
        var mine = new MusicRendition("MyGame", "The voicing my game ships with");
        mine.Register();

        //Act
        mine.Register();

        //Assert
        MusicRenditionRegistry.Registered.Should().HaveCount(7);
    }

    [Fact]
    public void a_different_rendition_under_a_taken_name_is_an_error()
    {
        //Arrange
        new MusicRendition("MyGame", "The first one").Register();
        Action act = () => new MusicRendition("MyGame", "The second one").Register();

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Contain("A different rendition is already registered");
    }

    [Fact]
    public void a_built_in_name_cannot_be_taken()
    {
        //Arrange
        Action act = () => new MusicRendition(BuiltInRenditions.AmbientDuet, "Mine, not yours").Register();

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Contain("belongs to a rendition that is built in");
    }

    [Fact]
    public void registering_nothing_is_an_error() =>
        ((Action)(() => MusicRenditionRegistry.Register(null))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void IsRegistered_says_no_to_a_blank_name_rather_than_throwing() =>
        MusicRenditionRegistry.IsRegistered(" ").Should().BeFalse();

    [Fact]
    public void a_reserved_name_is_recognised_without_regard_to_case() =>
        BuiltInRenditions.IsReservedName("neutral").Should().BeTrue();

    [Fact]
    public void a_name_nobody_reserved_is_not_reserved() =>
        BuiltInRenditions.IsReservedName("MyGame").Should().BeFalse();
}
