using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// The renditions that ship inside this library, and the names they are asked for under. They are
/// always registered, they need no registration of their own, and nothing else may take one of
/// their names.
/// </summary>
/// <remarks>
/// <para>
/// EVERY ONE OF THEM CARRIES A RATING. The voicings here are not invented: each is a pairing that
/// was played through real instruments to the person whose library this is, and written down with
/// the mark he gave it. <see cref="AmbientDuet"/> is the one that scored 9.7 out of 10.
/// </para>
/// <para>
/// <see cref="Automatic"/> is what plays when no rendition has been named. It has no voices of its
/// own, which is exactly what "voice it automatically" means: honour the music, then the request,
/// then the taste table.
/// </para>
/// </remarks>
public static class BuiltInRenditions
{
    /// <summary>
    /// The rendition that plays when none has been named: no voices of its own, so every part is
    /// voiced by the automatic rule.
    /// </summary>
    public const string Automatic = "Automatic";

    /// <summary>
    /// A celeste carrying the tune over a mixed chorus - the voicing rated 9.7 out of 10, the best
    /// of two listening sessions. It wants a piece of two parts.
    /// </summary>
    public const string AmbientDuet = "AmbientDuet";

    /// <summary>Soft bell-like keys over a warm pad: the tune in front, the pad well under it.</summary>
    public const string MelodyOverPad = "MelodyOverPad";

    /// <summary>A vibraphone over sustained strings at half its level.</summary>
    public const string VibesAndStrings = "VibesAndStrings";

    /// <summary>A harp and a solo cello, side by side at the same level.</summary>
    public const string HarpAndCello = "HarpAndCello";

    /// <summary>
    /// Five distinct, unassuming voices for a piece of any shape - nothing bright stacked on
    /// nothing bright, and nothing at unity gain.
    /// </summary>
    public const string Neutral = "Neutral";

    private static readonly string[] ReservedNames =
    [
        Automatic, AmbientDuet, MelodyOverPad, VibesAndStrings, HarpAndCello, Neutral
    ];

    /// <summary>
    /// The six built-in names, in the order
    /// <see cref="MusicRenditionRegistry.RegisteredNames"/> lists them.
    /// </summary>
    public static IReadOnlyList<string> Names => ReservedNames;

    /// <summary>
    /// Whether a name belongs to a built-in rendition, matched without regard to case. Nothing
    /// else may be registered under one.
    /// </summary>
    /// <param name="name">The name to test.</param>
    /// <returns>True when the name is reserved.</returns>
    public static bool IsReservedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var reserved in ReservedNames)
        {
            if (string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the six built-in renditions. The registry calls this - once at start-up, and again
    /// when a test resets it - so nothing a test does to one of them leaks into the next test.
    /// </summary>
    /// <returns>The renditions, in the order <see cref="Names"/> lists them.</returns>
    internal static MusicRendition[] CreateAll() =>
    [
        new MusicRendition(Automatic,
            "Voices the music itself: a program change the music carries is honoured, then the " +
            "instruments the request asked for, and every part still unvoiced is given a distinct " +
            "instrument from a small table of voicings that were listened to and rated. A part " +
            "playing alone is layered over a pad."),

        Duet(AmbientDuet,
            "A celeste carrying the tune over a mixed chorus - rated 9.7 out of 10, the best " +
            "voicing of two listening sessions.",
            GeneralMidiProgram.Celesta, 0.85F, GeneralMidiProgram.ChoirAahs, 0.55F),

        Duet(MelodyOverPad,
            "Soft bell-like keys in front of a warm pad, the pad at about half the level of the " +
            "keys - rated 8 out of 10.",
            GeneralMidiProgram.ElectricPiano2, 0.85F, GeneralMidiProgram.Pad2Warm, 0.45F),

        Duet(VibesAndStrings,
            "A vibraphone over sustained strings at half its level - rated 8.5 out of 10.",
            GeneralMidiProgram.Vibraphone, 0.85F, GeneralMidiProgram.StringEnsemble1, 0.5F),

        Duet(HarpAndCello,
            "A harp and a solo cello at the same level - rated 8.5 out of 10.",
            GeneralMidiProgram.OrchestralHarp, 0.8F, GeneralMidiProgram.Cello, 0.8F),

        Neutral5(Neutral,
            "Five distinct, unassuming voices for a piece of any shape. Nothing bright is stacked " +
            "on anything else bright, which is what a four-instrument stack at speed was marked " +
            "down for, and no part sits at unity gain.")
    ];

    private static MusicRendition Duet(string name, string description, GeneralMidiProgram first,
        float firstGain, GeneralMidiProgram second, float secondGain)
    {
        var rendition = new MusicRendition(name, description);
        rendition.Voices.Add(new RenditionVoice(first, firstGain));
        rendition.Voices.Add(new RenditionVoice(second, secondGain));

        return rendition;
    }

    private static MusicRendition Neutral5(string name, string description)
    {
        var rendition = new MusicRendition(name, description) { PercussionGain = 0.7F };
        rendition.Voices.Add(new RenditionVoice(GeneralMidiProgram.ElectricPiano2, 0.8F));
        rendition.Voices.Add(new RenditionVoice(GeneralMidiProgram.StringEnsemble1, 0.5F));
        rendition.Voices.Add(new RenditionVoice(GeneralMidiProgram.Vibraphone, 0.75F));
        rendition.Voices.Add(new RenditionVoice(GeneralMidiProgram.AcousticGuitarNylon, 0.65F));
        rendition.Voices.Add(new RenditionVoice(GeneralMidiProgram.ChoirAahs, 0.45F));

        return rendition;
    }
}
