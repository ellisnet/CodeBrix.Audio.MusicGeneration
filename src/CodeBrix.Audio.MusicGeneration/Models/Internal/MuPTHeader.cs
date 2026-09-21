using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// The head of an ABC tune as this model is prompted with it - and THE SEAM a musical intent is
/// turned into a prompt through.
/// </summary>
/// <remarks>
/// <para>
/// THE FIELDS AND THEIR ORDER ARE THE MODEL CARD'S: <c>X:</c>, <c>L:</c>, <c>Q:</c>, <c>M:</c>,
/// <c>K:</c>, joined with the model's own newline. It is not the order a printed tune is written
/// in, and it is the order every piece of the listening session that settled this library's taste
/// was prompted in, so it is the order used here.
/// </para>
/// <para>
/// THE TEMPO CARRIES ITS OWN BEAT, which is why it is two values rather than one. <c>Q:1/8=200</c>
/// and <c>Q:1/4=100</c> are the same tempo and are not the same text, and the model reads text -
/// so a caller that knows which beat it wants says so.
/// </para>
/// <para>
/// THE OPENING IS WHERE A DUET COMES FROM. A header alone produces a single line of melody; the
/// two best-rated pieces of the listening sessions were DUETS, and what made them duets was an
/// opening written in the model's own merged form, with the parts of one bar separated by bar
/// lines and the bars separated by <c>&lt;|&gt;</c>.
/// </para>
/// </remarks>
internal sealed class MuPTHeader
{
    /// <summary>The unit note length every audition prompt used: an eighth note.</summary>
    public static readonly MusicNoteLength DefaultUnitNoteLength = MusicNoteLength.EighthNote;

    /// <summary>The metre assumed when nothing says otherwise, which is what the library assumes.</summary>
    public static readonly MusicMeter DefaultMeter = MusicMeter.CommonTime;

    /// <summary>The key written when nothing says otherwise.</summary>
    public const string DefaultKey = "C";

    /// <summary>The tune's reference number - ABC's <c>X:</c> field, and always 1 here.</summary>
    public string ReferenceNumber { get; set; } = "1";

    /// <summary>What a bare note length means - ABC's <c>L:</c> field.</summary>
    public MusicNoteLength UnitNoteLength { get; set; } = DefaultUnitNoteLength;

    /// <summary>The note value the tempo counts, or null to write no <c>Q:</c> field at all.</summary>
    public MusicNoteLength? TempoBeat { get; set; }

    /// <summary>How many of those beats a minute holds.</summary>
    public double TempoPerMinute { get; set; }

    /// <summary>The metre - ABC's <c>M:</c> field.</summary>
    public MusicMeter Meter { get; set; } = DefaultMeter;

    /// <summary>The key as ABC writes it: "C", "Am", "Gmin", "Dmix".</summary>
    public string Key { get; set; } = DefaultKey;

    /// <summary>
    /// The opening bars, in the model's own merged form, or empty to let the model start on its
    /// own.
    /// </summary>
    public string Opening { get; set; } = string.Empty;

    /// <summary>Builds the header's lines, in the model card's order.</summary>
    /// <returns>One line per field, without the model's newline between them.</returns>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>
        {
            "X:" + ReferenceNumber,
            "L:" + Fraction(UnitNoteLength)
        };

        if (TempoBeat.HasValue)
        {
            lines.Add("Q:" + Fraction(TempoBeat.Value) + "=" +
                      TempoPerMinute.ToString("0.###", CultureInfo.InvariantCulture));
        }

        lines.Add("M:" + Meter.BeatsPerBar.ToString(CultureInfo.InvariantCulture) + "/" +
                  Meter.BeatNoteValue.ToString(CultureInfo.InvariantCulture));
        lines.Add("K:" + Key);

