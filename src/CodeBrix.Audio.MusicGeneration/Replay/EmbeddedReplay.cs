using System;
using System.Collections.Generic;
using CodeBrix.Audio.MusicGeneration.Internal;

namespace CodeBrix.Audio.MusicGeneration.Replay;

/// <summary>
/// The music that ships inside this library, and the names it plays under. These thirteen
/// generators - the shuffled default, one per embedded MIDI cue, and two ABC tunes - are always
/// there: they need no registration, and nothing else may register under their names.
/// </summary>
/// <remarks>
/// <para>
/// THE NAMES SAY WHAT THEY ARE. A demo loop that can be mistaken for a model is worse than no demo
/// loop at all, so the active generator is always reportable by name and these names read
/// "embedded replay" wherever they are printed.
/// </para>
/// <para>
/// <see cref="Midi"/> is what plays when nothing has been specified, however many model generators
/// an application has registered. It plays the ten embedded MIDI cues in a shuffled order: no cue
/// repeats until all ten have played, and no cue plays twice in a row. Each cue is also reachable
/// on its own by its own name, and a cue asked for by name never shuffles. The cues are written
/// for a General MIDI instrument library.
/// </para>
/// </remarks>
public static class EmbeddedReplay
{
    /// <summary>
    /// The default: the embedded MIDI cues in a shuffled order, one whole cue per pass, with no cue
    /// repeating until all of them have played. It is what plays when no generator has been
    /// specified.
    /// </summary>
    public const string Midi = "EmbeddedReplayMidi";

    /// <summary>
    /// The embedded cue "The Rope Reaches the Window", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string TheRopeReachesTheWindow = "EmbeddedReplayTheRopeReachesTheWindow";

    /// <summary>
    /// The embedded cue "A Way Through the Smoke", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string AWayThroughTheSmoke = "EmbeddedReplayAWayThroughTheSmoke";

    /// <summary>
    /// The embedded cue "The Boat Is Still Waiting", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string TheBoatIsStillWaiting = "EmbeddedReplayTheBoatIsStillWaiting";

    /// <summary>
    /// The embedded cue "Across the Collapsing Bridge", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string AcrossTheCollapsingBridge = "EmbeddedReplayAcrossTheCollapsingBridge";

    /// <summary>
    /// The embedded cue "Running Toward the Light", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string RunningTowardTheLight = "EmbeddedReplayRunningTowardTheLight";

    /// <summary>
    /// The embedded cue "The Last Door Opens", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string TheLastDoorOpens = "EmbeddedReplayTheLastDoorOpens";

    /// <summary>
    /// The embedded cue "Everyone on the Other Side", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string EveryoneOnTheOtherSide = "EmbeddedReplayEveryoneOnTheOtherSide";

    /// <summary>
    /// The embedded cue "A Signal from the Helicopter", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string ASignalFromTheHelicopter = "EmbeddedReplayASignalFromTheHelicopter";

    /// <summary>
    /// The embedded cue "Out of the Flood", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string OutOfTheFlood = "EmbeddedReplayOutOfTheFlood";

    /// <summary>
    /// The embedded cue "Safe Beyond the Searchlights", on its own: an ordinary replay of that one cue, asked for by name.
    /// It never shuffles.
    /// </summary>
    public const string SafeBeyondTheSearchlights = "EmbeddedReplaySafeBeyondTheSearchlights";

    /// <summary>The embedded piece written in ABC notation: a two-voice waltz, asked for by name.</summary>
    public const string Abc = "EmbeddedReplayAbc";

    /// <summary>A second embedded ABC piece, a slow single-voice air, asked for by name.</summary>
    public const string AbcSecond = "EmbeddedReplayAbcSecond";

    private static readonly string[] CueNames =
    [
        TheRopeReachesTheWindow, AWayThroughTheSmoke, TheBoatIsStillWaiting, AcrossTheCollapsingBridge, RunningTowardTheLight, TheLastDoorOpens, EveryoneOnTheOtherSide, ASignalFromTheHelicopter, OutOfTheFlood, SafeBeyondTheSearchlights
    ];

    private static readonly string[] ReservedNames = [Midi, .. CueNames, Abc, AbcSecond];

