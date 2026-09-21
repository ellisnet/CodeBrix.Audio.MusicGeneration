using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;
using ModelMidiEvent = CodeBrix.Ollama.ModelRunner.MidiEvent;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The one place the channel rule lives, proved on events built by hand - no model needed.
/// </summary>
public class SkyTNTEventMapperTests
{
    private const int ModelResolution = 480;
    private const int Resolution = 480;

    [Fact]
    public void ChannelOf_adds_one_because_the_model_counts_channels_from_nought() =>
        SkyTNTEventMapper.ChannelOf(0).Should().Be(1);

    [Fact]
    public void ChannelOf_turns_the_model_percussion_channel_into_channel_ten() =>
        SkyTNTEventMapper.ChannelOf(9).Should().Be(10);

    [Fact]
    public void ChannelOf_turns_the_last_model_channel_into_channel_sixteen() =>
        SkyTNTEventMapper.ChannelOf(15).Should().Be(16);

    [Fact]
    public void Rescale_leaves_a_tick_alone_when_both_resolutions_agree() =>
        SkyTNTEventMapper.Rescale(1234L, 480, 480).Should().Be(1234L);

    [Fact]
    public void Rescale_doubles_a_tick_for_a_timeline_twice_as_fine() =>
        SkyTNTEventMapper.Rescale(480L, 480, 960).Should().Be(960L);

    [Fact]
    public void Rescale_halves_a_tick_for_a_timeline_half_as_fine() =>
        SkyTNTEventMapper.Rescale(480L, 480, 240).Should().Be(240L);

    [Fact]
    public void Rescale_rounds_rather_than_truncating() =>
        SkyTNTEventMapper.Rescale(1L, 480, 960).Should().Be(2L);

    [Fact]
    public void a_note_becomes_one_note_on_carrying_its_length()
    {
        //Arrange - the model's channel 0 and a note lasting a quarter note
        var source = ModelMidiEvent.Note(960L, 1, 0, 60, 100, 480L);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 960L, ModelResolution, Resolution);

        //Assert
        var note = mapped.Should().BeOfType<NoteOnEvent>().Subject;
        note.AbsoluteTime.Should().Be(960L);
        note.Channel.Should().Be(1);
        note.NoteNumber.Should().Be(60);
        note.Velocity.Should().Be(100);
        note.NoteLength.Should().Be(480);
    }

    [Fact]
    public void a_percussion_note_lands_on_channel_ten()
    {
        //Arrange
        var source = ModelMidiEvent.Note(0L, 1, 9, 36, 110, 120L);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 0L, ModelResolution, Resolution);

        //Assert
        mapped.Should().BeOfType<NoteOnEvent>().Subject.Channel.Should().Be(10);
    }

    [Fact]
    public void a_note_length_is_rescaled_with_the_tick()
    {
        //Arrange - the timeline is twice as fine as the model
        var source = ModelMidiEvent.Note(480L, 1, 0, 60, 100, 240L);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 960L, ModelResolution, 960);

        //Assert
        mapped.Should().BeOfType<NoteOnEvent>().Subject.NoteLength.Should().Be(480);
    }

    [Fact]
    public void a_note_that_rounded_away_to_nothing_still_sounds_for_one_tick()
    {
        //Arrange - a very short note against a very coarse timeline
        var source = ModelMidiEvent.Note(0L, 1, 0, 60, 100, 1L);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 0L, ModelResolution, 4);

        //Assert
        mapped.Should().BeOfType<NoteOnEvent>().Subject.NoteLength.Should().Be(1);
    }

    [Fact]
    public void a_program_change_is_carried_across()
    {
        //Arrange
        var source = ModelMidiEvent.ProgramChange(0L, 1, 3, 40);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 0L, ModelResolution, Resolution);

        //Assert
        var patch = mapped.Should().BeOfType<PatchChangeEvent>().Subject;
        patch.Channel.Should().Be(4);
        patch.Patch.Should().Be(40);
    }

    [Fact]
    public void a_control_change_is_carried_across()
    {
        //Arrange - the sustain pedal, down
        var source = ModelMidiEvent.ControlChange(240L, 1, 0, 64, 127);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 240L, ModelResolution, Resolution);

        //Assert
        var control = mapped.Should().BeOfType<ControlChangeEvent>().Subject;
        control.Channel.Should().Be(1);
        ((int)control.Controller).Should().Be(64);
        control.ControllerValue.Should().Be(127);
    }

    [Fact]
    public void a_tempo_is_carried_across_in_the_unit_a_file_stores()
    {
        //Arrange
        var source = ModelMidiEvent.Tempo(0L, 0, 140.0);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 0L, ModelResolution, Resolution);

        //Assert - the same microseconds the model runner computed, not a second rounding of ours
        mapped.Should().BeOfType<TempoEvent>().Subject.MicrosecondsPerQuarterNote
            .Should().Be((int)source.MicrosecondsPerQuarterNote);
    }

    [Fact]
    public void a_time_signature_is_carried_across_as_the_power_a_file_stores()
    {
        //Arrange - six eight
        var source = ModelMidiEvent.TimeSignature(0L, 0, 6, 8);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 0L, ModelResolution, Resolution);

        //Assert
        var signature = mapped.Should().BeOfType<TimeSignatureEvent>().Subject;
        signature.Numerator.Should().Be(6);
        signature.Denominator.Should().Be(3);
    }

    [Fact]
    public void a_time_signature_reads_back_as_the_metre_it_names()
    {
        //Arrange
        var source = ModelMidiEvent.TimeSignature(0L, 0, 3, 4);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 0L, ModelResolution, Resolution);
        var meter = SkyTNTEventMapper.MeterOf(mapped);

        //Assert
        meter.HasValue.Should().BeTrue();
        meter.Value.BeatsPerBar.Should().Be(3);
        meter.Value.BeatNoteValue.Should().Be(4);
    }

    [Fact]
    public void a_key_signature_is_carried_across()
    {
        //Arrange - three flats, minor
        var source = ModelMidiEvent.KeySignature(0L, 0, -3, true);

        //Act
        var mapped = SkyTNTEventMapper.ToMidiEvent(source, 0L, ModelResolution, Resolution);

        //Assert
        var key = mapped.Should().BeOfType<KeySignatureEvent>().Subject;
        key.SharpsFlats.Should().Be(-3);
        key.MajorMinor.Should().Be(1);
    }

    [Fact]
    public void an_event_that_is_not_a_time_signature_names_no_metre() =>
        SkyTNTEventMapper.MeterOf(new TempoEvent(500000, 0L)).HasValue.Should().BeFalse();
}
