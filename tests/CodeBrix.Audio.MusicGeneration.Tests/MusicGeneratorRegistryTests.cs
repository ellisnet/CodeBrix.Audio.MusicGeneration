using System;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The generator rule: registering is not specifying. With nothing specified the embedded replay
/// plays, however many generators have been registered.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class MusicGeneratorRegistryTests
{
    /// <summary>Starts every test from a registry holding nothing but the built-in replays.</summary>
    public MusicGeneratorRegistryTests() => MusicGeneratorRegistry.ResetForTesting();

    [Fact]
    public void the_four_built_in_replays_are_there_with_no_registration_at_all()
        => MusicGeneratorRegistry.RegisteredNames.Should().Equal(
            EmbeddedReplay.Midi, EmbeddedReplay.MidiSecond,
            EmbeddedReplay.Abc, EmbeddedReplay.AbcSecond);

    [Theory]
    [InlineData("EmbeddedReplayMidi")]
    [InlineData("EmbeddedReplayMidiSecond")]
    [InlineData("EmbeddedReplayAbc")]
    [InlineData("EmbeddedReplayAbcSecond")]
    public void IsRegistered_is_true_for_every_built_in_name(string name)
        => MusicGeneratorRegistry.IsRegistered(name).Should().BeTrue();

    [Fact]
    public void the_built_in_names_say_they_are_the_embedded_replay()
        => EmbeddedReplay.Names.Should().OnlyContain(name => name.StartsWith("EmbeddedReplay"));

    [Fact]
    public void Resolve_with_nothing_specified_is_the_embedded_midi_replay()
        => MusicGeneratorRegistry.Resolve(null).Name.Should().Be(EmbeddedReplay.Midi);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_with_a_blank_name_is_the_embedded_midi_replay(string name)
        => MusicGeneratorRegistry.Resolve(name).Name.Should().Be(EmbeddedReplay.Midi);

    [Fact]
    public void Resolve_with_nothing_specified_is_still_the_embedded_replay_with_one_generator_registered()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));

        //Act
        var resolved = MusicGeneratorRegistry.Resolve(null);

        //Assert
        resolved.Name.Should().Be(EmbeddedReplay.Midi);
    }

    [Fact]
    public void Resolve_with_nothing_specified_is_still_the_embedded_replay_with_two_generators_registered()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));
        MusicGeneratorRegistry.Register(new StubMusicGenerator("SkyTNT"));

        //Act
        var resolved = MusicGeneratorRegistry.Resolve(null);

        //Assert
        resolved.Name.Should().Be(EmbeddedReplay.Midi);
        MusicGeneratorRegistry.Registered.Should().HaveCount(6);
    }

    [Fact]
    public void Resolve_returns_the_generator_whose_name_was_specified()
    {
        //Arrange
        var muPT = new StubMusicGenerator("MuPT");
        MusicGeneratorRegistry.Register(muPT);
        MusicGeneratorRegistry.Register(new StubMusicGenerator("SkyTNT"));

        //Act
        var resolved = MusicGeneratorRegistry.Resolve("MuPT");

        //Assert
        resolved.Should().BeSameAs(muPT);
    }

    [Fact]
    public void Resolve_matches_a_name_without_regard_to_case()
    {
        //Arrange
        var muPT = new StubMusicGenerator("MuPT");
        MusicGeneratorRegistry.Register(muPT);

        //Act
        var resolved = MusicGeneratorRegistry.Resolve("mupt");

        //Assert
        resolved.Should().BeSameAs(muPT);
    }

    [Fact]
    public void Resolve_of_an_unknown_name_lists_what_is_registered()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));
        Action act = () => MusicGeneratorRegistry.Resolve("SkyTNT");

        //Assert
        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().Be(
            "No music generator named 'SkyTNT' is registered. Registered generators: " +
            "EmbeddedReplayMidi, EmbeddedReplayMidiSecond, EmbeddedReplayAbc, " +
            "EmbeddedReplayAbcSecond, MuPT. Register it before asking for it by name.");
    }

    [Fact]
    public void registering_the_same_generator_twice_is_a_no_op()
    {
        //Arrange
        var muPT = new StubMusicGenerator("MuPT");

        //Act
        MusicGeneratorRegistry.Register(muPT);
        MusicGeneratorRegistry.Register(muPT);

        //Assert
        MusicGeneratorRegistry.Registered.Should().HaveCount(5);
    }

    [Fact]
    public void registering_a_different_generator_under_a_taken_name_is_an_error()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));
        Action act = () => MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));

        //Assert
        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().Be(
            "A different music generator is already registered under the name 'MuPT'. Music " +
            "generator names are unique and matched case-insensitively; registering the same " +
            "generator again is a no-op, but two different generators cannot share a name.");
    }

    [Fact]
    public void a_taken_name_is_taken_whatever_the_case()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));
        Action act = () => MusicGeneratorRegistry.Register(new StubMusicGenerator("mupt"));

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("EmbeddedReplayMidi")]
    [InlineData("embeddedreplayabc")]
    public void a_reserved_name_cannot_be_taken_by_another_generator(string reserved)
    {
        //Arrange
        Action act = () => MusicGeneratorRegistry.Register(new StubMusicGenerator(reserved));

        //Assert
        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().Be(
            $"The name '{reserved}' belongs to a replay generator that is built in, so another " +
            "generator cannot be registered under it. The built-in names are: EmbeddedReplayMidi, " +
            "EmbeddedReplayMidiSecond, EmbeddedReplayAbc, EmbeddedReplayAbcSecond.");
    }

    [Fact]
    public void registering_a_built_in_generator_back_over_itself_is_a_no_op()
    {
        //Arrange
        var builtIn = MusicGeneratorRegistry.Resolve(EmbeddedReplay.Abc);

        //Act
        MusicGeneratorRegistry.Register(builtIn);

        //Assert
        MusicGeneratorRegistry.Registered.Should().HaveCount(4);
    }

    [Fact]
    public void Register_rejects_null()
    {
        //Arrange
        Action act = () => MusicGeneratorRegistry.Register(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_rejects_a_generator_with_no_name(string name)
    {
        //Arrange
        Action act = () => MusicGeneratorRegistry.Register(new StubMusicGenerator(name));

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsRegistered_is_false_for_a_name_nobody_registered()
        => MusicGeneratorRegistry.IsRegistered("SkyTNT").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsRegistered_is_false_for_a_blank_name(string name)
        => MusicGeneratorRegistry.IsRegistered(name).Should().BeFalse();

    [Fact]
    public void Registered_lists_the_built_ins_first_and_then_registration_order()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("SkyTNT"));
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));

        //Act
        var names = MusicGeneratorRegistry.RegisteredNames;

        //Assert
        names.Should().Equal(EmbeddedReplay.Midi, EmbeddedReplay.MidiSecond, EmbeddedReplay.Abc,
            EmbeddedReplay.AbcSecond, "SkyTNT", "MuPT");
    }

    [Fact]
    public void ResetForTesting_leaves_the_built_ins_and_nothing_else()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));

        //Act
        MusicGeneratorRegistry.ResetForTesting();

        //Assert
        MusicGeneratorRegistry.RegisteredNames.Should().HaveCount(4);
        MusicGeneratorRegistry.IsRegistered("MuPT").Should().BeFalse();
    }

    [Fact]
    public void ResetForTesting_hands_out_built_ins_with_nothing_loaded()
    {
        //Arrange
        var before = MusicGeneratorRegistry.Resolve(EmbeddedReplay.MidiSecond);

        //Act
        MusicGeneratorRegistry.ResetForTesting();
        var after = MusicGeneratorRegistry.Resolve(EmbeddedReplay.MidiSecond);

        //Assert
        after.Should().NotBeSameAs(before);
        after.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void IsReservedName_knows_the_four_and_nothing_else()
    {
        //Assert
        EmbeddedReplay.IsReservedName(EmbeddedReplay.AbcSecond).Should().BeTrue();
        EmbeddedReplay.IsReservedName("EMBEDDEDREPLAYMIDI").Should().BeTrue();
        EmbeddedReplay.IsReservedName("MuPT").Should().BeFalse();
        EmbeddedReplay.IsReservedName(null).Should().BeFalse();
    }
}
