using System;
using System.Globalization;
using System.Text;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// THE OPENING BARS A VOICE COUNT TURNS INTO - two parts written in the model's own merged form,
/// because that is the only way a number of parts can mean anything to a model that reads
/// notation.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS. A header alone produces ONE LINE OF MELODY, and a single line stays a single
/// line: CodeBrix.Audio never invents an accompaniment from chord symbols. The two best-rated
/// pieces of the listening sessions that settled this library's taste were DUETS, and what made
/// them duets was two parts in the opening bars - so a request that asks for two voices is given
/// an opening with two parts in it.
/// </para>
/// <para>
/// THE MERGED FORM. One slice carries one bar of EVERY part, separated by bar lines, and the
/// slices are separated by <c>&lt;|&gt;</c>. The opening written here is two slices: the tonic
/// chord, then the chord a step of the scale away - the fifth degree when the tonic triad is
/// major, the fourth when it is minor - which is how both audition duets opened.
/// </para>
/// <para>
/// WHAT THE PARTS PLAY. The upper part arpeggiates the chord in the octave above middle C; the
/// lower part plays the bass note and then either the bass fifth or the chord above it, in the two
/// octaves below. THE PATTERNS ARE THE AUDITION'S OWN: this writer reproduces the first bar of
/// BOTH rated duet prompts exactly, in both parts, and the lower part of their second bars too.
/// The upper part of a second bar comes out in a different inversion, which is music rather than a
/// mistake.
/// </para>
/// <para>
/// NO ACCIDENTAL IS EVER WRITTEN. Every note here is a degree of the scale the <c>K:</c> field
/// names, so the key signature spells it - which is why a key written as <c>none</c>, having no
/// signature at all, is opened in C.
/// </para>
/// </remarks>
internal static class MuPTOpening
{
    /// <summary>The number of voices this writer can write an opening for.</summary>
    public const int MaximumVoiceCount = 2;

    private const int MelodyOctave = 5;
    private const int ChordOctave = 4;
    private const int BassOctave = 3;

    private static readonly int[] NaturalPitchClasses = { 0, 2, 4, 5, 7, 9, 11 };

