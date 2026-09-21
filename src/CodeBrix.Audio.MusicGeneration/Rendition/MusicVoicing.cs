using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// The whole per-part picture of how the music being played is voiced, as it stands right now.
/// </summary>
/// <remarks>
/// IT GROWS AS THE MUSIC DOES. A part appears here when it first sounds, because until then nobody
/// - not the rendition, not the automatic rule - knows that the part exists. Read it again later
/// and there may be more parts in it, and a part whose instrument the music changed reads as the
/// instrument it changed to.
/// </remarks>
public sealed class MusicVoicing
{
    private readonly PartVoicing[] parts;
    private readonly string[] diagnostics;

    internal MusicVoicing(string renditionName, string instrumentLibraryName, float masterGain,
        PartVoicing[] parts, string[] diagnostics)
    {
        RenditionName = renditionName;
        InstrumentLibraryName = instrumentLibraryName;
        MasterGain = masterGain;
        this.parts = parts;
        this.diagnostics = diagnostics;
    }

    /// <summary>The name of the rendition the voicing came from.</summary>
    public string RenditionName { get; }

    /// <summary>The name of the instrument library the parts are played with.</summary>
    public string InstrumentLibraryName { get; }

    /// <summary>The gain over the whole arrangement, the rendition's master gain times the session's volume.</summary>
    public float MasterGain { get; }

    /// <summary>Every part that has sounded so far, in the order the parts first sounded.</summary>
    public IReadOnlyList<PartVoicing> Parts => parts;

    /// <summary>
    /// Anything worth a developer's eye: a part the instrument library could not play as asked and
    /// what was used instead, a percussion part with no kit behind it, a layer added or dropped.
    /// Empty when everything went as asked.
    /// </summary>
    public IReadOnlyList<string> Diagnostics => diagnostics;

    /// <summary>Describes the voicing for a log or a test failure.</summary>
    /// <returns>One line per part.</returns>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0} through {1}, master gain {2}: {3}",
            RenditionName, InstrumentLibraryName, MasterGain,
            parts.Length == 0 ? "nothing has sounded yet" : string.Join("; ", (object[])parts));
}
