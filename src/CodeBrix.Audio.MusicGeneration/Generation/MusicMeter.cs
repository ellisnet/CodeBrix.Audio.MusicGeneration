using System;
using System.Globalization;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// A metre, written the way it is read: three over four, six over eight. The denominator is the
/// note value that gets one beat, so it is a power of two.
/// </summary>
public readonly struct MusicMeter : IEquatable<MusicMeter>
{
    /// <summary>Common time - four beats of a quarter note.</summary>
    public static readonly MusicMeter CommonTime = new MusicMeter(4, 4);

    /// <summary>Creates a metre.</summary>
    /// <param name="beatsPerBar">How many beats there are in a bar - the number on top.</param>
    /// <param name="beatNoteValue">
    /// The note value that gets one beat - the number underneath. It is a power of two: 1 for a
    /// whole note, 4 for a quarter note, 8 for an eighth note.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The beat count is not positive, or the note value is not a positive power of two.
    /// </exception>
    public MusicMeter(int beatsPerBar, int beatNoteValue)
    {
        if (beatsPerBar < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(beatsPerBar), beatsPerBar,
                "A bar has at least one beat in it.");
        }

        if (beatNoteValue < 1 || (beatNoteValue & (beatNoteValue - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(beatNoteValue), beatNoteValue,
                "The beat note value is a power of two: 1, 2, 4, 8, 16 and so on.");
        }

        BeatsPerBar = beatsPerBar;
        BeatNoteValue = beatNoteValue;
    }

    /// <summary>How many beats there are in a bar.</summary>
    public int BeatsPerBar { get; }

    /// <summary>The note value that gets one beat: 4 is a quarter note, 8 an eighth note.</summary>
    public int BeatNoteValue { get; }

    /// <summary>How long a bar of this metre is, in ticks.</summary>
    /// <param name="ticksPerQuarterNote">The tick resolution the music is written at.</param>
    /// <returns>The number of ticks in one bar.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The resolution is not positive.</exception>
    public long TicksPerBar(int ticksPerQuarterNote)
    {
        if (ticksPerQuarterNote < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote), ticksPerQuarterNote,
                "A tick resolution is a positive number of ticks per quarter note.");
        }

        // A whole note is four quarter notes, so the beat is (4 / BeatNoteValue) quarter notes.
        // Multiplying before dividing keeps the arithmetic exact for every resolution that divides
        // the beat, which is every resolution a MIDI file or an ABC tune realistically uses.
        return (long)BeatsPerBar * 4L * ticksPerQuarterNote / BeatNoteValue;
    }

    /// <summary>Whether this metre is the same as another.</summary>
    /// <param name="other">The metre to compare with.</param>
    /// <returns>True when both numbers match.</returns>
    public bool Equals(MusicMeter other) =>
        BeatsPerBar == other.BeatsPerBar && BeatNoteValue == other.BeatNoteValue;

    /// <summary>Whether this metre is the same as another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when it is a metre with the same numbers.</returns>
    public override bool Equals(object obj) => obj is MusicMeter other && Equals(other);

    /// <summary>A hash of the metre.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(BeatsPerBar, BeatNoteValue);

    /// <summary>Whether two metres are the same.</summary>
    /// <param name="left">The first metre.</param>
    /// <param name="right">The second metre.</param>
    /// <returns>True when both numbers match.</returns>
    public static bool operator ==(MusicMeter left, MusicMeter right) => left.Equals(right);

    /// <summary>Whether two metres differ.</summary>
    /// <param name="left">The first metre.</param>
    /// <param name="right">The second metre.</param>
    /// <returns>True when either number differs.</returns>
    public static bool operator !=(MusicMeter left, MusicMeter right) => !left.Equals(right);

    /// <summary>Writes the metre the way it is read, as "3/4".</summary>
    /// <returns>The metre as text.</returns>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0}/{1}", BeatsPerBar, BeatNoteValue);
}
