using System.Collections.Generic;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// How one part is SET UP, as opposed to what it plays: its controllers, its pitch-bend range, its
/// pitch bend and its channel pressure. Not its notes, and not its program.
/// </summary>
/// <remarks>
/// <para>
/// WHY A PART'S SETTINGS HAVE TO BE KEPT. Real General MIDI music sets every channel's volume, pan,
/// expression and reverb send at tick 0, long before a part that enters at bar nine plays its first
/// note - and a part is given its instrument when it first SOUNDS, because a rendition written
/// before the music exists cannot know which channel will carry the tune. Without this, everything
/// set before that first note would be lost and the mix would be wrong.
/// </para>
/// <para>
/// AND WHY IT HAS TO FOLLOW A RE-VOICING. A part whose instrument changes part-way through gets a
/// brand-new child synthesizer, which starts at the default of everything. Without this, its level
/// and its position would JUMP at that moment.
/// </para>
/// <para>
/// THE PROGRAM IS DELIBERATELY NOT HERE. Which instrument a part plays is the voicer's decision,
/// taken from the rendition, the music and the taste table; replaying a program change into a child
/// that was built for a chosen instrument would undo that decision.
/// </para>
/// </remarks>
internal sealed class ChannelState
{
    private const int ControllerCount = 128;
    private const int NotSet = -1;

    private const int ControlChangeCommand = 0xB0;
    private const int PitchBendCommand = 0xE0;
    private const int ChannelPressureCommand = 0xD0;

    private const int DataEntryMsb = 6;
    private const int DataEntryLsb = 38;
    private const int DataIncrement = 96;
    private const int DataDecrement = 97;
    private const int NonRegisteredLsb = 98;
    private const int NonRegisteredMsb = 99;
    private const int RegisteredLsb = 100;
    private const int RegisteredMsb = 101;
    private const int FirstChannelModeController = 120;
    private const int ResetAllControllers = 121;

    private const int MainVolume = 7;
    private const int Pan = 10;
    private const int BankSelectMsb = 0;
    private const int BankSelectLsb = 32;

    private readonly int[] controllers = new int[ControllerCount];

    private int selectedRegisteredMsb = NotSet;
    private int selectedRegisteredLsb = NotSet;
    private int bendRangeMsb = NotSet;
    private int bendRangeLsb = NotSet;
    private int pitchBend = NotSet;
    private int pressure = NotSet;

    /// <summary>Creates a state in which nothing has been set.</summary>
    public ChannelState()
    {
        for (var controller = 0; controller < ControllerCount; controller++)
        {
            controllers[controller] = NotSet;
        }
    }

    /// <summary>
    /// Takes in one event that has reached the timeline on this channel, and keeps whatever of it
    /// is part of the channel's state.
    /// </summary>
    /// <param name="midiEvent">The event.</param>
    public void Observe(MidiEvent midiEvent)
    {
        switch (midiEvent)
        {
            case ControlChangeEvent control:
                ObserveController((int)control.Controller, control.ControllerValue);

                return;

            case PitchWheelChangeEvent bend:
                pitchBend = bend.Pitch;

                return;

            case ChannelAfterTouchEvent aftertouch:
                pressure = aftertouch.AfterTouchPressure;

                return;
        }
    }

