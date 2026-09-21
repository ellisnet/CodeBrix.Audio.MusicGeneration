using System;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// One request, turned into what the model runner is asked for: how many tokens, and how each one
/// is chosen.
/// </summary>
/// <remarks>
/// <para>
/// THE SAMPLING SETTINGS MEAN THE SAME THING ON BOTH SIDES, which is why they go straight across:
/// a top-k of zero switches top-k off here and there, a top-p of 1 switches top-p off here and
/// there, and a repetition penalty of 1 applies none here and there. THE DEFAULTS ARE THE MUSIC
/// ONES - temperature 0.8, top-k 40, top-p 0.92 and NO repetition penalty - which is what the
/// listening sessions this library's taste rests on were run at.
/// </para>
/// <para>
/// A LENGTH CAP COUNTS TOKENS, not events: this model writes text, and the request type says as
/// much.
/// </para>
/// </remarks>
internal static class MuPTRequestMapper
{
    /// <summary>The seed value the engine reads as "draw a random one", which no request can mean.</summary>
    public const uint EngineRandomSeed = 0xFFFFFFFFu;

    /// <summary>Turns a request into the model runner's own generation options.</summary>
    /// <param name="request">The request.</param>
    /// <param name="options">The generator's own settings.</param>
    /// <param name="generatorName">The generator's name, for a refusal that names it.</param>
    /// <returns>The options to generate with.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for a seed this model runner cannot hold.
    /// </exception>
    public static GenerationOptions ToGenerationOptions(MusicRequest request,
        MuPTGeneratorOptions options, string generatorName)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var controls = request.Controls;

        return new GenerationOptions
        {
            MaxTokens = controls.MaximumEvents ?? options.MaximumTokensPerPass,
            Sampling = new SamplingOptions
            {
                Temperature = (float)controls.Temperature,
                TopK = controls.TopK,
                TopP = (float)controls.TopP,
                RepeatPenalty = (float)controls.RepetitionPenalty,
                Seed = SeedOf(request, generatorName)
            }
        };
    }

    private static uint? SeedOf(MusicRequest request, string generatorName)
    {
        if (!request.Seed.HasValue)
        {
            return null;
        }

        var seed = unchecked((uint)request.Seed.Value);

        if (seed == EngineRandomSeed)
        {
            // -1 IS THE ONE SEED THAT CANNOT BE HONOURED: the engine reads that bit pattern as its
            // own "draw a random seed" marker, so a piece asked for with it would not repeat.
            // Saying so is better than handing back music that is different every time.
            throw new MusicRequestNotHonouredException(
                $"The music generator '{generatorName}' cannot use the seed -1: the engine " +
                "underneath reads that value as its own marker for 'draw a random seed', so the " +
                "music would not be the same twice. Any other seed works.",
                generatorName, MusicRequestFeatures.Seed);
        }

        return seed;
    }
}
