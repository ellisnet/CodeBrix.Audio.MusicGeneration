using System;
using CodeBrix.Audio.MusicGeneration.Models;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The MuPT model, built ONCE for a whole test class and released when it is finished with.
/// </summary>
/// <remarks>
/// LOADING IS THE EXPENSIVE PART. xUnit builds a test class once per test, so a generator built in
/// the constructor would load the model again for every test in it; a class fixture is built once.
/// The generator is null when the model variable is not set, and every test in the class skips.
/// </remarks>
public sealed class MuPTLoadedModel : IDisposable
{
    /// <summary>How many tokens one pass of a live test writes: a few bars, and no more.</summary>
    public const int ShortPass = 96;

    /// <summary>Builds the generator, without loading anything.</summary>
    public MuPTLoadedModel() =>
        Generator = MuPTModelFile.IsAvailable
            ? new MuPTMusicGenerator(MuPTLiveTests.Name, MuPTModelFile.Path, Options())
            : null;

    /// <summary>The generator, or null when there is no model to point it at.</summary>
    public MuPTMusicGenerator Generator { get; }

    /// <summary>The settings every live test's generator is built with.</summary>
    /// <returns>The settings.</returns>
    public static MuPTGeneratorOptions Options() =>
        new MuPTGeneratorOptions { MaximumTokensPerPass = ShortPass };

    /// <summary>Gives the model's memory back when the class is finished with it.</summary>
    public void Dispose() => Generator?.Dispose();
}
