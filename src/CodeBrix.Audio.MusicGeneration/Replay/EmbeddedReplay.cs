using System;
using System.Collections.Generic;
using CodeBrix.Audio.MusicGeneration.Internal;

namespace CodeBrix.Audio.MusicGeneration.Replay;

/// <summary>
/// The music that ships inside this library, and the names it plays under. These four generators
/// are always there: they need no registration, and nothing else may register under their names.
/// </summary>
/// <remarks>
/// <para>
/// THE NAMES SAY WHAT THEY ARE. A demo loop that can be mistaken for a model is worse than no demo
/// loop at all, so the active generator is always reportable by name and these names read
/// "embedded replay" wherever they are printed.
/// </para>
/// <para>
/// <see cref="Midi"/> is what plays when nothing has been specified, however many model generators
/// an application has registered. The other three are asked for by name.
/// </para>
/// </remarks>
public static class EmbeddedReplay
{
    /// <summary>
    /// The embedded MIDI piece that plays when no generator has been specified - about two minutes
    /// of it, looping for as long as it is asked for.
    /// </summary>
    public const string Midi = "EmbeddedReplayMidi";

    /// <summary>A second embedded MIDI piece, shorter and sparser, asked for by name.</summary>
    public const string MidiSecond = "EmbeddedReplayMidiSecond";

    /// <summary>The embedded piece written in ABC notation: a two-voice waltz, asked for by name.</summary>
    public const string Abc = "EmbeddedReplayAbc";

    /// <summary>A second embedded ABC piece, a slow single-voice air, asked for by name.</summary>
    public const string AbcSecond = "EmbeddedReplayAbcSecond";

    private static readonly string[] ReservedNames = [Midi, MidiSecond, Abc, AbcSecond];

    /// <summary>
    /// The four reserved names, in the order they are listed by
    /// <see cref="MusicGeneratorRegistry.RegisteredNames"/>.
    /// </summary>
    public static IReadOnlyList<string> Names => ReservedNames;

    /// <summary>
    /// Whether a name belongs to one of the built-in replays, matched without regard to case.
    /// Nothing else may be registered under one.
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
    /// Builds the four built-in generators. The registry calls this - once at start-up, and again
    /// when a test resets it - so that every one of them starts out with nothing loaded.
    /// </summary>
    /// <returns>The four generators, in the order <see cref="Names"/> lists them.</returns>
    internal static ReplayMusicGenerator[] CreateAll() =>
    [
        ReplayMusicGenerator.FromEmbeddedMusic(Midi,
            "Replays a piece of generated MIDI that ships inside this library: the music that " +
            "plays when no generator has been named. It is a recording of what a model once " +
            "wrote, not a model - it plays the same piece every time.",
            EmbeddedMusicResources.DefaultMidi, false),
        ReplayMusicGenerator.FromEmbeddedMusic(MidiSecond,
            "Replays a second piece of generated MIDI that ships inside this library - sparser " +
            "than the default one, and asked for by name.",
            EmbeddedMusicResources.SecondMidi, false),
        ReplayMusicGenerator.FromEmbeddedMusic(Abc,
            "Replays a two-voice waltz written in ABC notation that ships inside this library, " +
            "converted to MIDI as it is played.",
            EmbeddedMusicResources.DefaultAbc, true),
        ReplayMusicGenerator.FromEmbeddedMusic(AbcSecond,
            "Replays a slow single-voice air written in ABC notation that ships inside this " +
            "library, converted to MIDI as it is played.",
            EmbeddedMusicResources.SecondAbc, true)
    ];
}
