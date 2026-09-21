using System;
using System.Globalization;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// How ONE part of a piece is voiced: an instrument, how loud it sits, and - for a part that wants
/// thickening - one second instrument layered under it at its own level.
/// </summary>
/// <remarks>
/// <para>
/// A VOICE IS DATA. It carries General MIDI program numbers rather than synthesizers, so the same
/// rendition sounds through a synthesized bank, a SoundFont or a library of sampled instruments,
/// and so a voicing can be retuned after a listening session without an API change.
/// </para>
/// <para>
/// GAIN IS NOT OPTIONAL. Instrument libraries differ enormously in level - the same arrangement
/// that balances through one bank has an inaudible part through another - so every voice states
/// its own gain, and no built-in voicing leaves a part at unity by accident.
/// </para>
/// <para>
/// ONE LAYER AT MOST, and it is for a part that is alone. Layering two instruments on BOTH parts of
/// a two-part piece was rated well below giving each part one distinct voice; layering helped only
/// where a single sparse part had nothing underneath it.
/// </para>
/// </remarks>
public sealed class RenditionVoice
{
    /// <summary>
    /// The gain a voice takes when none is given: a little below unity, so that a part is never
    /// left at full level merely because nobody thought about it.
    /// </summary>
    public const float DefaultGain = 0.8F;

    private int program;
    private float gain = DefaultGain;
    private int? layerProgram;
    private float layerGain = DefaultGain;

    /// <summary>Creates a voice on one instrument at the default gain.</summary>
    /// <param name="program">The General MIDI program, 0 to 127.</param>
    /// <exception cref="ArgumentOutOfRangeException">The program is outside 0 to 127.</exception>
    public RenditionVoice(int program)
        : this(program, DefaultGain)
    {
    }

    /// <summary>Creates a voice on one instrument.</summary>
    /// <param name="program">The General MIDI program, 0 to 127.</param>
    /// <param name="gain">How loud the part sits, where 1 is unity.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The program is outside 0 to 127, or the gain is negative or not a finite number.
    /// </exception>
    public RenditionVoice(int program, float gain)
    {
        Program = program;
        Gain = gain;
    }

    /// <summary>Creates a voice with a second instrument layered under it.</summary>
    /// <param name="program">The General MIDI program of the instrument in front, 0 to 127.</param>
    /// <param name="gain">How loud the instrument in front sits.</param>
    /// <param name="layerProgram">The General MIDI program of the layer, 0 to 127.</param>
    /// <param name="layerGain">How loud the layer sits, usually well below the instrument in front.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A program is outside 0 to 127, or a gain is negative or not a finite number.
    /// </exception>
    public RenditionVoice(int program, float gain, int layerProgram, float layerGain)
        : this(program, gain)
    {
        LayerProgram = layerProgram;
        LayerGain = layerGain;
    }

    /// <summary>Creates a voice on one named General MIDI instrument.</summary>
    /// <param name="program">The General MIDI instrument.</param>
    /// <param name="gain">How loud the part sits, where 1 is unity.</param>
    /// <exception cref="ArgumentOutOfRangeException">The gain is negative or not a finite number.</exception>
    public RenditionVoice(GeneralMidiProgram program, float gain)
        : this((int)program, gain)
    {
    }

    /// <summary>Creates a voice on named General MIDI instruments, one layered under the other.</summary>
    /// <param name="program">The instrument in front.</param>
    /// <param name="gain">How loud the instrument in front sits.</param>
    /// <param name="layerProgram">The instrument layered under it.</param>
    /// <param name="layerGain">How loud the layer sits.</param>
    /// <exception cref="ArgumentOutOfRangeException">A gain is negative or not a finite number.</exception>
    public RenditionVoice(GeneralMidiProgram program, float gain, GeneralMidiProgram layerProgram,
        float layerGain)
        : this((int)program, gain, (int)layerProgram, layerGain)
    {
    }

    /// <summary>The General MIDI program of the instrument in front, 0 to 127.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 127.</exception>
    public int Program
    {
        get => program;
        set
        {
            RequireProgram(value, nameof(value));
            program = value;
        }
    }

    /// <summary>How loud the instrument in front sits, where 1 is unity.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public float Gain
    {
        get => gain;
        set
        {
            RequireGain(value, nameof(value));
            gain = value;
        }
    }

    /// <summary>
    /// The General MIDI program layered under the instrument in front, or null when the part is
    /// voiced with one instrument alone.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 127.</exception>
    public int? LayerProgram
    {
        get => layerProgram;
        set
        {
            if (value.HasValue)
            {
                RequireProgram(value.Value, nameof(value));
            }

            layerProgram = value;
        }
    }

    /// <summary>How loud the layer sits. It is ignored when there is no layer.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public float LayerGain
    {
        get => layerGain;
        set
        {
            RequireGain(value, nameof(value));
            layerGain = value;
        }
    }

    /// <summary>Whether a second instrument is layered under the instrument in front.</summary>
    public bool HasLayer => layerProgram.HasValue;

    /// <summary>Copies the voice, so that changing one rendition cannot change another.</summary>
    /// <returns>The copy.</returns>
    public RenditionVoice Clone() =>
        new RenditionVoice(program, gain)
        {
            layerProgram = layerProgram,
            layerGain = layerGain
        };

    /// <summary>Describes the voice for a diagnostic or a test failure.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() =>
        layerProgram.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "{0} at {1}, with {2} under it at {3}",
                NameOf(program), gain, NameOf(layerProgram.Value), layerGain)
            : string.Format(CultureInfo.InvariantCulture, "{0} at {1}", NameOf(program), gain);

    internal static string NameOf(int program) =>
        string.Format(CultureInfo.InvariantCulture, "program {0} ({1})", program,
            GeneralMidi.DisplayName((GeneralMidiProgram)program));

    private static void RequireProgram(int value, string parameterName)
    {
        if (value < 0 || value > 127)
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                "A General MIDI program is 0 to 127.");
        }
    }

    private static void RequireGain(float value, string parameterName)
    {
        if (!(value >= 0.0F) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                "A gain is a non-negative, finite multiple of the part's own level, where 1 is unity.");
        }
    }
}
