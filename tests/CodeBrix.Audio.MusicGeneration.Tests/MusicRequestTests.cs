using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>The one request type, and the defaults a caller who says nothing gets.</summary>
public class MusicRequestTests
{
    [Fact]
    public void a_new_request_asks_for_nothing_but_the_defaults()
    {
        //Act
        var request = new MusicRequest();

        //Assert
        request.Text.Should().BeNull();
        request.ModelNativeText.Should().BeNull();
        request.Primer.Should().BeNull();
        request.Intent.Should().BeNull();
        request.InstrumentHints.Should().BeEmpty();
        request.Seed.Should().BeNull();
        request.InferenceThreadCount.Should().BeNull();
        request.Continuation.Should().BeNull();
        request.Controls.Should().NotBeNull();
    }

    [Fact]
    public void TicksPerQuarterNote_defaults_to_480()
        => new MusicRequest().TicksPerQuarterNote.Should().Be(480);

    [Fact]
    public void DefaultTicksPerQuarterNote_is_480()
        => MusicRequest.DefaultTicksPerQuarterNote.Should().Be(480);

    [Fact]
    public void PaceInRealTime_defaults_to_on()
        => new MusicRequest().PaceInRealTime.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(32768)]
    public void TicksPerQuarterNote_rejects_a_resolution_a_midi_header_could_not_carry(int resolution)
    {
        //Arrange
        var request = new MusicRequest();
        Action act = () => request.TicksPerQuarterNote = resolution;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(96)]
    [InlineData(32767)]
    public void TicksPerQuarterNote_takes_every_resolution_a_midi_header_can_carry(int resolution)
    {
        //Arrange
        var request = new MusicRequest();

        //Act
        request.TicksPerQuarterNote = resolution;

        //Assert
        request.TicksPerQuarterNote.Should().Be(resolution);
    }

    [Fact]
    public void InferenceThreadCount_rejects_a_count_that_is_not_positive()
    {
        //Arrange
        var request = new MusicRequest();
        Action act = () => request.InferenceThreadCount = 0;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InferenceThreadCount_takes_null_for_the_generator_s_own_default()
    {
        //Arrange
        var request = new MusicRequest { InferenceThreadCount = 4 };

        //Act
        request.InferenceThreadCount = null;

        //Assert
        request.InferenceThreadCount.Should().BeNull();
    }

    [Fact]
    public void Controls_rejects_null()
    {
        //Arrange
        var request = new MusicRequest();
        Action act = () => request.Controls = null;

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Clone_copies_the_lists_rather_than_sharing_them()
    {
        //Arrange
        var request = new MusicRequest { Seed = 29, TicksPerQuarterNote = 960 };
        request.InstrumentHints.Add(GeneralMidiProgram.Violin);
        request.Intent = new MusicIntent { Key = "A", Mode = MusicMode.Minor };
        request.Intent.CharacterWords.Add("gentle");
        request.Continuation = new MusicContinuation { BeatsPerMinute = 140.0 };
        request.Continuation.ChannelPrograms[1] = GeneralMidiProgram.Celesta;

        //Act
        var copy = request.Clone();
        copy.InstrumentHints.Add(GeneralMidiProgram.Flute);
        copy.Intent.CharacterWords.Add("driving");
        copy.Continuation.ChannelPrograms[2] = GeneralMidiProgram.ChoirAahs;

        //Assert
        copy.Seed.Should().Be(29);
        copy.TicksPerQuarterNote.Should().Be(960);
        copy.Intent.Key.Should().Be("A");
        copy.Continuation.BeatsPerMinute.Should().Be(140.0);
        request.InstrumentHints.Should().HaveCount(1);
        request.Intent.CharacterWords.Should().HaveCount(1);
        request.Continuation.ChannelPrograms.Should().HaveCount(1);
    }

    [Fact]
    public void Clone_of_a_bare_request_carries_no_intent_and_no_continuation()
    {
        //Act
        var copy = new MusicRequest().Clone();

        //Assert
        copy.Intent.Should().BeNull();
        copy.Continuation.Should().BeNull();
    }

    [Fact]
    public void DrumKit_is_unsaid_until_a_request_says_it()
    {
        //Assert - null is "leave it to the generator", which is not the same as no percussion
        new MusicIntent().DrumKit.Should().BeNull();
        MusicIntent.NoDrumKit.Should().Be(-1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(127)]
    [InlineData(MusicIntent.NoDrumKit)]
    public void DrumKit_takes_a_kit_a_percussion_channel_could_hold(int kit) =>
        new MusicIntent { DrumKit = kit }.DrumKit.Should().Be(kit);

    [Theory]
    [InlineData(-2)]
    [InlineData(128)]
    public void DrumKit_rejects_a_program_no_percussion_channel_could_hold(int kit)
    {
        //Act
        Action act = () => new MusicIntent { DrumKit = kit };

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>().Which
            .Message.Should().Contain("MusicIntent.NoDrumKit");
    }

    [Fact]
    public void Clone_carries_the_drum_kit_and_the_voice_count()
    {
        //Arrange
        var request = new MusicRequest
        {
            Intent = new MusicIntent { DrumKit = 24, VoiceCount = 2 }
        };

        //Act
        var copy = request.Clone();

        //Assert
        copy.Intent.DrumKit.Should().Be(24);
        copy.Intent.VoiceCount.Should().Be(2);
    }
}
