using System;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Presets;

/// <summary>
/// A ready-made request, with the name of the generator family it was written for and the
/// rendition it was rated through. A preset is somewhere to START: take the request, change what
/// you want, and generate.
/// </summary>
/// <remarks>
/// <para>
/// A PRESET IS A REQUEST, NOT A GENERATOR. It names no generator and registers nothing; the
/// application still registers a generator of the right family and specifies it by name. A preset
/// handed to a generator that cannot honour it is refused by name, like any other request.
/// </para>
/// <para>
/// <see cref="SuggestedRendition"/> IS EVIDENCE, NOT DECORATION. Where a listening session rated a
/// voicing for this music, the rendition it was rated through is named here; where none was rated,
/// it is null and the automatic rendition voices the piece.
/// </para>
/// </remarks>
public sealed class MusicPreset
{
    private readonly Func<MusicRequest> request;

    internal MusicPreset(string name, string family, string description, string suggestedRendition,
        bool isProvisional, Func<MusicRequest> request)
    {
        Name = name;
        Family = family;
        Description = description;
        SuggestedRendition = suggestedRendition;
        IsProvisional = isProvisional;
        this.request = request;
    }

    /// <summary>The preset's name, matched without regard to case.</summary>
    public string Name { get; }

    /// <summary>
    /// The generator family this was written for - the same word a generator reports through
    /// <see cref="IMusicGenerator.Family"/>.
    /// </summary>
    public string Family { get; }

    /// <summary>What the music is, in one line.</summary>
    public string Description { get; }

    /// <summary>
    /// The rendition this music was rated through, or null when no listening session rated one and
    /// the automatic rendition is the answer.
    /// </summary>
    public string SuggestedRendition { get; }

    /// <summary>
    /// Whether this preset is still waiting to be judged by ear. A provisional preset is a
    /// reasoned guess that has not been listened to yet, and it may be revised or withdrawn.
    /// </summary>
    public bool IsProvisional { get; }

    /// <summary>Builds the request this preset stands for.</summary>
    /// <returns>A new request every time, so a caller can change it freely.</returns>
    public MusicRequest CreateRequest() => request();

    /// <summary>A line a console application or a log can write straight out.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        var rendition = SuggestedRendition == null
            ? "voiced automatically"
            : "through " + SuggestedRendition;

        return $"{Name} ({Family}, {rendition}){(IsProvisional ? " - provisional" : string.Empty)}" +
               $": {Description}";
    }
}
