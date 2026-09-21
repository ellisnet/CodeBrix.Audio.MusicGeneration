using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using ModelMidiEvent = CodeBrix.Ollama.ModelRunner.MidiEvent;
using ModelMidiEventKind = CodeBrix.Ollama.ModelRunner.MidiEventKind;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// THE ONE PLACE THE CHANNEL RULE LIVES: it turns the model runner's MIDI events into
/// CodeBrix.Audio's, and it is the only code in this library that knows the two count channels
/// differently.
/// </summary>
/// <remarks>
/// <para>
/// CHANNELS. The model runner counts 0 to 15, the way the MIDI wire format does; CodeBrix.Audio
/// counts 1 to 16. ONE IS ADDED HERE AND NOWHERE ELSE, so percussion - channel 9 there - becomes
/// channel 10 here. An adapter for another model does the same thing in its own mapper; nothing
/// downstream ever translates a channel again.
/// </para>
/// <para>
/// TICKS. The model writes at its own resolution and a request fixes the timeline's, so every tick
/// and every note length is rescaled. The scale is applied to the ABSOLUTE tick rather than to a
/// difference, so rounding cannot accumulate along a piece.
/// </para>
/// <para>
/// A NOTE IS ONE EVENT CARRYING ITS LENGTH, which is what the model runner produces and what
/// <c>MidiStream</c> wants: no note-offs are made here and none are wanted.
/// </para>
/// </remarks>
internal static class SkyTNTEventMapper
{
    private const int MetronomeTicksPerClick = 24;
    private const int ThirtySecondNotesPerQuarterNote = 8;

    /// <summary>Adds one to a channel the model runner counted from nought.</summary>
    /// <param name="modelChannel">The channel as the model runner counts it, 0 to 15.</param>
    /// <returns>The channel as CodeBrix.Audio counts it, 1 to 16.</returns>
    /// <remarks>Percussion is 9 there and 10 here, which is the whole of the rule.</remarks>
    public static int ChannelOf(int modelChannel) => modelChannel + 1;

    /// <summary>
    /// Rescales a tick from the model's resolution to the timeline's, to the nearest tick.
    /// </summary>
    /// <param name="modelTick">The tick at the model's resolution.</param>
    /// <param name="modelTicksPerQuarterNote">The model's resolution.</param>
    /// <param name="ticksPerQuarterNote">The timeline's resolution.</param>
    /// <returns>The same musical moment, at the timeline's resolution.</returns>
    public static long Rescale(long modelTick, int modelTicksPerQuarterNote, int ticksPerQuarterNote)
    {
        if (modelTicksPerQuarterNote == ticksPerQuarterNote)
        {
            return modelTick;
        }

        return (long)Math.Round(
            (double)modelTick * ticksPerQuarterNote / modelTicksPerQuarterNote,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Turns one of the model runner's events into a CodeBrix.Audio event at a tick of the
    /// timeline's own.
    /// </summary>
    /// <param name="item">The model runner's event.</param>
    /// <param name="tick">Where it goes, in the segment's own ticks.</param>
    /// <param name="modelTicksPerQuarterNote">The model's resolution, for a note's length.</param>
    /// <param name="ticksPerQuarterNote">The timeline's resolution, for a note's length.</param>
    /// <returns>The event, or null for a kind this library carries nothing across for.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static MidiEvent ToMidiEvent(ModelMidiEvent item, long tick,
        int modelTicksPerQuarterNote, int ticksPerQuarterNote)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        switch (item.Kind)
        {
            case ModelMidiEventKind.Note:
                var length = Rescale(item.DurationTicks, modelTicksPerQuarterNote,
                    ticksPerQuarterNote);

                // A NOTE THAT ROUNDED AWAY TO NOTHING STILL SOUNDS. At a coarse resolution a short
                // note can scale to nought ticks, and a note of no length is a note nobody hears.
                if (length < 1L)
                {
                    length = 1L;
                }

                return new NoteOnEvent(tick, ChannelOf(item.Channel), item.NoteNumber,
                    item.Velocity, (int)length);

            case ModelMidiEventKind.ProgramChange:
                return new PatchChangeEvent(tick, ChannelOf(item.Channel), item.Program);

            case ModelMidiEventKind.ControlChange:
                return new ControlChangeEvent(tick, ChannelOf(item.Channel),
                    (MidiController)item.Controller, item.Value);

            case ModelMidiEventKind.Tempo:
                return new TempoEvent((int)item.MicrosecondsPerQuarterNote, tick);

            case ModelMidiEventKind.TimeSignature:
                return new TimeSignatureEvent(tick, item.Numerator, PowerOfTwo(item.Denominator),
                    MetronomeTicksPerClick, ThirtySecondNotesPerQuarterNote);

            case ModelMidiEventKind.KeySignature:
                return new KeySignatureEvent(item.SharpsOrFlats, item.IsMinor ? 1 : 0, tick);

            default:
                return null;
        }
    }

    /// <summary>The metre a time-signature event sets, or null when the event is not one.</summary>
    /// <param name="midiEvent">The event.</param>
    /// <returns>The metre, or null.</returns>
    public static MusicMeter? MeterOf(MidiEvent midiEvent)
    {
        if (midiEvent is TimeSignatureEvent signature && signature.Numerator >= 1 &&
            signature.Denominator >= 0 && signature.Denominator <= 7)
        {
            return new MusicMeter(signature.Numerator, 1 << signature.Denominator);
        }

        return null;
    }

    // A STANDARD MIDI FILE STORES THE POWER, NOT THE NUMBER: the 4 of 3/4 is written as 2. The
    // model runner validates that its denominator is a power of two, so this always terminates.
    private static int PowerOfTwo(int denominator)
    {
        var power = 0;
        var value = denominator;

        while (value > 1)
        {
            value >>= 1;
            power++;
        }

        return power;
    }
}
