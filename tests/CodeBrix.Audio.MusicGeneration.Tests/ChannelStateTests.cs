using System;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Rendition;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// That a part's SETTINGS reach it however late it is voiced, and follow it when its instrument
/// changes: the volume, the pan, the expression and the sends set at tick 0 for a part that does
/// not play until bar nine, and the same settings carried onto the new instrument when the music
/// re-voices a part part-way through.
/// </summary>
public class ChannelStateTests
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;
    private const int ControlChangeCommand = 0xB0;
    private const int PitchBendCommand = 0xE0;
    private const int ChannelPressureCommand = 0xD0;
    private const int PatchChangeCommand = 0xC0;

    [Fact]
    public void a_replaced_instrument_keeps_rendering_its_release_tail()
    {
        //Arrange - a sustained pad, with a fixed rendition so no automatic layer contributes
        var library = Library();
        var rendition = new MusicRendition("RingOut", "A part that follows program changes.")
        {
            FollowsProgramChanges = true,
            MasterGain = 1.0F
        };
        rendition.Voices.Add(new RenditionVoice(4, 1.0F));
        var voicer = new RenditionVoicer(rendition, library, SampleRate, null, 1.0F);
        var router = voicer.Router;
        voicer.Observe(Note(0L, 1));
        voicer.ApplyPending(0, 0x90, 60);
        router.ProcessMidiMessage(0, 0x90, 60, 100);
        var left = new float[2048];
        var right = new float[2048];
        for (var block = 0; block < 4; block++)
        {
            router.Render(left, right);
        }
        var previous = library.Created.Single();
        previous.ActiveVoiceCount.Should().BeGreaterThan(0);

        //Act - replace the sounding part, without playing any note on the new instrument
        voicer.Observe(new PatchChangeEvent(Resolution, 1, (int)GeneralMidiProgram.Flute));
        voicer.ApplyPending(0, PatchChangeCommand, (int)GeneralMidiProgram.Flute);
        router.Render(left, right);

        //Assert - the released child still contributes audio, then is retired when its tail ends
        router.RetiredChildCount.Should().Be(1);
        previous.ActiveVoiceCount.Should().BeGreaterThan(0);
        left.Concat(right).Max(value => Math.Abs(value)).Should().BeGreaterThan(0.0001F);
        library.Created.Last().NoteOnCount.Should().Be(0);
        for (var block = 0; block < 300 && router.RetiredChildCount != 0; block++)
        {
            router.Render(left, right);
        }
        router.RetiredChildCount.Should().Be(0);
    }

    [Fact]
    public void a_volume_and_a_pan_set_at_tick_zero_reach_a_part_that_enters_much_later()
    {
        //Arrange - the whole mix is set up at tick 0, as real General MIDI music does
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(Control(0L, 4, MidiController.MainVolume, 90));
        voicer.Observe(Control(0L, 4, MidiController.Pan, 20));

        //Act - channel 4 does not play until several commit windows later
        voicer.Observe(Note(64L * Resolution, 4));
        voicer.ApplyPending(3, 0x90, 60);

        //Assert
        var part = ThePart(library);
        part.Messages.Should().Contain(Message(ControlChangeCommand, 3, (int)MidiController.MainVolume, 90));
        part.Messages.Should().Contain(Message(ControlChangeCommand, 3, (int)MidiController.Pan, 20));
    }

    [Fact]
    public void the_settings_reach_a_part_before_its_first_note_does()
    {
        //Arrange
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(Control(0L, 1, MidiController.MainVolume, 77));

        //Act - the hook applies what is waiting and THEN the sequencer delivers the note
        voicer.Observe(Note(0L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        var part = ThePart(library);
        var volume = part.Messages.ToList()
            .IndexOf(Message(ControlChangeCommand, 0, (int)MidiController.MainVolume, 77));

        //Assert - it is there, and nothing was played before it
        volume.Should().Be(0);
        part.NoteOnCount.Should().Be(0);
    }

    [Fact]
    public void a_re_voiced_part_keeps_its_volume_its_pan_its_expression_and_its_sends()
    {
        //Arrange
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(Control(0L, 1, MidiController.MainVolume, 88));
        voicer.Observe(Control(0L, 1, MidiController.Pan, 110));
        voicer.Observe(Control(0L, 1, MidiController.Expression, 64));
        voicer.Observe(Control(0L, 1, (MidiController)91, 40));
        voicer.Observe(Control(0L, 1, (MidiController)93, 30));
        voicer.Observe(Note(0L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        var before = library.Created.Count;

        //Act - the music changes the instrument, so a brand-new child is built for this part
        voicer.Observe(new PatchChangeEvent(1920L, 1, (int)GeneralMidiProgram.Trumpet));
        voicer.ApplyPending(0, PatchChangeCommand, (int)GeneralMidiProgram.Trumpet);

        //Assert - the new instrument starts where the old one was, so the mix does not jump
        var replacement = library.Created.Skip(before).First();
        replacement.Program.Should().Be((int)GeneralMidiProgram.Trumpet);
        replacement.Messages.Should().Contain(Message(ControlChangeCommand, 0, (int)MidiController.MainVolume, 88));
        replacement.Messages.Should().Contain(Message(ControlChangeCommand, 0, (int)MidiController.Pan, 110));
        replacement.Messages.Should().Contain(Message(ControlChangeCommand, 0, (int)MidiController.Expression, 64));
        replacement.Messages.Should().Contain(Message(ControlChangeCommand, 0, 91, 40));
        replacement.Messages.Should().Contain(Message(ControlChangeCommand, 0, 93, 30));
    }

    [Fact]
    public void the_latest_value_of_a_controller_is_the_one_a_new_instrument_is_given()
    {
        //Arrange
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(Control(0L, 1, MidiController.MainVolume, 40));
        voicer.Observe(Control(960L, 1, MidiController.MainVolume, 100));

        //Act
        voicer.Observe(Note(1920L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        //Assert
        var part = ThePart(library);
        part.Messages.Should().Contain(Message(ControlChangeCommand, 0, (int)MidiController.MainVolume, 100));
        part.Messages.Should().NotContain(Message(ControlChangeCommand, 0, (int)MidiController.MainVolume, 40));
    }

    [Fact]
    public void the_pitch_bend_range_is_replayed_as_the_whole_conversation_that_sets_it()
    {
        //Arrange - registered parameter 0 is selected, then written
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(Control(0L, 1, (MidiController)101, 0));
        voicer.Observe(Control(0L, 1, (MidiController)100, 0));
        voicer.Observe(Control(0L, 1, (MidiController)6, 12));
        voicer.Observe(Control(0L, 1, (MidiController)38, 0));

        //Act
        voicer.Observe(Note(0L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        //Assert
        var part = ThePart(library);
        var sent = part.Messages.ToList();
        var select = sent.IndexOf(Message(ControlChangeCommand, 0, 101, 0));
        var write = sent.IndexOf(Message(ControlChangeCommand, 0, 6, 12));

        select.Should().BeGreaterThanOrEqualTo(0);
        sent.Should().Contain(Message(ControlChangeCommand, 0, 100, 0));
        write.Should().BeGreaterThan(select);
        sent.Should().Contain(Message(ControlChangeCommand, 0, 38, 0));
    }

    [Fact]
    public void the_pitch_bend_and_the_channel_pressure_are_carried_across_too()
    {
        //Arrange
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(new PitchWheelChangeEvent(0L, 1, 10000));
        voicer.Observe(new ChannelAfterTouchEvent(0L, 1, 55));

        //Act
        voicer.Observe(Note(0L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        //Assert
        var part = ThePart(library);
        part.Messages.Should().Contain(Message(PitchBendCommand, 0, 10000 & 0x7F, (10000 >> 7) & 0x7F));
        part.Messages.Should().Contain(Message(ChannelPressureCommand, 0, 55, 0));
    }

    [Fact]
    public void a_program_change_is_never_replayed_into_a_part_because_the_voicer_chose_its_instrument()
    {
        //Arrange
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.Flute));

        //Act
        voicer.Observe(Note(0L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        //Assert - the part IS a flute, and nobody told the flute to become one
        var part = ThePart(library);
        part.Program.Should().Be((int)GeneralMidiProgram.Flute);
        part.Messages.Should().NotContain(message => message.StartsWith("C0", StringComparison.Ordinal));
    }

    [Fact]
    public void a_channel_mode_message_is_not_a_setting_and_is_not_replayed()
    {
        //Arrange
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(Control(0L, 1, MidiController.AllNotesOff, 0));

        //Act
        voicer.Observe(Note(0L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        //Assert
        ThePart(library).Messages.Should()
            .NotContain(Message(ControlChangeCommand, 0, (int)MidiController.AllNotesOff, 0));
    }

    [Fact]
    public void resetting_all_controllers_clears_the_expression_and_leaves_the_level_alone()
    {
        //Arrange
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(Control(0L, 1, MidiController.MainVolume, 90));
        voicer.Observe(Control(0L, 1, MidiController.Expression, 30));
        voicer.Observe(Control(480L, 1, MidiController.ResetAllControllers, 0));

        //Act
        voicer.Observe(Note(960L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        //Assert
        var part = ThePart(library);
        part.Messages.Should().Contain(Message(ControlChangeCommand, 0, (int)MidiController.MainVolume, 90));
        part.Messages.Should().NotContain(Message(ControlChangeCommand, 0, (int)MidiController.Expression, 30));
    }

    [Fact]
    public void a_layered_part_gives_the_layer_the_same_settings_as_the_part()
    {
        //Arrange - one part on its own is the case the automatic rule layers a pad under
        var library = Library();
        var voicer = Voicer(library);

        voicer.Observe(Control(0L, 1, MidiController.MainVolume, 70));

        //Act
        voicer.Observe(Note(0L, 1));
        voicer.ApplyPending(0, 0x90, 60);

        //Assert
        var built = library.Created;
        built.Count.Should().BeGreaterThan(1);
        built.Should().AllSatisfy(part => part.Messages.Should()
            .Contain(Message(ControlChangeCommand, 0, (int)MidiController.MainVolume, 70)));
    }

    private static string Message(int command, int wireChannel, int data1, int data2) =>
        $"{command:X2} ch{wireChannel} {data1} {data2}";

    // A library of this test's own, so that what it built is only what this test asked for.
    private static TestInstrumentLibrary Library() => TestInstrumentLibrary.Complete("ChannelStateParts");

    private static TestSynthesizer ThePart(TestInstrumentLibrary library) => library.Created[0];

    private static ControlChangeEvent Control(long tick, int channel, MidiController controller,
        int value) =>
        new ControlChangeEvent(tick, channel, controller, value);

    private static NoteOnEvent Note(long tick, int channel) =>
        new NoteOnEvent(tick, channel, 60, 100, Resolution);

    private static RenditionVoicer Voicer(TestInstrumentLibrary library) =>
        new RenditionVoicer(MusicRenditionRegistry.Resolve(BuiltInRenditions.Automatic).Clone(),
            library, SampleRate, null, 1.0F);
}