    /// <summary>
    /// The reserved names, in the order they are listed by
    /// <see cref="MusicGeneratorRegistry.RegisteredNames"/>: <see cref="Midi"/> first, then the ten
    /// cues, then <see cref="Abc"/> and <see cref="AbcSecond"/>.
    /// </summary>
    public static IReadOnlyList<string> Names => ReservedNames;

    /// <summary>
    /// The reserved names of the ten embedded MIDI cues, each an ordinary single-cue replay, in the
    /// order <see cref="Names"/> lists them. <see cref="Midi"/> shuffles over the same ten.
    /// </summary>
    public static IReadOnlyList<string> Cues => CueNames;

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
    /// Builds the built-in generators. The registry calls this - once at start-up, and again when
    /// a test resets it - so that every one of them starts out with nothing loaded.
    /// </summary>
    /// <returns>The generators, in the order <see cref="Names"/> lists them.</returns>
    internal static ReplayMusicGenerator[] CreateAll() => CreateAll(new Random());

    /// <summary>
    /// Builds the built-in generators, with the default's shuffle drawn from the given source - a
    /// seeded one when a test needs a reproducible order.
    /// </summary>
    /// <param name="random">Where the default's shuffles come from.</param>
    /// <returns>The generators, in the order <see cref="Names"/> lists them.</returns>
    internal static ReplayMusicGenerator[] CreateAll(Random random) =>
    [
        CreateDefault(random),
        ReplayMusicGenerator.FromEmbeddedMusic(TheRopeReachesTheWindow,
            "Replays \"The Rope Reaches the Window\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[0], false),
        ReplayMusicGenerator.FromEmbeddedMusic(AWayThroughTheSmoke,
            "Replays \"A Way Through the Smoke\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[1], false),
        ReplayMusicGenerator.FromEmbeddedMusic(TheBoatIsStillWaiting,
            "Replays \"The Boat Is Still Waiting\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[2], false),
        ReplayMusicGenerator.FromEmbeddedMusic(AcrossTheCollapsingBridge,
            "Replays \"Across the Collapsing Bridge\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[3], false),
        ReplayMusicGenerator.FromEmbeddedMusic(RunningTowardTheLight,
            "Replays \"Running Toward the Light\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[4], false),
        ReplayMusicGenerator.FromEmbeddedMusic(TheLastDoorOpens,
            "Replays \"The Last Door Opens\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[5], false),
        ReplayMusicGenerator.FromEmbeddedMusic(EveryoneOnTheOtherSide,
            "Replays \"Everyone on the Other Side\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[6], false),
        ReplayMusicGenerator.FromEmbeddedMusic(ASignalFromTheHelicopter,
            "Replays \"A Signal from the Helicopter\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[7], false),
        ReplayMusicGenerator.FromEmbeddedMusic(OutOfTheFlood,
            "Replays \"Out of the Flood\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[8], false),
        ReplayMusicGenerator.FromEmbeddedMusic(SafeBeyondTheSearchlights,
            "Replays \"Safe Beyond the Searchlights\", one of the original MIDI cues that ship inside this library, on its " +
            "own. It is a replay, not a model, and it plays only this cue.",
            EmbeddedMusicResources.MidiCues[9], false),
        ReplayMusicGenerator.FromEmbeddedMusic(Abc,
            "Replays a two-voice waltz written in ABC notation that ships inside this library, " +
            "converted to MIDI as it is played.",
            EmbeddedMusicResources.DefaultAbc, true),
        ReplayMusicGenerator.FromEmbeddedMusic(AbcSecond,
            "Replays a slow single-voice air written in ABC notation that ships inside this " +
            "library, converted to MIDI as it is played.",
            EmbeddedMusicResources.SecondAbc, true)
    ];

    /// <summary>Builds the shuffled default on its own.</summary>
    /// <param name="random">Where its shuffles come from.</param>
    /// <returns>The default generator, named <see cref="Midi"/>.</returns>
    internal static ReplayMusicGenerator CreateDefault(Random random) =>
        ReplayMusicGenerator.FromEmbeddedMidiCues(Midi,
            "Plays a set of original MIDI pieces that ship inside this library - the music that " +
            "plays when no generator has been named. Each pass is one whole piece, in a shuffled " +
            "order with no piece repeating until all of them have played. It is a replay, not a " +
            "model.",
            EmbeddedMusicResources.MidiCues, random);
}