    private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] NaturalMinor = { 0, 2, 3, 5, 7, 8, 10 };
    private static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
    private static readonly int[] Phrygian = { 0, 1, 3, 5, 7, 8, 10 };
    private static readonly int[] Lydian = { 0, 2, 4, 6, 7, 9, 11 };
    private static readonly int[] Mixolydian = { 0, 2, 4, 5, 7, 9, 10 };
    private static readonly int[] Locrian = { 0, 1, 3, 5, 6, 8, 10 };

    /// <summary>
    /// Refuses, by name, a voice count this model cannot be given an opening for - and a metre and
    /// unit note length whose bar does not divide into whole unit notes.
    /// </summary>
    /// <param name="intent">What the music should be, or null when nothing is specified.</param>
    /// <param name="generatorName">The generator's name, for a refusal that names it.</param>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for a number of parts there is no honest opening to write for.
    /// </exception>
    public static void EnsureWritable(MusicIntent intent, string generatorName)
    {
        if (intent == null || !intent.VoiceCount.HasValue)
        {
            return;
        }

        var voices = intent.VoiceCount.Value;

        if (voices == 1)
        {
            // One part is what a header on its own already produces.
            return;
        }

        if (voices < 1 || voices > MaximumVoiceCount)
        {
            throw new MusicRequestNotHonouredException(
                $"The music generator '{generatorName}' does not honour this request: a number of " +
                "parts means something to this model only as an OPENING written in its own merged " +
                "notation, and the openings it can write are for one part and for two. For " +
                $"{voices}, write the opening bars yourself and hand them over through " +
                "MusicRequest.ModelNativeText.",
                generatorName, MusicRequestFeatures.VoiceCount);
        }

        var meter = intent.Meter ?? MuPTHeader.DefaultMeter;
        var unit = intent.UnitNoteLength ?? MuPTHeader.DefaultUnitNoteLength;

        if (!UnitsPerBar(meter, unit).HasValue)
        {
            throw new MusicRequestNotHonouredException(
                $"The music generator '{generatorName}' does not honour this request: an opening " +
                $"is written in whole unit notes, and a bar of {meter} does not divide into whole " +
                $"notes of {unit.Numerator}/{unit.Denominator}. Choose a unit note length the bar " +
                "divides into, or write the opening bars yourself through " +
                "MusicRequest.ModelNativeText.",
                generatorName, MusicRequestFeatures.VoiceCount);
        }
    }

    /// <summary>Writes the opening bars of a duet, in the model's own merged form.</summary>
    /// <param name="meter">The metre the bars are in.</param>
    /// <param name="unitNoteLength">What a bare note length means, ABC's <c>L:</c> field.</param>
    /// <param name="key">The tonic, as a note letter with an optional accidental, or null for C.</param>
    /// <param name="mode">The mode, or null for the major scale.</param>
    /// <returns>The opening, ending with a slice separator, or empty when it cannot be written.</returns>
    public static string For(MusicMeter meter, MusicNoteLength unitNoteLength, string key,
        MusicMode? mode)
    {
        var units = UnitsPerBar(meter, unitNoteLength);

        if (!units.HasValue)
        {
            return string.Empty;
        }

        var scale = ScaleOf(mode);
        var tonicLetter = LetterOf(key);
        var tonicPitchClass = PitchClassOf(key, tonicLetter);

        // THE SECOND CHORD IS THE FIFTH DEGREE when the tonic triad is major and the FOURTH when
        // it is minor, which is how both audition duets opened: C to G, and A minor to D minor.
        var secondDegree = ThirdInterval(scale, 0) == 4 ? 4 : 3;

        var text = new StringBuilder();

        Slice(text, units.Value, meter, scale, tonicLetter, tonicPitchClass, 0);
        Slice(text, units.Value, meter, scale, tonicLetter, tonicPitchClass, secondDegree);

        return text.ToString();
    }

    /// <summary>How many unit notes a bar of a metre holds, or null when it is not a whole number.</summary>
    /// <param name="meter">The metre.</param>
    /// <param name="unitNoteLength">The unit note length.</param>
    /// <returns>The count, or null.</returns>
    public static int? UnitsPerBar(MusicMeter meter, MusicNoteLength unitNoteLength)
    {
        var top = meter.BeatsPerBar * unitNoteLength.Denominator;
        var bottom = meter.BeatNoteValue * unitNoteLength.Numerator;

        if (bottom <= 0 || top <= 0 || top % bottom != 0)
        {
            return null;
        }

        return top / bottom;
    }

    private static void Slice(StringBuilder text, int units, MusicMeter meter, int[] scale,
        int tonicLetter, int tonicPitchClass, int degree)
    {
        var symbol = ChordSymbol(scale, tonicLetter, tonicPitchClass, degree);

        text.Append('"').Append(symbol).Append("\" ");
        text.Append(UpperBar(units, meter, tonicLetter, degree));
        text.Append(" | ");
        text.Append(LowerBar(units, meter, tonicLetter, degree));
        text.Append(" | ")
            .Append(MuPTSmtAbc.SliceSeparator)
            .Append(' ')
            .Append(MuPTSmtAbc.SliceSeparator)
            .Append(' ');
    }

    // The tune's own melody: the chord, arpeggiated in the octave above middle C.
    private static string UpperBar(int units, MusicMeter meter, int tonicLetter, int degree)
    {
        var root = At(tonicLetter + degree, MelodyOctave);
        var beats = CompoundBeats(units, meter);

        if (beats > 0)
        {
            // A COMPOUND METRE IS FELT IN THREES, so each dotted beat is a run of three notes
            // written as one group - which is how the audition's jig prompt was written.
            var bar = new StringBuilder();

            for (var beat = 0; beat < beats; beat++)
            {
                if (beat > 0)
                {
                    bar.Append(' ');
                }

                var rising = beat % 2 == 0;

                bar.Append(Note(root + (rising ? 0 : 4), 0));
                bar.Append(Note(root + 2, 0));
                bar.Append(Note(root + (rising ? 4 : 0), 0));
            }

            return bar.ToString();
        }

        if (units % 4 == 0)
        {
            var length = units / 4;

            return Spaced(Note(root, length), Note(root + 2, length), Note(root + 4, length),
                Note(root + 2, length));
        }

        if (units % 3 == 0)
        {
            var length = units / 3;

            // THE FIFTH BELOW THE ROOT, then the root, then the third - the rising shape the
            // waltz duet of the listening session opened with.
            return Spaced(Note(root - 3, length), Note(root, length), Note(root + 2, length));
        }

        if (units % 2 == 0)
        {
            var length = units / 2;

            return Spaced(Note(root, length), Note(root + 4, length));
        }

        return Note(root, units);
    }

    // The part underneath: the bass note, then either the bass fifth or the chord above it.
    private static string LowerBar(int units, MusicMeter meter, int tonicLetter, int degree)
    {
        var root = At(tonicLetter + degree, BassOctave);
        var beats = CompoundBeats(units, meter);

        if (beats > 0)
        {
            var written = new string[beats];
            var length = units / beats;

            written[0] = Note(root, length);

            for (var beat = 1; beat < beats; beat++)
            {
                written[beat] = Chord(tonicLetter + degree, length);
            }

            return Spaced(written);
        }

        if (units % 3 == 0)
        {
            var length = units / 3;

            return Spaced(Note(root, length), Chord(tonicLetter + degree, length),
                Chord(tonicLetter + degree, length));
        }

        if (units % 2 == 0)
        {
            var length = units / 2;

            return Spaced(Note(root, length), Note(root + 4, length));
        }

        return Note(root, units);
    }

    // The number of dotted beats in a compound bar whose beat is three unit notes long, or 0 when
    // the bar is not one.
    private static int CompoundBeats(int units, MusicMeter meter)
    {
        if (meter.BeatNoteValue != 8 || meter.BeatsPerBar % 3 != 0)
        {
            return 0;
        }

        var beats = meter.BeatsPerBar / 3;

        return beats > 0 && units == beats * 3 ? beats : 0;
    }

    // The third and the fifth together, in the octave below the melody: a chord under a bass note.
    private static string Chord(int rootLetter, int length) =>
        "[" + Note(At(rootLetter + 2, ChordOctave), 0) + Note(At(rootLetter + 4, ChordOctave), 0) +
        "]" + Length(length);

    // A letter of the scale, put in a named octave: the letter decides the octave's register, so a
    // scale that climbs past B stays where it was put rather than following the letter round.
    private static int At(int letter, int octave) => (octave * 7) + (((letter % 7) + 7) % 7);

    // ABC writes middle C as "C", the octave above it as "c", the one below as "C," and the one
    // above that as "c'".
    private static string Note(int step, int length)
    {
        var octave = step / 7;
        var name = "CDEFGAB"[step % 7];

        var written = octave >= 5
            ? char.ToLowerInvariant(name).ToString() + new string('\'', octave - 5)
            : name.ToString() + new string(',', 4 - octave);

        return length > 0 ? written + Length(length) : written;
    }

    private static string Spaced(params string[] notes) => string.Join(" ", notes);

    private static string Length(int length) =>
        length == 1 ? string.Empty : length.ToString(CultureInfo.InvariantCulture);

    private static string ChordSymbol(int[] scale, int tonicLetter, int tonicPitchClass, int degree)
    {
        var letter = "CDEFGAB"[((tonicLetter + degree) % 7 + 7) % 7];
        var pitchClass = (tonicPitchClass + scale[degree % 7]) % 12;
        var natural = NaturalPitchClasses[((tonicLetter + degree) % 7 + 7) % 7];
        var alter = (((pitchClass - natural) % 12) + 18) % 12 - 6;

        var accidental = alter > 0
            ? new string('#', alter)
            : alter < 0 ? new string('b', -alter) : string.Empty;

        var third = ThirdInterval(scale, degree);
        var fifth = FifthInterval(scale, degree);

        var quality = fifth == 6 ? "dim" : third == 3 ? "m" : string.Empty;

        return letter + accidental + quality;
    }

    private static int ThirdInterval(int[] scale, int degree) =>
        ((scale[(degree + 2) % 7] - scale[degree % 7]) % 12 + 12) % 12;

    private static int FifthInterval(int[] scale, int degree) =>
        ((scale[(degree + 4) % 7] - scale[degree % 7]) % 12 + 12) % 12;

    private static int[] ScaleOf(MusicMode? mode)
    {
        if (!mode.HasValue)
        {
            return Major;
        }

        switch (mode.Value)
        {
            case MusicMode.Minor:
            case MusicMode.Aeolian: return NaturalMinor;
            case MusicMode.Dorian: return Dorian;
            case MusicMode.Phrygian: return Phrygian;
            case MusicMode.Lydian: return Lydian;
            case MusicMode.Mixolydian: return Mixolydian;
            case MusicMode.Locrian: return Locrian;
            default: return Major;
        }
    }

    // A key with no signature at all - ABC's "none" - is opened in C, because every note written
    // here relies on the signature to spell it and C is the only tonic that needs none.
    private static int LetterOf(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        var letter = char.ToUpperInvariant(key.Trim()[0]);
        var at = "CDEFGAB".IndexOf(letter);

        return at < 0 ? 0 : at;
    }

    private static int PitchClassOf(string key, int letter)
    {
        var pitchClass = NaturalPitchClasses[letter];

        if (string.IsNullOrWhiteSpace(key))
        {
            return pitchClass;
        }

        var trimmed = key.Trim();

        for (var index = 1; index < trimmed.Length; index++)
        {
            if (trimmed[index] == '#')
            {
                pitchClass++;
            }
            else if (trimmed[index] == 'b')
            {
                pitchClass--;
            }
            else
            {
                break;
            }
        }

        return ((pitchClass % 12) + 12) % 12;
    }
}
