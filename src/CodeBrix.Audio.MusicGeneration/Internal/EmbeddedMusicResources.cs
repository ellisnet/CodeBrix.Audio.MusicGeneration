using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>
/// The four pieces of music that ship inside the assembly, and the one place their manifest names
/// are written down.
/// </summary>
internal static class EmbeddedMusicResources
{
    /// <summary>The manifest-name prefix every embedded piece shares.</summary>
    public const string Prefix = "CodeBrix.Audio.MusicGeneration.EmbeddedMusic.";

    /// <summary>The longer dense piece: the music that plays when nothing has been specified.</summary>
    public const string DefaultMidi = Prefix + "skytnt-fp32-dense-long-seed20260918-2000ev.mid";

    /// <summary>The second embedded MIDI piece, available by name.</summary>
    public const string SecondMidi = Prefix + "skytnt-fp32-ordinary.mid";

    /// <summary>The two-voice waltz: the embedded piece in ABC notation.</summary>
    public const string DefaultAbc = Prefix + "mupt-q8_0-duet-Am-waltz-seed29.abc";

    /// <summary>The second embedded ABC piece, available by name.</summary>
    public const string SecondAbc = Prefix + "mupt-q8_0-air-Dmix-seed11.abc";

    private static readonly string[] AllNames = [DefaultMidi, SecondMidi, DefaultAbc, SecondAbc];

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
