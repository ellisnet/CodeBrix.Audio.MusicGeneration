using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// What one or more character words mean in terms a model can actually be asked for: a tempo, a
/// mode, or both.
/// </summary>
/// <remarks>
/// NEITHER MODEL FAMILY READS PROSE, so a character word is worth having only where it names
/// something musical. A word never names an instrument here - what plays a part is the rendition's
/// business, not the generator's - and a word this library does not know is refused by name rather
/// than dropped.
/// </remarks>
public sealed class MusicCharacter
{
    private readonly string[] words;

    internal MusicCharacter(string[] words, double? beatsPerMinute, MusicMode? mode)
    {
        this.words = words;
        BeatsPerMinute = beatsPerMinute;
        Mode = mode;
    }

    /// <summary>The words this was read from, in the order they were given.</summary>
    public IReadOnlyList<string> Words => words;

    /// <summary>
    /// The tempo the words name, in quarter notes per minute, or null when none of them names one.
    /// </summary>
    public double? BeatsPerMinute { get; }

    /// <summary>The mode the words name, or null when none of them names one.</summary>
    public MusicMode? Mode { get; }

    /// <summary>A line a diagnostic can write straight out.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        var tempo = BeatsPerMinute.HasValue
            ? BeatsPerMinute.Value.ToString("0.###", CultureInfo.InvariantCulture) + " bpm"
            : "no tempo";

        var mode = Mode.HasValue ? Mode.Value.ToString() : "no mode";

        return string.Join(", ", words) + ": " + tempo + ", " + mode;
    }
}
