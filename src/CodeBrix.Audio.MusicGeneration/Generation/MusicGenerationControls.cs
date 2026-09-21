using System;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// How adventurous the generator is allowed to be, and how much it is allowed to write. The
/// defaults are the settings a listening session settled on FOR MUSIC, which are not the settings a
/// text model is usually run with.
/// </summary>
/// <remarks>
/// The repetition penalty is the difference that matters: text generation leans on one to stop a
/// model repeating itself, and music repeats on purpose. The default here is
/// <see cref="NoRepetitionPenalty"/> - none at all.
/// </remarks>
public sealed class MusicGenerationControls
{
    /// <summary>The temperature music is generated at unless a caller changes it.</summary>
    public const double DefaultTemperature = 0.8;

    /// <summary>The top-k music is generated with unless a caller changes it.</summary>
    public const int DefaultTopK = 40;

    /// <summary>The top-p music is generated with unless a caller changes it.</summary>
    public const double DefaultTopP = 0.92;

    /// <summary>The repetition penalty that applies none at all.</summary>
    public const double NoRepetitionPenalty = 1.0;

    private double temperature = DefaultTemperature;
    private int topK = DefaultTopK;
    private double topP = DefaultTopP;
    private double repetitionPenalty = NoRepetitionPenalty;
    private int? maximumEvents;

    /// <summary>
    /// How much the generator is allowed to wander: lower is more predictable, higher is more
    /// surprising. Must be positive.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive, finite number.</exception>
    public double Temperature
    {
        get => temperature;
        set
        {
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The temperature is a positive number.");
            }

            temperature = value;
        }
    }

    /// <summary>
    /// How many candidates the generator chooses between at each step. Zero switches top-k off.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int TopK
    {
        get => topK;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Top-k is a count of candidates, so it cannot be negative. Zero switches it off.");
            }

            topK = value;
        }
    }

    /// <summary>
    /// How much of the probability mass the generator chooses within, from 0 to 1. A value of 1
    /// switches top-p off.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 1.</exception>
    public double TopP
    {
        get => topP;
        set
        {
            if (!(value > 0.0) || value > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Top-p is a share of the probability mass, above 0 and at most 1.");
            }

            topP = value;
        }
    }

    /// <summary>
    /// How strongly the generator is pushed away from repeating itself.
    /// <see cref="NoRepetitionPenalty"/>, the default, applies none - which is what music wants.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive, finite number.</exception>
    public double RepetitionPenalty
    {
        get => repetitionPenalty;
        set
        {
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A repetition penalty is a positive number; 1.0 means none at all.");
            }

            repetitionPenalty = value;
        }
    }

    /// <summary>
    /// The most a generator may write in one segment, counted in whatever unit it generates -
    /// events for a model that writes events, tokens for one that writes text. Null leaves the
    /// limit to the generator, which is what a caller who has no particular length in mind wants.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? MaximumEvents
    {
        get => maximumEvents;
        set
        {
            if (value.HasValue && value.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A maximum event count is a positive number, or null for the generator's own limit.");
            }

            maximumEvents = value;
        }
    }

    /// <summary>
    /// Whether the sampling settings are all at their defaults - which is how a request says it
    /// does not rely on them, so a generator that ignores sampling can still take it. The length
    /// cap is not a sampling setting and is not counted here.
    /// </summary>
    public bool SamplingIsDefault =>
        temperature == DefaultTemperature
        && topK == DefaultTopK
        && topP == DefaultTopP
        && repetitionPenalty == NoRepetitionPenalty;

    /// <summary>Copies the controls, so a caller can vary one request from another.</summary>
    /// <returns>A copy that shares nothing with this one.</returns>
    public MusicGenerationControls Clone() =>
        new MusicGenerationControls
        {
            temperature = temperature,
            topK = topK,
            topP = topP,
            repetitionPenalty = repetitionPenalty,
            maximumEvents = maximumEvents
        };
}
