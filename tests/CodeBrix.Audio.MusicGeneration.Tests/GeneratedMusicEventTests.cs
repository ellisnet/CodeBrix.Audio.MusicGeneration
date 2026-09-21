using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The one thing a generator yields: an event, a settled tick, or a settled tick on its own.
/// </summary>
public class GeneratedMusicEventTests
{
    [Fact]
    public void FromMidiEvent_carries_the_event_and_the_settled_tick()
    {
        //Arrange
        var note = new NoteOnEvent(480L, 3, 64, 100, 240);

        //Act
        var generated = GeneratedMusicEvent.FromMidiEvent(note, 480L);

        //Assert
        generated.HasEvent.Should().BeTrue();
        generated.Event.Should().BeSameAs(note);
        generated.SettledThroughTick.Should().Be(480L);
        generated.HasSettled.Should().BeTrue();
    }

    [Fact]
    public void FromMidiEvent_allows_nothing_settled()
    {
        //Arrange
        var note = new NoteOnEvent(0L, 1, 60, 100, 120);

        //Act
        var generated = GeneratedMusicEvent.FromMidiEvent(note, GeneratedMusicEvent.NothingSettled);

        //Assert
        generated.HasEvent.Should().BeTrue();
        generated.HasSettled.Should().BeFalse();
        generated.SettledThroughTick.Should().Be(-1L);
    }

    [Fact]
    public void FromMidiEvent_rejects_a_null_event()
    {
        //Arrange
        Action act = () => GeneratedMusicEvent.FromMidiEvent(null, 0L);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromMidiEvent_rejects_a_settled_tick_below_nothing_settled()
    {
        //Arrange
        var note = new NoteOnEvent(0L, 1, 60, 100, 120);
        Action act = () => GeneratedMusicEvent.FromMidiEvent(note, -2L);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SettledThrough_carries_no_event()
    {
        //Act
        var generated = GeneratedMusicEvent.SettledThrough(1920L);

        //Assert
        generated.HasEvent.Should().BeFalse();
        generated.Event.Should().BeNull();
        generated.SettledThroughTick.Should().Be(1920L);
    }

    [Fact]
    public void SettledThrough_rejects_a_settled_tick_that_is_not_a_real_tick()
    {
        //Arrange
        Action act = () => GeneratedMusicEvent.SettledThrough(GeneratedMusicEvent.NothingSettled);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NothingSettled_is_minus_one()
        => GeneratedMusicEvent.NothingSettled.Should().Be(-1L);

    [Fact]
    public void ToString_says_what_settled_when_there_is_no_event()
        => GeneratedMusicEvent.SettledThrough(960L).ToString().Should().Be("settled through 960");

    [Fact]
    public void ToString_names_the_event_and_what_settled()
    {
        //Arrange
        var generated = GeneratedMusicEvent.FromMidiEvent(new NoteOnEvent(0L, 1, 60, 100, 120), 0L);

        //Act
        var text = generated.ToString();

        //Assert
        text.Should().Contain("settled through 0");
    }
}
