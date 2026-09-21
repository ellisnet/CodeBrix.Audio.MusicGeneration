using System;
using CodeBrix.Audio.MusicGeneration.Streaming;

namespace CodeBrix.Audio.MusicGeneration.Models;

/// <summary>
/// What a <see cref="SkyTNTMusicGenerator"/> is built with: the settings that are fixed when the
/// model LOADS, and the defaults it writes with when a request says nothing.
/// </summary>
/// <remarks>
/// <para>
/// THE THREAD COUNT IS A LOAD-TIME SETTING and that is not this library's choice: the graphs are
/// loaded with the number of threads their arithmetic is spread over, and a loaded graph cannot
/// change it. So it is here rather than on a request - and a request that names a DIFFERENT count
/// reloads the model rather than being quietly ignored.
/// </para>
/// <para>
/// THE DEFAULTS ARE DELIBERATELY MODEST. Generation shares the machine with the application, the
/// synthesizer and, in a game, the game loop; a few seconds of silence while music buffers is
/// acceptable and a host that stutters is not.
/// </para>
/// </remarks>
public sealed class SkyTNTGeneratorOptions
{
    /// <summary>
    /// How many threads the model's arithmetic is spread over unless a caller changes it: A SHARE
    /// OF THIS MACHINE - a quarter of the processors it reports, at least one and never more than
    /// four.
    /// </summary>
    /// <remarks>
    /// IT IS A SHARE RATHER THAN A NUMBER because the conservative answer is different on every
    /// machine: four threads is a sixth of a twenty-four-thread laptop and ALL of a four-core
    /// device. MEASURED on a sixteen-core laptop: the rate climbs steeply from one thread to four
    /// - 1.9, 3.0, 4.2 times real time - and then flattens, with eight threads inside the
    /// run-to-run spread of four. So four is where the speed stops being worth the cores, and a
    /// quarter of the machine is what is left to the application.
    /// </remarks>
    public static readonly int DefaultInferenceThreadCount =
        StreamingDefaults.DefaultInferenceThreadCount;

    /// <summary>How many events one pass writes unless a caller changes it.</summary>
    /// <remarks>
    /// A pass is a segment of music, and the choice is a trade: a longer pass means fewer seams,
    /// and this model slows down as its context grows, so a longer pass also means a lower
    /// real-time factor by the end of it. MEASURED on a sixteen-core laptop at four threads: the
    /// opening 250 events are written at 3.2 to 3.7 times real time, the thousandth at 1.3 to
    /// 1.45, and the two-thousandth at 0.7 to 0.8 - BELOW real time, which would engage the
    /// segment-at-a-time fallback. A seam resets the rate, so a shorter pass is faster on average;
    /// a thousand events is about a hundred seconds of music and is the last round number still
    /// comfortably above real time.
    /// </remarks>
    public const int DefaultMaximumEventsPerPass = 1000;

    /// <summary>How many bars of the music so far are handed to the model as its prompt.</summary>
    /// <remarks>
    /// Enough for a phrase, short enough to leave the model's context for new music and short
    /// enough that the pause at a seam stays small. The engine's own tail is this long too, so the
    /// number here only ever shortens what it is given.
    /// </remarks>
    public const int DefaultMaximumPromptBars = 4;

    private int inferenceThreadCount = DefaultInferenceThreadCount;
    private int maximumEventsPerPass = DefaultMaximumEventsPerPass;
    private int maximumPromptBars = DefaultMaximumPromptBars;

    /// <summary>
    /// How many threads the model's arithmetic is spread over. It is settled when the model loads.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int InferenceThreadCount
    {
        get => inferenceThreadCount;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A thread count is a positive number of threads.");
            }

            inferenceThreadCount = value;
        }
    }

    /// <summary>
    /// How many events one pass writes when a request names no limit of its own through
    /// <see cref="Generation.MusicGenerationControls.MaximumEvents"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaximumEventsPerPass
    {
        get => maximumEventsPerPass;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A pass writes at least one event.");
            }

            maximumEventsPerPass = value;
        }
    }

    /// <summary>
    /// The most bars of the tail of a continuation that are handed to the model. A tail longer
    /// than this is cut back to its LAST bars, which are the ones the new music follows on from.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaximumPromptBars
    {
        get => maximumPromptBars;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A prompt is at least one bar of music.");
            }

            maximumPromptBars = value;
        }
    }

    /// <summary>
    /// The drum kit the music is written with, as the program the percussion channel takes - 0 for
    /// the standard kit, 8 room, 16 power, 24 and 25 electronic, 32 jazz, 40 brushes, 48
    /// orchestral. Null, the default, asks for no percussion.
    /// </summary>
    /// <remarks>
    /// IT IS THE GENERATOR'S DEFAULT, AND A REQUEST OVERRIDES IT. A request says which kit it
    /// wants through <see cref="Generation.MusicIntent.DrumKit"/>, and
    /// <see cref="Generation.MusicIntent.NoDrumKit"/> asks for no percussion at all; music already
    /// under way says it through the continuation's own program for channel 10. This setting is
    /// what a request that says nothing gets.
    /// </remarks>
    public int? DrumKit { get; set; }

    /// <summary>
    /// Whether the model may write controller changes - pedals, volume, pan. It is OFF by default.
    /// </summary>
    /// <remarks>
    /// MEASURED, AND THE REASON IT IS OFF: with controller events allowed, this model can spend a
    /// WHOLE PASS writing them at tick 0 - modulation, expression, registered-parameter pairs -
    /// and write no notes at all. One measurement produced 250 events inside the first bar, every
    /// one a control change; the same request with this switched off produced 219 events over
    /// twelve seconds of music. A pass of no music is much worse than a pass without a pedal, so
    /// the default protects the music. Switch it on for a piece that wants the expression, and
    /// expect some of the event budget to go on it.
    /// </remarks>
    public bool AllowControlChange { get; set; }

    /// <summary>Copies the options, so a later edit cannot change a generator already built.</summary>
    /// <returns>A copy that shares nothing with this one.</returns>
    public SkyTNTGeneratorOptions Clone() =>
        new SkyTNTGeneratorOptions
        {
            inferenceThreadCount = inferenceThreadCount,
            maximumEventsPerPass = maximumEventsPerPass,
            maximumPromptBars = maximumPromptBars,
            DrumKit = DrumKit,
            AllowControlChange = AllowControlChange
        };
}
