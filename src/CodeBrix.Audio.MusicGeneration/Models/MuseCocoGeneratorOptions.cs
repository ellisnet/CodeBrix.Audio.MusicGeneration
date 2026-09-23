using System;
using CodeBrix.Audio.MusicGeneration.Streaming;

namespace CodeBrix.Audio.MusicGeneration.Models;

/// <summary>Load settings and per-pass token limits for a staged MuseCoco music model.</summary>
public sealed class MuseCocoGeneratorOptions
{
    private int inferenceThreadCount = StreamingDefaults.DefaultInferenceThreadCount;
    private int maximumTokensPerPass = 2560;
    private int minimumTokensPerPass = 512;
    private int experimentalSectionTokens = 512;
    private int experimentalContextBars = 4;

    /// <summary>Threads used for managed inference, unless a request overrides the count.</summary>
    public int InferenceThreadCount
    {
        get => inferenceThreadCount;
        set => inferenceThreadCount = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Maximum REMIGEN2 tokens per pass, excluding attributes; default 2,560.</summary>
    public int MaximumTokensPerPass
    {
        get => maximumTokensPerPass;
        set => maximumTokensPerPass = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Tokens generated before the model may finish; default 512. Clamped to the effective maximum
    /// when a request asks for a shorter pass. Zero permits immediate end-of-sequence.
    /// </summary>
    public int MinimumTokensPerPass
    {
        get => minimumTokensPerPass;
        set => minimumTokensPerPass = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Optional staged text-model directory. It is loaded only for a request with natural-language
    /// text. Attribute-only requests and preloading load only the music model.
    /// </summary>
    public string TextBundleDirectory { get; set; }

    /// <summary>
    /// EXPERIMENTAL: continue successive sections using recent musical context within one request.
    /// False by default. The request's total token cap still applies, but can exceed the model's
    /// position capacity because each section replays a bounded context with fresh recurrent state.
    /// Musical transitions and long-form quality require listening evaluation.
    /// </summary>
    /// <remarks>
    /// Context belongs to one GenerateAsync enumeration. A new request, including a follow-up,
    /// starts fresh. Cancellation or early disposal discards the context. Set MinimumTokensPerPass
    /// to zero to allow natural section endings; an empty response ends the request.
    /// </remarks>
    public bool ExperimentalContinuation { get; set; }

    /// <summary>
    /// EXPERIMENTAL: maximum new tokens in each continuation section, default 512. The effective
    /// cap also respects the request's remaining tokens and the space after replayed context.
    /// </summary>
    public int ExperimentalSectionTokens
    {
        get => experimentalSectionTokens;
        set => experimentalSectionTokens = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>EXPERIMENTAL: recent bars replayed before each later section, from 1 to 16; default 4.</summary>
    public int ExperimentalContextBars
    {
        get => experimentalContextBars;
        set => experimentalContextBars = value >= 1 && value <= 16
            ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Copies settings so edits do not affect a generator already constructed.</summary>
    /// <returns>An independent copy.</returns>
    public MuseCocoGeneratorOptions Clone() => (MuseCocoGeneratorOptions)MemberwiseClone();
}
