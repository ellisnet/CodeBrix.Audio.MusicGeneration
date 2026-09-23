using System;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.SkyTNT;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The SkyTNT model, built ONCE for a whole test class and released when it is finished with.
/// </summary>
/// <remarks>
/// LOADING IS THE EXPENSIVE PART. xUnit builds a test class once per test, so a generator built in
/// the constructor would load the model again for every test in it; a class fixture is built once.
/// The published SkyTNT package supplies the copied bundle; these short tests run in the ordinary suite.
/// </remarks>
public sealed class SkyTNTLoadedModel : IDisposable
{
    /// <summary>How many events one pass of a live test writes: a few dozen, and no more.</summary>
    public const int ShortPass = 60;

    /// <summary>Builds the generator, without loading anything.</summary>
    public SkyTNTLoadedModel() =>
        Generator = new SkyTNTMusicGenerator(SkyTNTLiveTests.Name, SkyTNTModel.ModelDirectory, Options());

    /// <summary>The generator backed by the published model package's copied bundle.</summary>
    public SkyTNTMusicGenerator Generator { get; }

    /// <summary>The settings every live test's generator is built with.</summary>
    /// <returns>The settings.</returns>
    public static SkyTNTGeneratorOptions Options() =>
        new SkyTNTGeneratorOptions { MaximumEventsPerPass = ShortPass, DrumKit = null };

    /// <summary>Gives the model's memory back when the class is finished with it.</summary>
    public void Dispose() => Generator?.Dispose();
}
