using System;
using System.Globalization;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// One thing a generator has to say: a MIDI event, how far its output has settled, or both.
/// </summary>
/// <remarks>
/// <para>
/// THE MIDI EVENT is an ordinary CodeBrix.Audio <see cref="MidiEvent"/>, counting channels 1 to 16,
/// so nothing downstream has to translate between event models. A NOTE TRAVELS AS ONE EVENT: a
/// <see cref="NoteOnEvent"/> carrying its duration, which is exactly what
/// <c>MidiStream.Append</c> wants - it schedules the matching note-off itself. A generator does not
/// emit a separate note-off, and a note whose length is not known until later is not emitted until
/// it is.
/// </para>
/// <para>
/// THE SETTLED TICK is the generator's promise that nothing at or before it will change or be
/// added. It is not "the highest tick written so far": a generator may write events out of tick
/// order, and the caller reorders them, holding each event until the settled tick has passed it.
/// <see cref="NothingSettled"/> is the value that says nothing has settled yet, which is what a
/// generator reports while it is still deciding what happens at the tick it is writing.
/// </para>
/// <para>
/// AN ITEM WITH NO EVENT carries a settled tick on its own. It exists because silence is settled
/// too: a generator that has decided on a long rest has nothing to write at those ticks, and
/// without this there would be no way for it to say so. Use <see cref="SettledThrough"/> for it.
/// </para>
/// </remarks>
public sealed class GeneratedMusicEvent
{
    /// <summary>The settled tick that means nothing has settled yet.</summary>
    public const long NothingSettled = -1L;

    private readonly MidiEvent midiEvent;
    private readonly long settledThroughTick;

    private GeneratedMusicEvent(MidiEvent midiEvent, long settledThroughTick)
    {
        this.midiEvent = midiEvent;
        this.settledThroughTick = settledThroughTick;
    }

    /// <summary>
    /// The MIDI event, at a tick measured from the start of the segment, or null when this item
    /// only carries a settled tick.
    /// </summary>
    public MidiEvent Event => midiEvent;

    /// <summary>Whether this item carries a MIDI event, rather than a settled tick alone.</summary>
    public bool HasEvent => midiEvent != null;

    /// <summary>
    /// The tick through which the generator's output has settled: nothing at or before it will
    /// change or be added. <see cref="NothingSettled"/> means nothing has settled yet.
    /// </summary>
    public long SettledThroughTick => settledThroughTick;

    /// <summary>Whether anything has settled - that is, whether the settled tick is a real tick.</summary>
    public bool HasSettled => settledThroughTick >= 0L;

    /// <summary>Builds an item that carries a MIDI event and the settled tick that goes with it.</summary>
    /// <param name="midiEvent">
    /// The event, at a tick measured from the start of the segment. A note is a
    /// <see cref="NoteOnEvent"/> carrying its duration.
    /// </param>
    /// <param name="settledThroughTick">
    /// The tick through which output has settled, or <see cref="NothingSettled"/> when the
    /// generator is not yet sure of anything - including of this event's own tick.
    /// </param>
    /// <returns>The item to yield.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="midiEvent"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The settled tick is below <see cref="NothingSettled"/>.
    /// </exception>
    public static GeneratedMusicEvent FromMidiEvent(MidiEvent midiEvent, long settledThroughTick)
    {
        if (midiEvent == null)
        {
            throw new ArgumentNullException(nameof(midiEvent));
        }

        RequireSettledTick(settledThroughTick, NothingSettled, nameof(settledThroughTick));

        return new GeneratedMusicEvent(midiEvent, settledThroughTick);
    }

    /// <summary>
    /// Builds an item that carries nothing but a settled tick - the way a generator says that a
    /// stretch of silence, or the end of a piece, is decided.
    /// </summary>
    /// <param name="settledThroughTick">The tick through which output has settled.</param>
    /// <returns>The item to yield.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The settled tick is negative.</exception>
    public static GeneratedMusicEvent SettledThrough(long settledThroughTick)
    {
        RequireSettledTick(settledThroughTick, 0L, nameof(settledThroughTick));

        return new GeneratedMusicEvent(null, settledThroughTick);
    }

    /// <summary>Describes the item for a log or a test failure.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() =>
        midiEvent == null
            ? string.Format(CultureInfo.InvariantCulture, "settled through {0}", settledThroughTick)
            : string.Format(CultureInfo.InvariantCulture, "{0} (settled through {1})",
                midiEvent, settledThroughTick);

    private static void RequireSettledTick(long value, long lowest, string parameterName)
    {
        if (value < lowest)
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                lowest == 0L
                    ? "A settled tick carried on its own must be a real tick, so it cannot be negative."
                    : "A settled tick is either a real tick or GeneratedMusicEvent.NothingSettled.");
        }
    }
}
