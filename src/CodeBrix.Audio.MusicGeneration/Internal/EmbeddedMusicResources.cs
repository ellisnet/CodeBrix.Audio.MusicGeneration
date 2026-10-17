using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>
/// The music that ships inside the assembly - ten MIDI cues and two ABC tunes - and the one place
/// their manifest names are written down.
/// </summary>
internal static class EmbeddedMusicResources
{
    /// <summary>The manifest-name prefix every embedded piece shares.</summary>
    public const string Prefix = "CodeBrix.Audio.MusicGeneration.EmbeddedMusic.";

    private static readonly string[] MidiCueNames =
    [
        Prefix + "TheRopeReachesTheWindow.mid",
        Prefix + "AWayThroughTheSmoke.mid",
        Prefix + "TheBoatIsStillWaiting.mid",
        Prefix + "AcrossTheCollapsingBridge.mid",
        Prefix + "RunningTowardTheLight.mid",
        Prefix + "TheLastDoorOpens.mid",
        Prefix + "EveryoneOnTheOtherSide.mid",
        Prefix + "ASignalFromTheHelicopter.mid",
        Prefix + "OutOfTheFlood.mid",
        Prefix + "SafeBeyondTheSearchlights.mid"
    ];

    /// <summary>The two-voice waltz: the embedded piece in ABC notation.</summary>
    public const string DefaultAbc = Prefix + "mupt-q8_0-duet-Am-waltz-seed29.abc";

    /// <summary>The second embedded ABC piece, available by name.</summary>
    public const string SecondAbc = Prefix + "mupt-q8_0-air-Dmix-seed11.abc";

    private static readonly string[] AllNames = [.. MidiCueNames, DefaultAbc, SecondAbc];

    /// <summary>
    /// The ten embedded MIDI cues, by manifest name, in the order <see cref="Replay.EmbeddedReplay"/>
    /// lists their reserved names. The default replay shuffles over all of them.
    /// </summary>
    public static IReadOnlyList<string> MidiCues => MidiCueNames;

    /// <summary>Every embedded piece, by manifest name.</summary>
    public static IReadOnlyList<string> All => AllNames;

    /// <summary>Reads an embedded piece.</summary>
    /// <param name="resourceName">The manifest name of the piece.</param>
    /// <returns>The bytes of the piece.</returns>
    /// <exception cref="MusicGenerationException">
    /// The assembly does not carry that resource, which means the build dropped it.
    /// </exception>
    public static byte[] Read(string resourceName)
    {
        var assembly = typeof(EmbeddedMusicResources).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new MusicGenerationException(
                $"The music '{resourceName}' is not in this assembly, although it is supposed to " +
                "ship inside it. The build dropped the embedded resource.");
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }
}