    /// <summary>
    /// Everything that has to be sent to a newly built instrument, in the order it has to be sent.
    /// </summary>
    /// <returns>The messages, or an empty list when the channel has never been set up.</returns>
    public IReadOnlyList<ChannelStateMessage> Snapshot()
    {
        var messages = new List<ChannelStateMessage>();

        for (var controller = 0; controller < ControllerCount; controller++)
        {
            if (controllers[controller] != NotSet)
            {
                messages.Add(new ChannelStateMessage(ControlChangeCommand, controller,
                    controllers[controller]));
            }
        }

        if (bendRangeMsb != NotSet || bendRangeLsb != NotSet)
        {
            // The pitch-bend range is registered parameter 0, and it is set by SELECTING it and
            // then writing its value - so it has to be replayed as that whole little conversation.
            messages.Add(new ChannelStateMessage(ControlChangeCommand, RegisteredMsb, 0));
            messages.Add(new ChannelStateMessage(ControlChangeCommand, RegisteredLsb, 0));
            messages.Add(new ChannelStateMessage(ControlChangeCommand, DataEntryMsb,
                bendRangeMsb == NotSet ? 2 : bendRangeMsb));

            if (bendRangeLsb != NotSet)
            {
                messages.Add(new ChannelStateMessage(ControlChangeCommand, DataEntryLsb, bendRangeLsb));
            }
        }

        if (selectedRegisteredMsb != NotSet && selectedRegisteredLsb != NotSet &&
            (selectedRegisteredMsb != 0 || selectedRegisteredLsb != 0))
        {
            // Leave the part selecting whatever it was selecting, so that a data entry arriving
            // after the new instrument is built still goes where the music meant it to.
            messages.Add(new ChannelStateMessage(ControlChangeCommand, RegisteredMsb,
                selectedRegisteredMsb));
            messages.Add(new ChannelStateMessage(ControlChangeCommand, RegisteredLsb,
                selectedRegisteredLsb));
        }

        if (pitchBend != NotSet)
        {
            messages.Add(new ChannelStateMessage(PitchBendCommand, pitchBend & 0x7F,
                (pitchBend >> 7) & 0x7F));
        }

        if (pressure != NotSet)
        {
            messages.Add(new ChannelStateMessage(ChannelPressureCommand, pressure, 0));
        }

        return messages;
    }

    private void ObserveController(int controller, int value)
    {
        if (controller < 0 || controller >= ControllerCount)
        {
            return;
        }

        switch (controller)
        {
            case RegisteredMsb:
                selectedRegisteredMsb = value;

                return;

            case RegisteredLsb:
                selectedRegisteredLsb = value;

                return;

            case NonRegisteredMsb:
            case NonRegisteredLsb:
                // A non-registered parameter is selected: no registered one is, so a later data
                // entry is not the pitch-bend range.
                selectedRegisteredMsb = NotSet;
                selectedRegisteredLsb = NotSet;

                return;

            case DataEntryMsb:
                if (IsPitchBendRangeSelected())
                {
                    bendRangeMsb = value;
                }

                return;

            case DataEntryLsb:
                if (IsPitchBendRangeSelected())
                {
                    bendRangeLsb = value;
                }

                return;

            case DataIncrement:
            case DataDecrement:
                // Relative, and only meaningful against whatever the synthesizer already holds.
                return;

            case ResetAllControllers:
                Reset();

                return;
        }

        if (controller >= FirstChannelModeController)
        {
            // Channel mode messages - all sound off, all notes off and the rest - are things that
            // HAPPEN, not settings a part carries. There is nothing to restore.
            return;
        }

        controllers[controller] = value;
    }

    private bool IsPitchBendRangeSelected() =>
        selectedRegisteredMsb == 0 && selectedRegisteredLsb == 0;

    private void Reset()
    {
        // What "reset all controllers" resets: the expressive controllers and the pedals, the pitch
        // bend and the pressure. It deliberately leaves the level, the position and the bank alone,
        // which is what the MIDI specification says and what a mix depends on.
        for (var controller = 0; controller < ControllerCount; controller++)
        {
            if (controller == MainVolume || controller == Pan || controller == BankSelectMsb ||
                controller == BankSelectLsb)
            {
                continue;
            }

            controllers[controller] = NotSet;
        }

        bendRangeMsb = NotSet;
        bendRangeLsb = NotSet;
        selectedRegisteredMsb = NotSet;
        selectedRegisteredLsb = NotSet;
        pitchBend = NotSet;
        pressure = NotSet;
    }
}
