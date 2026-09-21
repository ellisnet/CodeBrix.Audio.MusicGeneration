namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// The mode a piece is in, beside its tonic. The set is the one music notation uses, so it maps
/// onto ABC's <c>K:</c> field and onto a MIDI key signature alike.
/// </summary>
public enum MusicMode
{
    /// <summary>The major scale. Ionian by another name.</summary>
    Major = 0,

    /// <summary>The natural minor scale. Aeolian by another name.</summary>
    Minor = 1,

    /// <summary>Ionian - the major scale under its modal name.</summary>
    Ionian = 2,

    /// <summary>Dorian: a minor scale with a raised sixth.</summary>
    Dorian = 3,

    /// <summary>Phrygian: a minor scale with a flattened second.</summary>
    Phrygian = 4,

    /// <summary>Lydian: a major scale with a raised fourth.</summary>
    Lydian = 5,

    /// <summary>Mixolydian: a major scale with a flattened seventh.</summary>
    Mixolydian = 6,

    /// <summary>Aeolian - the natural minor scale under its modal name.</summary>
    Aeolian = 7,

    /// <summary>Locrian: a minor scale with a flattened second and a flattened fifth.</summary>
    Locrian = 8,

    /// <summary>No key at all - accidentals are written as they occur.</summary>
    None = 9
}
