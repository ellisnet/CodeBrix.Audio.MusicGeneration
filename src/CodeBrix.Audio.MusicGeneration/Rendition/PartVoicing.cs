using System.Globalization;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// What ONE part of the music is actually being played with - the channel it is on, the instrument
/// it got, the level it sits at, and where that choice came from.
/// </summary>
/// <remarks>
/// It is a snapshot of a decision already taken, not a way of taking one: a session builds these
/// as the music's parts first sound, and hands them out so that a developer can see the voicing
/// rather than guess at it.
/// </remarks>
public sealed class PartVoicing
{
    internal PartVoicing(int channel, bool isPercussion, int program, int requestedProgram,
        float gain, int? layerProgram, float layerGain, VoicingSource source, string note)
    {
        Channel = channel;
        IsPercussion = isPercussion;
        Program = program;
        RequestedProgram = requestedProgram;
        Gain = gain;
        LayerProgram = layerProgram;
        LayerGain = layerGain;
        Source = source;
        Note = note;
    }

    /// <summary>The MIDI channel the part is on, 1 to 16.</summary>
    public int Channel { get; }

    /// <summary>Whether this is the percussion part rather than a melodic one.</summary>
    public bool IsPercussion { get; }

    /// <summary>
    /// The General MIDI program actually sounding, 0 to 127. It is meaningless for the percussion
    /// part, which is the library's kit.
    /// </summary>
    public int Program { get; }

    /// <summary>
    /// The program that was asked for. It differs from <see cref="Program"/> only when the
    /// instrument library does not cover what was asked for and something covered was used instead.
    /// </summary>
    public int RequestedProgram { get; }

    /// <summary>Whether the instrument library could not play what was asked for.</summary>
    public bool WasReplacedForCoverage => !IsPercussion && Program != RequestedProgram;

    /// <summary>The level this part sits at, where 1 is unity.</summary>
    public float Gain { get; }

    /// <summary>The program layered under this part, or null when nothing is layered under it.</summary>
    public int? LayerProgram { get; }

    /// <summary>The level the layer sits at. It means nothing when there is no layer.</summary>
    public float LayerGain { get; }

    /// <summary>Whether a second instrument is layered under this part.</summary>
    public bool HasLayer => LayerProgram.HasValue;

    /// <summary>Where the choice of instrument came from.</summary>
    public VoicingSource Source { get; }

    /// <summary>
    /// A sentence saying why this part sounds the way it does - the rating a taste-table voicing
    /// rests on, or the reason something else was used. Never null; empty when there is nothing to add.
    /// </summary>
    public string Note { get; }

    /// <summary>Describes the part for a diagnostic or a test failure.</summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        if (IsPercussion)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "channel {0}: the percussion kit at {1}", Channel, Gain);
        }

        return LayerProgram.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "channel {0}: {1} at {2}, with {3} under it at {4} ({5})",
                Channel, RenditionVoice.NameOf(Program), Gain, RenditionVoice.NameOf(LayerProgram.Value),
                LayerGain, Source)
            : string.Format(CultureInfo.InvariantCulture, "channel {0}: {1} at {2} ({3})",
                Channel, RenditionVoice.NameOf(Program), Gain, Source);
    }

    internal static string DisplayNameOf(int program) =>
        GeneralMidi.DisplayName((GeneralMidiProgram)program);
}
