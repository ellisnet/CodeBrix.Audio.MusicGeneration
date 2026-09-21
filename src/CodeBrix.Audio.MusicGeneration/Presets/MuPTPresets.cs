using System;
using System.Collections.Generic;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Rendition;

namespace CodeBrix.Audio.MusicGeneration.Presets;

/// <summary>
/// THE EIGHT PROMPTS THE MuPT LISTENING SESSION WAS RUN ON, each as a request a caller can use or
/// start from.
/// </summary>
/// <remarks>
/// <para>
/// THEY ARE THE PROMPTS THEMSELVES, CHARACTER FOR CHARACTER. Every one of these is carried as
/// <see cref="MusicRequest.ModelNativeText"/> rather than built from a musical intent, because
/// these are the exact prompts whose music was played to a listener and written down with a mark
/// - and a prompt built from intent would spell the same tempo a different way. Build from
/// <see cref="MusicIntent"/> when you want something these do not cover; the adapter turns an
/// intent into a header of exactly this shape.
/// </para>
/// <para>
/// THE IDIOM IS ABC'S, AND IT IS WORTH SAYING PLAINLY: this is a folk-and-classical notation, in
/// parts, WITH NO DRUMS AT ALL. A reel, a jig, a hornpipe, a waltz, a slow air. There is no
/// percussion to ask for and no electronic idiom to ask for.
/// </para>
/// <para>
/// A SINGLE LINE COMES BACK AS MELODY ONLY - nothing in this family invents an accompaniment from
/// chord symbols - so the two DUET presets, whose openings carry two parts, are the ones that
/// produce two-part music. They are also the two whose music was rated highest of everything that
/// was played.
/// </para>
/// </remarks>
public static class MuPTPresets
{
    private static readonly MusicPreset[] AllPresets;

    static MuPTPresets()
    {
        ReelInGMinor = Preset("ReelInGMinor",
            "A reel in G minor - the prompt from the model's own card, fast and in four.",
            null, "X:1\nL:1/8\nQ:1/8=200\nM:4/4\nK:Gmin\n|:\"Gm\" BGdB");

        JigInD = Preset("JigInD",
            "A jig in D - six eighth notes to the bar, felt in two dotted quarters.",
            null, "X:1\nL:1/8\nQ:3/8=110\nM:6/8\nK:D\n|:\"D\" fed cBA");

        WaltzInAMinor = Preset("WaltzInAMinor",
            "A waltz in A minor - a single line, rated 8 out of 10 through soft keys over a pad.",
            BuiltInRenditions.MelodyOverPad,
            "X:1\nL:1/8\nQ:1/4=120\nM:3/4\nK:Am\n|:\"Am\" A2 c2 e2");

        AirInDMixolydian = Preset("AirInDMixolydian",
            "A slow air in D mixolydian - rated 8.5 out of 10 through a vibraphone over strings.",
            BuiltInRenditions.VibesAndStrings,
            "X:1\nL:1/8\nQ:1/4=70\nM:4/4\nK:Dmix\n|:\"D\" A3 B A2 FD");

        HornpipeInG = Preset("HornpipeInG",
            "A hornpipe in G - dotted, swung pairs at a brisk walk.",
            null, "X:1\nL:1/8\nQ:1/4=150\nM:4/4\nK:G\n|:\"G\" G>A B>c d>B G>B");

        OpenInC = Preset("OpenInC",
            "A header and nothing else, in C: the model chooses the tune entirely.",
            null, "X:1\nL:1/8\nQ:1/4=100\nM:4/4\nK:C\n");

        DuetInC = Preset("DuetInC",
            "A two-part piece in C - rated 8.5 out of 10 through a harp and a solo cello.",
            BuiltInRenditions.HarpAndCello,
            "X:1\nL:1/8\nQ:1/4=96\nM:4/4\nK:C\n" +
            "\"C\" c2 e2 g2 e2 | C,4 G,4 | <|> <|> \"G\" d2 g2 b2 g2 | G,4 D4 | <|> <|> ");

        WaltzDuetInAMinor = Preset("WaltzDuetInAMinor",
            "A two-part waltz in A minor - the highest-rated music of two listening sessions, " +
            "9.7 out of 10 through a celeste over a mixed chorus.",
            BuiltInRenditions.AmbientDuet,
            "X:1\nL:1/8\nQ:1/4=108\nM:3/4\nK:Am\n" +
            "\"Am\" e2 a2 c'2 | A,2 [CE]2 [CE]2 | <|> <|> " +
            "\"Dm\" d2 f2 a2 | D,2 [FA]2 [FA]2 | <|> <|> ");

        AllPresets = new[]
        {
            ReelInGMinor, JigInD, WaltzInAMinor, AirInDMixolydian, HornpipeInG, OpenInC, DuetInC,
            WaltzDuetInAMinor
        };
    }

    /// <summary>A reel in G minor: the prompt printed on the model's own card.</summary>
    public static MusicPreset ReelInGMinor { get; }

    /// <summary>A jig in D, in 6/8.</summary>
    public static MusicPreset JigInD { get; }

    /// <summary>A waltz in A minor, one line of melody.</summary>
    public static MusicPreset WaltzInAMinor { get; }

    /// <summary>A slow air in D mixolydian.</summary>
    public static MusicPreset AirInDMixolydian { get; }

    /// <summary>A hornpipe in G.</summary>
    public static MusicPreset HornpipeInG { get; }

    /// <summary>A header in C and nothing else: the model writes the whole tune.</summary>
    public static MusicPreset OpenInC { get; }

    /// <summary>A two-part piece in C, opened in the model's own merged notation.</summary>
    public static MusicPreset DuetInC { get; }

    /// <summary>A two-part waltz in A minor - the best-rated music of the listening sessions.</summary>
    public static MusicPreset WaltzDuetInAMinor { get; }

    /// <summary>Every preset, in the order the listening session played them.</summary>
    public static IReadOnlyList<MusicPreset> All => AllPresets;

    /// <summary>Finds a preset by name.</summary>
    /// <param name="name">The name, matched without regard to case.</param>
    /// <returns>The preset, or null when no preset has that name.</returns>
    public static MusicPreset Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        for (var index = 0; index < AllPresets.Length; index++)
        {
            if (string.Equals(AllPresets[index].Name, name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return AllPresets[index];
            }
        }

        return null;
    }

    private static MusicPreset Preset(string name, string description, string rendition,
        string prompt) =>
        new MusicPreset(name, MuPTMusicGenerator.MuPTFamily, description, rendition, false,
            () => new MusicRequest { ModelNativeText = prompt });
}
