using System;
using CodeBrix.Audio.MusicGeneration.Streaming;

namespace CodeBrix.Audio.MusicGeneration.Models;

/// <summary>
/// What a <see cref="MuPTMusicGenerator"/> is built with: the settings that are fixed when the
/// model LOADS, and the defaults it writes with when a request says nothing.
/// </summary>
/// <remarks>
/// <para>
/// THE THREAD COUNT AND THE CONTEXT SIZE ARE LOAD-TIME SETTINGS, and that is the engine's shape
/// rather than this library's choice: a loaded model runs on the threads it was loaded with and
/// keeps a key/value cache the size it was loaded with. So they are here rather than on a request
/// - and a request naming a DIFFERENT thread count RELOADS the model rather than being quietly
/// ignored.
/// </para>
/// <para>
/// THE DEFAULTS ARE DELIBERATELY MODEST. Generation shares the machine with the application, the
/// synthesizer and, in a game, the game loop; a few seconds of silence while music buffers is
/// acceptable and a host that stutters is not.
/// </para>
/// </remarks>
public sealed class MuPTGeneratorOptions
{
    /// <summary>
    /// How many threads the model's arithmetic is spread over unless a caller changes it: A SHARE
    /// OF THIS MACHINE - a quarter of the processors it reports, at least one and never more than
    /// four.
    /// </summary>
    /// <remarks>
    /// IT IS A SHARE RATHER THAN A NUMBER because the conservative answer is different on every
    /// machine: four threads is a sixth of a twenty-four-thread laptop and ALL of a four-core
    /// device. MEASURED on a sixteen-core laptop: four threads write music 23 to 29 times faster
    /// than real time and eight write it 36 to 44 times faster - and there is nothing to spend the
    /// difference on, because generation stops at a thirty-second lead either way.
    /// </remarks>
    public static readonly int DefaultInferenceThreadCount =
        StreamingDefaults.DefaultInferenceThreadCount;

    /// <summary>How many tokens one pass writes unless a caller changes it.</summary>
    /// <remarks>
    /// A pass is a segment of music, and 448 tokens is what the listening sessions that settled
    /// this library's taste were run at: twelve to twenty bars, depending on how many parts the
    /// music has. A longer pass means fewer seams and a slower re-read of the text as it grows.
    /// </remarks>
    public const int DefaultMaximumTokensPerPass = 448;

    /// <summary>How many bars of the music so far are handed to the model as its prompt.</summary>
    /// <remarks>
    /// Enough for a phrase, short enough to leave the model's context for new music and short
    /// enough that the pause at a seam stays small.
    /// </remarks>
    public const int DefaultMaximumPromptBars = 4;

    /// <summary>
    /// How much of the model's context is made room for unless a caller changes it: 2,048 tokens.
    /// </summary>
    /// <remarks>
    /// The model was trained on 8,192 and asking for all of it costs memory a game does not have
    /// to spend: 2,048 tokens is a prompt, a pass and room to spare, and nothing this library does
    /// sends more. A consumer whose own MuPT writes longer pieces raises it.
    /// </remarks>
    public const int DefaultContextTokens = 2048;

    private int inferenceThreadCount = DefaultInferenceThreadCount;
    private int maximumTokensPerPass = DefaultMaximumTokensPerPass;
    private int maximumPromptBars = DefaultMaximumPromptBars;
    private int? contextTokens = DefaultContextTokens;

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
    /// How many tokens one pass writes when a request names no limit of its own through
    /// <see cref="Generation.MusicGenerationControls.MaximumEvents"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaximumTokensPerPass
    {
        get => maximumTokensPerPass;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A pass writes at least one token.");
            }

            maximumTokensPerPass = value;
        }
    }

    /// <summary>
    /// The most bars of the model's own text that a continuation is re-prompted with. The music
    /// before them is not sent: a model's context is for the music it is about to write.
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
    /// How many tokens of context the model is loaded with, or null for the whole context it was
    /// trained on - which costs a great deal more memory and buys nothing this library uses.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? ContextTokens
    {
        get => contextTokens;
        set
        {
            if (value.HasValue && value.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A context is a positive number of tokens, or null for the model's own.");
            }

            contextTokens = value;
        }
    }

    /// <summary>Copies the options, so a later edit cannot change a generator already built.</summary>
    /// <returns>A copy that shares nothing with this one.</returns>
    public MuPTGeneratorOptions Clone() =>
        new MuPTGeneratorOptions
        {
            inferenceThreadCount = inferenceThreadCount,
            maximumTokensPerPass = maximumTokensPerPass,
            maximumPromptBars = maximumPromptBars,
            contextTokens = contextTokens
        };
}
