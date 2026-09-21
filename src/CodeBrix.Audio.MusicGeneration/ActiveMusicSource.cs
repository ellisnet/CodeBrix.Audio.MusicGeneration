using System.Globalization;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;

namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// What is REALLY making the music: which generator, which instrument library, which rendition,
/// and what every part is actually being played with.
/// </summary>
/// <remarks>
/// IT EXISTS SO THAT A DEMO LOOP CAN NEVER MASQUERADE AS A MODEL. Music is trivially easy to get
/// out of this library, and what a consumer gets before they have registered and named a model is
/// a piece of embedded music played back - so the active source is always reportable by name, in
/// one line a log can print, and <see cref="IsReplay"/> answers the question outright.
/// </remarks>
public sealed class ActiveMusicSource
{
    internal ActiveMusicSource(string generatorName, string generatorFamily,
        string generatorDescription, string instrumentLibraryName, string renditionName,
        MusicVoicing voicing)
    {
        GeneratorName = generatorName;
        GeneratorFamily = generatorFamily;
        GeneratorDescription = generatorDescription;
        InstrumentLibraryName = instrumentLibraryName;
        RenditionName = renditionName;
        Voicing = voicing;
    }

    /// <summary>The name of the generator writing the music.</summary>
    public string GeneratorName { get; }

    /// <summary>
    /// The family it belongs to - "Replay" for one playing a piece it already has, or the name of
    /// the model family behind it.
    /// </summary>
    public string GeneratorFamily { get; }

    /// <summary>The generator's own sentence about what it produces.</summary>
    public string GeneratorDescription { get; }

    /// <summary>
    /// Whether the music is a piece being REPLAYED rather than one being composed - which is true
    /// of the four pieces embedded in this library and of any replay a consumer built over music
    /// of their own. It is the question "am I actually hearing a model" in one property.
    /// </summary>
    public bool IsReplay => GeneratorFamily == ReplayMusicGenerator.ReplayFamily;

    /// <summary>The name of the instrument library the parts are played with.</summary>
    public string InstrumentLibraryName { get; }

    /// <summary>The name of the rendition the parts are voiced by.</summary>
    public string RenditionName { get; }

    /// <summary>What every part that has sounded so far is actually being played with.</summary>
    public MusicVoicing Voicing { get; }

    /// <summary>Describes the source in one line, for a log.</summary>
    /// <returns>The line.</returns>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture,
            "{0} ({1}) through {2}, voiced by {3}", GeneratorName, GeneratorFamily,
            InstrumentLibraryName, RenditionName);
}
