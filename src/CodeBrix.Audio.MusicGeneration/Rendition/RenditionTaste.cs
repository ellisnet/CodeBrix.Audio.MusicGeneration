using System.Collections.Generic;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// The table a part is voiced from when nothing else has voiced it: an ordered set of instruments
/// and the levels they sit at, each one taken from a voicing that was listened to and rated.
/// </summary>
/// <remarks>
/// <para>
/// IT IS DATA, not code, so retuning a voicing after a listening session is an edit to this one
/// file. Every entry carries the rating it rests on, which is also what a voicing diagnostic
/// prints, so a developer asking "why is this part a vibraphone" gets an answer.
/// </para>
/// <para>
/// THREE RULES ARE BUILT INTO THE ORDER. Pairs that were rated together are adjacent, so a duet
/// picking the first two entries gets the pair that was rated, not two unrelated leads. No entry is
/// the acoustic grand piano, and no entry is at unity gain, because a part left on
/// piano-at-unity-gain by accident is the single failure the ratings were clearest about. And the
/// three programs measured as much louder than their neighbours - electric piano 1, clean electric
/// guitar and the bag pipe - are not here at all.
/// </para>
/// </remarks>
internal static class RenditionTaste
{
    /// <summary>The gain the percussion part sits at when the automatic rule voices it.</summary>
    public const float PercussionGain = MusicRendition.DefaultPercussionGain;

    /// <summary>
    /// The gain a part takes when the MUSIC or the REQUEST chose its instrument and the table has
    /// nothing to say about it. It is below unity deliberately.
    /// </summary>
    public const float ChosenElsewhereGain = RenditionVoice.DefaultGain;

    /// <summary>The instrument layered under a part that is playing alone, and its gain.</summary>
    public const int LoneLayerProgram = (int)GeneralMidiProgram.Pad2Warm;

    /// <summary>How loud the layer under a lone part sits.</summary>
    public const float LoneLayerGain = 0.5F;

    /// <summary>Why a lone part is layered at all, for the diagnostic that says it happened.</summary>
    public const string LoneLayerRating =
        "9/10 - a single sparse part layered over a pad, \"really excellent\"";

    private static readonly TasteEntry[] Table =
    [
        new TasteEntry((int)GeneralMidiProgram.Celesta, 0.85F,
            "9.7/10 - celeste over a mixed chorus, the best-rated voicing of both listening sessions"),
        new TasteEntry((int)GeneralMidiProgram.ChoirAahs, 0.55F,
            "9.7/10 - the mixed chorus under the celeste"),
        new TasteEntry((int)GeneralMidiProgram.Vibraphone, 0.85F,
            "8.5/10 - vibraphone over sustained strings"),
        new TasteEntry((int)GeneralMidiProgram.StringEnsemble1, 0.5F,
            "8.5/10 - the sustained strings under the vibraphone, at half its level"),
        new TasteEntry((int)GeneralMidiProgram.OrchestralHarp, 0.8F,
            "8.5/10 - harp and solo cello"),
        new TasteEntry((int)GeneralMidiProgram.Cello, 0.8F,
            "8.5/10 - the solo cello beside the harp"),
        new TasteEntry((int)GeneralMidiProgram.ElectricPiano2, 0.8F,
            "8/10 - soft bell-like keys carrying the tune over a pad"),
        new TasteEntry((int)GeneralMidiProgram.Pad2Warm, 0.45F,
            "8/10 - the warm pad under the keys"),
        new TasteEntry((int)GeneralMidiProgram.Flute, 0.65F,
            "8/10 - \"very ambient ... ethereal\", the flute of the piano, violin and flute piece"),
        new TasteEntry((int)GeneralMidiProgram.Violin, 0.6F,
            "8/10 - the violin of the same ambient piece"),
        new TasteEntry((int)GeneralMidiProgram.AcousticGuitarNylon, 0.7F,
            "8/10 - the nylon guitar of the dense piece"),
        new TasteEntry((int)GeneralMidiProgram.Pad1NewAge, 0.45F,
            "a second pad, on the taste the 8/10 pad established"),
        new TasteEntry((int)GeneralMidiProgram.MusicBox, 0.7F,
            "the bell family the 9.7/10 celeste came from"),
        new TasteEntry((int)GeneralMidiProgram.TubularBells, 0.6F,
            "the bell family again, darker"),
        new TasteEntry((int)GeneralMidiProgram.Fx3Crystal, 0.55F,
            "ethereal, which is the word both listening sessions kept reaching for")
    ];

    /// <summary>The table, in the order parts are voiced from it.</summary>
    public static IReadOnlyList<TasteEntry> Entries => Table;

    /// <summary>
    /// The gain the table gives an instrument, or <see cref="ChosenElsewhereGain"/> when the
    /// instrument is not one it knows.
    /// </summary>
    /// <param name="program">The General MIDI program.</param>
    /// <returns>The gain to voice it at.</returns>
    public static float GainFor(int program)
    {
        foreach (var entry in Table)
        {
            if (entry.Program == program)
            {
                return entry.Gain;
            }
        }

        return ChosenElsewhereGain;
    }
}