        return lines;
    }

    /// <summary>The header and its opening, as the model is prompted with them.</summary>
    /// <returns>The prompt text, with the model's newline between the header's lines.</returns>
    public string ToPrompt()
    {
        var lines = Lines();
        var text = string.Join(MuPTSmtAbc.NewLine, lines) + MuPTSmtAbc.NewLine;

        return Opening == null ? text : text + Opening;
    }

    /// <summary>The header alone, without the opening - what a continuation is re-prompted with.</summary>
    /// <returns>The header's lines, joined and ended with the model's newline.</returns>
    public string ToHeaderText() =>
        string.Join(MuPTSmtAbc.NewLine, Lines()) + MuPTSmtAbc.NewLine;

    /// <summary>
    /// Works out the header a request asks for, field by field, leaving out what nothing says.
    /// </summary>
    /// <param name="intent">What the music should be, or null for none of it.</param>
    /// <param name="carried">What the music has been doing, or null when it starts here.</param>
    /// <returns>The header.</returns>
    /// <remarks>
    /// <para>
    /// THE TEMPO'S BEAT IS THE METRE'S. A simple metre counts its tempo in quarter notes and a
    /// compound one - 6/8, 9/8, 12/8 - in dotted quarters, which is how the tempo of a jig is
    /// written and how every audition prompt but one was written. A caller that wants another beat
    /// builds the header itself; that is what this type is for.
    /// </para>
    /// <para>
    /// A VOICE COUNT OF MORE THAN ONE WRITES THE OPENING, through <see cref="MuPTOpening"/>: a
    /// number of parts means nothing to this model on its own, and everything as two parts in the
    /// first bars.
    /// </para>
    /// </remarks>
    public static MuPTHeader For(MusicIntent intent, MusicContinuation carried)
    {
        string key = null;
        MusicMode? mode = null;
        MusicMeter? meter = null;
        MusicNoteLength? unitNoteLength = null;
        double? beatsPerMinute = null;
        int? voiceCount = null;

        if (carried != null)
        {
            key = carried.Key;
            mode = carried.Mode;
            meter = carried.Meter;
            beatsPerMinute = carried.BeatsPerMinute;
        }

        if (intent != null)
        {
            // WHAT THE REQUEST ASKS FOR WINS over what the music has been doing, field by field:
            // a continuation that changes key is a continuation that changes key.
            if (!string.IsNullOrWhiteSpace(intent.Key))
            {
                key = intent.Key;
            }

            if (intent.Mode.HasValue)
            {
                mode = intent.Mode;
            }

            if (intent.Meter.HasValue)
            {
                meter = intent.Meter;
            }

            if (intent.UnitNoteLength.HasValue)
            {
                unitNoteLength = intent.UnitNoteLength;
            }

            if (intent.BeatsPerMinute.HasValue)
            {
                beatsPerMinute = intent.BeatsPerMinute;
            }

            voiceCount = intent.VoiceCount;
        }

        return Build(key, mode, meter, unitNoteLength, beatsPerMinute, voiceCount);
    }

    /// <summary>The note value a tempo is counted in for a metre.</summary>
    /// <param name="meter">The metre.</param>
    /// <returns>A dotted quarter for a compound metre, a quarter otherwise.</returns>
    public static MusicNoteLength TempoBeatFor(MusicMeter meter) =>
        meter.BeatNoteValue == 8 && meter.BeatsPerBar % 3 == 0
            ? new MusicNoteLength(3, 8)
            : MusicNoteLength.QuarterNote;

    /// <summary>The key a tonic and a mode are written as in an ABC <c>K:</c> field.</summary>
    /// <param name="key">The tonic, as a note letter with an optional accidental.</param>
    /// <param name="mode">The mode, or null for none.</param>
    /// <returns>The key, or null when there is no tonic to write.</returns>
    public static string KeyOf(string key, MusicMode? mode)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var tonic = key.Trim();

        if (!mode.HasValue)
        {
            return tonic;
        }

        switch (mode.Value)
        {
            case MusicMode.Major:
            case MusicMode.Ionian: return tonic;
            case MusicMode.Minor:
            case MusicMode.Aeolian: return tonic + "m";
            case MusicMode.Dorian: return tonic + "dor";
            case MusicMode.Phrygian: return tonic + "phr";
            case MusicMode.Lydian: return tonic + "lyd";
            case MusicMode.Mixolydian: return tonic + "mix";
            case MusicMode.Locrian: return tonic + "loc";
            case MusicMode.None: return "none";
            default: return tonic;
        }
    }

    private static MuPTHeader Build(string key, MusicMode? mode, MusicMeter? meter,
        MusicNoteLength? unitNoteLength, double? beatsPerMinute, int? voiceCount)
    {
        var header = new MuPTHeader();
        var written = KeyOf(key, mode);

        if (written != null)
        {
            header.Key = written;
        }
        else if (mode.HasValue && mode.Value == MusicMode.None)
        {
            header.Key = "none";
        }

        if (meter.HasValue)
        {
            header.Meter = meter.Value;
        }

        if (unitNoteLength.HasValue)
        {
            header.UnitNoteLength = unitNoteLength.Value;
        }

        if (voiceCount.HasValue && voiceCount.Value > 1)
        {
            // THE NUMBER OF PARTS IS THE OPENING. A key written as "none" carries no signature to
            // spell the notes with, so the opening is written in C - which needs none.
            header.Opening = MuPTOpening.For(header.Meter, header.UnitNoteLength,
                header.Key == "none" ? null : key, header.Key == "none" ? null : mode);
        }

        if (!beatsPerMinute.HasValue)
        {
            return header;
        }

        // The tempo arrives in QUARTER NOTES a minute, which is how MIDI counts it; the field is
        // written in the beat the metre is felt in, so the number is converted with it.
        var beat = TempoBeatFor(header.Meter);
        var quartersPerBeat = 4.0 * beat.Numerator / beat.Denominator;

        header.TempoBeat = beat;
        header.TempoPerMinute = Rounded(beatsPerMinute.Value / quartersPerBeat);

        return header;
    }

    private static double Rounded(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static string Fraction(MusicNoteLength length) =>
        length.Numerator.ToString(CultureInfo.InvariantCulture) + "/" +
        length.Denominator.ToString(CultureInfo.InvariantCulture);
}
