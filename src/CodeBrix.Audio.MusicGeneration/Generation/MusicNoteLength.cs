using System;
using System.Globalization;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// A note value written as a fraction of a whole note - an eighth note is 1/8. It is what ABC's
/// <c>L:</c> field carries, and what a text model needs told before its note lengths mean anything.
/// </summary>
public readonly struct MusicNoteLength : IEquatable<MusicNoteLength>
{
    /// <summary>A quarter note, 1/4.</summary>
    public static readonly MusicNoteLength QuarterNote = new MusicNoteLength(1, 4);

    /// <summary>An eighth note, 1/8 - the usual unit note length of an ABC tune.</summary>
    public static readonly MusicNoteLength EighthNote = new MusicNoteLength(1, 8);

    /// <summary>Creates a note value from a fraction of a whole note.</summary>
    /// <param name="numerator">The top of the fraction.</param>
    /// <param name="denominator">The bottom of the fraction.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either number is not positive.</exception>
    public MusicNoteLength(int numerator, int denominator)
    {
        if (numerator < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), numerator,
                "A note value is a positive fraction of a whole note.");
        }

        if (denominator < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator,
                "A note value is a positive fraction of a whole note.");
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>The top of the fraction.</summary>
    public int Numerator { get; }

    /// <summary>The bottom of the fraction.</summary>
    public int Denominator { get; }

    /// <summary>How long this note value is, in ticks.</summary>
    /// <param name="ticksPerQuarterNote">The tick resolution the music is written at.</param>
    /// <returns>The number of ticks, rounded to the nearest whole tick.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The resolution is not positive.</exception>
    public long Ticks(int ticksPerQuarterNote)
    {
        if (ticksPerQuarterNote < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote), ticksPerQuarterNote,
                "A tick resolution is a positive number of ticks per quarter note.");
        }

        return (long)Math.Round((double)Numerator * 4.0 * ticksPerQuarterNote / Denominator,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>Whether this note value is the same as another, written the same way.</summary>
    /// <param name="other">The note value to compare with.</param>
    /// <returns>True when both numbers match.</returns>
    public bool Equals(MusicNoteLength other) =>
        Numerator == other.Numerator && Denominator == other.Denominator;

    /// <summary>Whether this note value is the same as another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when it is a note value with the same numbers.</returns>
    public override bool Equals(object obj) => obj is MusicNoteLength other && Equals(other);

    /// <summary>A hash of the note value.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    /// <summary>Whether two note values are written the same way.</summary>
    /// <param name="left">The first note value.</param>
    /// <param name="right">The second note value.</param>
    /// <returns>True when both numbers match.</returns>
    public static bool operator ==(MusicNoteLength left, MusicNoteLength right) => left.Equals(right);

    /// <summary>Whether two note values are written differently.</summary>
    /// <param name="left">The first note value.</param>
    /// <param name="right">The second note value.</param>
    /// <returns>True when either number differs.</returns>
    public static bool operator !=(MusicNoteLength left, MusicNoteLength right) => !left.Equals(right);

    /// <summary>Writes the note value as a fraction, as "1/8".</summary>
    /// <returns>The note value as text.</returns>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0}/{1}", Numerator, Denominator);
}
