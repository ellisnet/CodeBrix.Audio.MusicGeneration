namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>One row of the taste table: an instrument, the level it sits at, and why it is there.</summary>
internal readonly struct TasteEntry
{
    /// <summary>Creates a row.</summary>
    /// <param name="program">The General MIDI program, 0 to 127.</param>
    /// <param name="gain">The level the instrument sits at.</param>
    /// <param name="rating">The rating the row rests on, as a voicing diagnostic prints it.</param>
    public TasteEntry(int program, float gain, string rating)
    {
        Program = program;
        Gain = gain;
        Rating = rating;
    }

    /// <summary>The General MIDI program.</summary>
    public int Program { get; }

    /// <summary>The level the instrument sits at, where 1 is unity.</summary>
    public float Gain { get; }

    /// <summary>The rating this row rests on.</summary>
    public string Rating { get; }
}
