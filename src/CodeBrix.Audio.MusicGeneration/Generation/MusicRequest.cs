using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// Everything a caller can say about the music they want. There is ONE request type for every
/// generator, and every part of it is optional - a request with nothing set is a perfectly good
/// request, and is what the zero-configuration path sends.
/// </summary>
/// <remarks>
/// <para>
/// WHAT A GENERATOR DOES WITH IT is the generator's business: a text model reads
/// <see cref="ModelNativeText"/> as its own notation and turns <see cref="Intent"/> into a header,
/// an event model maps the intent onto its own options and takes <see cref="Primer"/> as a piece to
/// start from. What no generator does is quietly ignore a part it cannot act on: every generator
/// declares what it honours through <see cref="IMusicGenerator.Honours"/>, and
/// <see cref="MusicGeneratorCapabilities"/> turns a request that asks for more into an error that
/// names each part by name.
/// </para>
/// <para>
/// TWO PARTS ARE NOT CAPABILITIES, because they are not a reason to refuse anything:
/// <see cref="TicksPerQuarterNote"/>, which every generator writes its ticks at, and
/// <see cref="PaceInRealTime"/>, which a generator that paces itself honours and one that already
/// runs flat out ignores.
/// </para>
/// </remarks>
public sealed class MusicRequest
{
    /// <summary>
    /// The tick resolution music is generated at unless a caller changes it: 480 ticks to the
    /// quarter note, which is what most MIDI writing software uses.
    /// </summary>
    public const int DefaultTicksPerQuarterNote = 480;

    /// <summary>
    /// The coarsest resolution a request may name. It is 1 tick to the quarter note, which is
    /// useless for music but is not an error in itself.
    /// </summary>
    public const int MinimumTicksPerQuarterNote = 1;

    /// <summary>
    /// The finest resolution a request may name: 32,767 ticks to the quarter note, which is what a
    /// standard MIDI file's header can carry.
    /// </summary>
    public const int MaximumTicksPerQuarterNote = 32767;

    private readonly List<GeneralMidiProgram> instrumentHints = new List<GeneralMidiProgram>();

    private int ticksPerQuarterNote = DefaultTicksPerQuarterNote;
    private int? inferenceThreadCount;
    private MusicGenerationControls controls = new MusicGenerationControls();

    /// <summary>
    /// What the music should be like, in plain words - "something calm for a forest at dusk".
    /// Null or blank when there is nothing to say.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Text in the generator's own notation, handed over verbatim - ABC for a generator that reads
    /// ABC. It is the escape hatch for a caller who knows exactly what they want and would rather
    /// write it than describe it. Null or blank when there is none.
    /// </summary>
    public string ModelNativeText { get; set; }

    /// <summary>
    /// A piece of MIDI to start from, as CodeBrix.Audio writes MIDI. A generator that takes one
    /// generates music that follows it. Null when there is none.
    /// </summary>
    public MidiEventCollection Primer { get; set; }

    /// <summary>What the music should be, in musical terms. Null when nothing is specified.</summary>
    public MusicIntent Intent { get; set; }

    /// <summary>
    /// The instruments the music should use, as General MIDI programs. They are hints: a generator
    /// that honours them writes for those sounds, and the rendition decides what actually plays
    /// them. Empty when there is nothing to say.
    /// </summary>
    public IList<GeneralMidiProgram> InstrumentHints => instrumentHints;

    /// <summary>
    /// The seed the generator starts from, so the same request produces the same music twice. Null
    /// for a fresh seed each time. A continuation wants a DIFFERENT seed from the segment before
    /// it, because the same seed writes the same music again.
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>
    /// How adventurous the generator may be, and how much it may write. Never null - a request
    /// always carries controls, at their music defaults until a caller changes them.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public MusicGenerationControls Controls
    {
        get => controls;
        set => controls = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// How many threads the generator may run inference on. Null means the generator's own
    /// conservative default, which is the right answer for an application sharing a machine with
    /// anything else - a game, above all.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? InferenceThreadCount
    {
        get => inferenceThreadCount;
        set
        {
            if (value.HasValue && value.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A thread count is a positive number, or null for the generator's own default.");
            }

            inferenceThreadCount = value;
        }
    }

    /// <summary>
    /// The tick resolution the generated ticks are at. THE CALLER FIXES IT, not the generator,
    /// because several segments - possibly from several generators - land on one timeline and a
    /// timeline has one resolution.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is outside <see cref="MinimumTicksPerQuarterNote"/> to
    /// <see cref="MaximumTicksPerQuarterNote"/>.
    /// </exception>
    public int TicksPerQuarterNote
    {
        get => ticksPerQuarterNote;
        set
        {
            if (value < MinimumTicksPerQuarterNote || value > MaximumTicksPerQuarterNote)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A tick resolution is a number of ticks per quarter note between " +
                    $"{MinimumTicksPerQuarterNote} and {MaximumTicksPerQuarterNote}, which is what " +
                    "a standard MIDI file's header can carry.");
            }

            ticksPerQuarterNote = value;
        }
    }

    /// <summary>
    /// Whether a generator that paces itself should release its music in real time, which is the
    /// default. Turning it off asks for the whole segment as fast as the caller can pull it, which
    /// is what an offline render wants - rendering to a file has no play head to keep up with.
    /// </summary>
    /// <remarks>
    /// Honoured by generators that pace themselves; ignored by those that do not, because a model
    /// generating as fast as it can is already as fast as it goes. Everything else about the output
    /// is unchanged either way: the same events, at the same ticks, in the same order, with the
    /// same settled ticks.
    /// </remarks>
    public bool PaceInRealTime { get; set; } = true;

    /// <summary>
    /// What the music has been doing, when this request continues an earlier segment. Null when the
    /// music starts here.
    /// </summary>
    public MusicContinuation Continuation { get; set; }

    /// <summary>
    /// Copies the request, so a session can vary one segment from the last without the generator
    /// seeing a request change under it.
    /// </summary>
    /// <returns>
    /// A copy. The MIDI primer and continuation tail are shared, because they are read and never
    /// modified; every collection the request owns is copied.
    /// </returns>
    public MusicRequest Clone()
    {
        var copy = new MusicRequest
        {
            Text = Text,
            ModelNativeText = ModelNativeText,
            Primer = Primer,
            Intent = Intent == null ? null : Intent.Clone(),
            Seed = Seed,
            controls = controls.Clone(),
            inferenceThreadCount = inferenceThreadCount,
            ticksPerQuarterNote = ticksPerQuarterNote,
            PaceInRealTime = PaceInRealTime,
            Continuation = Continuation == null ? null : Continuation.Clone()
        };

        copy.instrumentHints.AddRange(instrumentHints);

        return copy;
    }
}
