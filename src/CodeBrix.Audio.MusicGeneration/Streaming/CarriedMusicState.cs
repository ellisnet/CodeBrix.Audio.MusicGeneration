using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// What is in force in the music that has actually reached the timeline: the tempo, the metre, the
/// key, and each channel's program and controllers. It is what carries across a seam.
/// </summary>
/// <remarks>
/// <para>
/// IT DOES TWO JOBS, and they are the same job seen from two sides. Going OUT, it is the carried
/// state handed to a generator with a continuation, so that a new segment does not start by
/// deciding the tempo and the instruments all over again. Coming IN, it is what a seam's events are
/// measured against, so that a new segment restating something that has not changed emits NOTHING -
/// a seam that re-sends the tempo, the metre and five program changes is a seam a listener hears.
/// </para>
/// <para>
/// IT IS FED AT COMMIT TIME, not when music is released from the generator, so it describes the
/// music that will be heard. A follow-up prompt throws away a lookahead that never reached the
/// timeline, and the state must not remember it.
/// </para>
/// </remarks>
internal sealed class CarriedMusicState
{
    private const int Channels = 16;
    private const int ControllerCount = 128;
    private const int NoProgram = -1;
    private const int NoValue = -1;
    private const int NoKey = int.MinValue;

    private static readonly string[] MajorKeys =
    {
        "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#"
    };

    private static readonly string[] MinorKeys =
    {
        "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#", "G#", "D#", "A#"
    };

    private readonly int[] programs = new int[Channels + 1];
    private readonly int[,] controllers = new int[Channels + 1, ControllerCount];

    private int microsecondsPerQuarterNote = NoValue;
    private MusicMeter meter = MusicMeter.CommonTime;
    private bool hasMeter;
    private int sharpsFlats = NoKey;
    private int majorMinor;

    /// <summary>Creates a state that knows nothing yet.</summary>
    public CarriedMusicState()
    {
        for (var channel = 0; channel <= Channels; channel++)
        {
            programs[channel] = NoProgram;

            for (var controller = 0; controller < ControllerCount; controller++)
            {
                controllers[channel, controller] = NoValue;
            }
        }
    }

    /// <summary>The tempo in force, in quarter notes per minute, or null while none has been set.</summary>
    public double? BeatsPerMinute => microsecondsPerQuarterNote <= 0
        ? null
        : 60000000.0 / microsecondsPerQuarterNote;

    /// <summary>The metre in force, or null while none has been set.</summary>
    public MusicMeter? Meter => hasMeter ? meter : null;

    /// <summary>
    /// Takes the whole of another state, which is how the state OF THE LOOKAHEAD is put back to the
    /// state OF WHAT IS PLAYING when a follow-up prompt throws that lookahead away.
    /// </summary>
    /// <param name="other">The state to take.</param>
    public void CopyFrom(CarriedMusicState other)
    {
        for (var channel = 0; channel <= Channels; channel++)
        {
            programs[channel] = other.programs[channel];

            for (var controller = 0; controller < ControllerCount; controller++)
            {
                controllers[channel, controller] = other.controllers[channel, controller];
            }
        }

        microsecondsPerQuarterNote = other.microsecondsPerQuarterNote;
        meter = other.meter;
        hasMeter = other.hasMeter;
        sharpsFlats = other.sharpsFlats;
        majorMinor = other.majorMinor;
    }

    /// <summary>Takes in one event that has reached the timeline.</summary>
    /// <param name="midiEvent">The event, at its tick on the timeline.</param>
    public void Observe(MidiEvent midiEvent)
    {
        switch (midiEvent)
        {
            case TempoEvent tempo:
                microsecondsPerQuarterNote = tempo.MicrosecondsPerQuarterNote;

                return;

            case TimeSignatureEvent signature:
                if (signature.Numerator >= 1 && signature.Denominator >= 0 && signature.Denominator <= 7)
                {
                    meter = new MusicMeter(signature.Numerator, 1 << signature.Denominator);
                    hasMeter = true;
                }

                return;

            case KeySignatureEvent key:
                sharpsFlats = key.SharpsFlats;
                majorMinor = key.MajorMinor;

                return;

            case PatchChangeEvent patch:
                if (IsChannel(patch.Channel))
                {
                    programs[patch.Channel] = patch.Patch;
                }

                return;

            case ControlChangeEvent control:
                var number = (int)control.Controller;

                if (IsChannel(control.Channel) && number >= 0 && number < ControllerCount)
                {
                    controllers[control.Channel, number] = control.ControllerValue;
                }

                return;
        }
    }

    /// <summary>
    /// Whether an event says something the music is already saying, so that emitting it would
    /// change nothing and only mark the seam.
    /// </summary>
    /// <param name="midiEvent">The event.</param>
    /// <returns>True when the value it carries is the one already in force.</returns>
    public bool IsRedundant(MidiEvent midiEvent)
    {
        switch (midiEvent)
        {
            case TempoEvent tempo:
                return microsecondsPerQuarterNote == tempo.MicrosecondsPerQuarterNote;

            case TimeSignatureEvent signature:
                return hasMeter && signature.Numerator >= 1 && signature.Denominator >= 0 &&
                       signature.Denominator <= 7 &&
                       meter == new MusicMeter(signature.Numerator, 1 << signature.Denominator);

            case KeySignatureEvent key:
                return sharpsFlats == key.SharpsFlats && majorMinor == key.MajorMinor;

            case PatchChangeEvent patch:
                return IsChannel(patch.Channel) && programs[patch.Channel] == patch.Patch;

            case ControlChangeEvent control:
                var number = (int)control.Controller;

                return IsChannel(control.Channel) && number >= 0 && number < ControllerCount &&
                       controllers[control.Channel, number] == control.ControllerValue;

            default:
                return false;
        }
    }

    /// <summary>Builds the carried half of a continuation - everything except the tail itself.</summary>
    /// <returns>The continuation, with no tail set.</returns>
    public MusicContinuation ToContinuation()
    {
        var continuation = new MusicContinuation
        {
            BeatsPerMinute = BeatsPerMinute,
            Meter = Meter
        };

        if (sharpsFlats != NoKey && sharpsFlats >= -7 && sharpsFlats <= 7)
        {
            var isMinor = majorMinor == 1;

            continuation.Key = (isMinor ? MinorKeys : MajorKeys)[sharpsFlats + 7];
            continuation.Mode = isMinor ? MusicMode.Minor : MusicMode.Major;
        }

        for (var channel = 1; channel <= Channels; channel++)
        {
            if (programs[channel] >= 0 && programs[channel] < 128)
            {
                continuation.ChannelPrograms[channel] = (GeneralMidiProgram)programs[channel];
            }
        }

        return continuation;
    }

    private static bool IsChannel(int channel) => channel >= 1 && channel <= Channels;
}
