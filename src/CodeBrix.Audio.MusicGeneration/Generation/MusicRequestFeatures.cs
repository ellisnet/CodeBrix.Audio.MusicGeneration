using System;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// The parts of a <see cref="MusicRequest"/> a generator can act on. A generator declares the set
/// it honours through <see cref="IMusicGenerator.Honours"/>, and a request that relies on anything
/// outside that set is refused by name rather than quietly generating something else.
/// </summary>
/// <remarks>
/// <para>
/// Two things are deliberately absent, because every generator has to deal with them and so they
/// are never a reason to refuse a request:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="MusicRequest.TicksPerQuarterNote"/> - the caller fixes the tick resolution, and
/// generated ticks are always at it.
/// </description></item>
/// <item><description>
/// <see cref="MusicRequest.PaceInRealTime"/> - a generator that paces itself honours it, and one
/// that already runs flat out ignores it.
/// </description></item>
/// </list>
/// </remarks>
[Flags]
public enum MusicRequestFeatures
{
    /// <summary>Nothing beyond the tick resolution: the generator plays what it has.</summary>
    None = 0,

    /// <summary>Free natural-language text, <see cref="MusicRequest.Text"/>.</summary>
    FreeText = 1 << 0,

    /// <summary>Text in the generator's own notation, <see cref="MusicRequest.ModelNativeText"/>.</summary>
    ModelNativeText = 1 << 1,

    /// <summary>A piece of MIDI to start from, <see cref="MusicRequest.Primer"/>.</summary>
    Primer = 1 << 2,

    /// <summary>The key and mode of <see cref="MusicIntent"/>.</summary>
    Key = 1 << 3,

    /// <summary>The metre of <see cref="MusicIntent"/>.</summary>
    Meter = 1 << 4,

    /// <summary>The unit note length of <see cref="MusicIntent"/>.</summary>
    UnitNoteLength = 1 << 5,

    /// <summary>The tempo of <see cref="MusicIntent"/>.</summary>
    Tempo = 1 << 6,

    /// <summary>The character words of <see cref="MusicIntent"/>.</summary>
    CharacterWords = 1 << 7,

    /// <summary>The voice count of <see cref="MusicIntent"/>.</summary>
    VoiceCount = 1 << 8,

    /// <summary>The target length of <see cref="MusicIntent"/>.</summary>
    TargetLength = 1 << 9,

    /// <summary>The instrument hints, <see cref="MusicRequest.InstrumentHints"/>.</summary>
    InstrumentHints = 1 << 10,

    /// <summary>The seed, <see cref="MusicRequest.Seed"/>.</summary>
    Seed = 1 << 11,

    /// <summary>
    /// The sampling settings of <see cref="MusicGenerationControls"/> - temperature, top-k, top-p
    /// and the repetition penalty. A request that leaves them at their documented defaults does not
    /// rely on them, so a generator that ignores sampling still takes it.
    /// </summary>
    SamplingControls = 1 << 12,

    /// <summary>The length cap, <see cref="MusicGenerationControls.MaximumEvents"/>.</summary>
    MaximumEvents = 1 << 13,

    /// <summary>The thread count, <see cref="MusicRequest.InferenceThreadCount"/>.</summary>
    InferenceThreadCount = 1 << 14,

    /// <summary>The continuation context, <see cref="MusicRequest.Continuation"/>.</summary>
    Continuation = 1 << 15,

    /// <summary>
    /// The drum kit of <see cref="MusicIntent"/>. It is its own part of a request because a drum
    /// kit is not a General MIDI program, so the instrument hints cannot carry it - and because a
    /// generator that writes notation has no percussion to give.
    /// </summary>
    DrumKit = 1 << 16
}
