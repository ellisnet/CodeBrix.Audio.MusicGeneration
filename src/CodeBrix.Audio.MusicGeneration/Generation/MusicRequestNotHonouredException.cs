using System;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// A request asked a generator for something it does not do. The generator is named, and so is
/// every part of the request it will not act on - nothing is ever quietly ignored.
/// </summary>
public class MusicRequestNotHonouredException : MusicGenerationException
{
    /// <summary>Creates the exception with the default message.</summary>
    public MusicRequestNotHonouredException()
        : base("The music generator does not honour part of the request.")
    {
        GeneratorName = string.Empty;
        UnhonouredFeatures = MusicRequestFeatures.None;
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What the generator will not act on.</param>
    public MusicRequestNotHonouredException(string message)
        : base(message)
    {
        GeneratorName = string.Empty;
        UnhonouredFeatures = MusicRequestFeatures.None;
    }

    /// <summary>Creates the exception with a message and the error that caused it.</summary>
    /// <param name="message">What the generator will not act on.</param>
    /// <param name="innerException">The error underneath this one.</param>
    public MusicRequestNotHonouredException(string message, Exception innerException)
        : base(message, innerException)
    {
        GeneratorName = string.Empty;
        UnhonouredFeatures = MusicRequestFeatures.None;
    }

    /// <summary>Creates the exception for a named generator and the parts it will not act on.</summary>
    /// <param name="message">What the generator will not act on, written for a developer.</param>
    /// <param name="generatorName">The generator that was asked.</param>
    /// <param name="unhonouredFeatures">The parts of the request it does not honour.</param>
    public MusicRequestNotHonouredException(string message, string generatorName,
        MusicRequestFeatures unhonouredFeatures)
        : base(message)
    {
        GeneratorName = generatorName ?? string.Empty;
        UnhonouredFeatures = unhonouredFeatures;
    }

    /// <summary>The generator that was asked, by name. Empty when the exception was built without one.</summary>
    public string GeneratorName { get; }

    /// <summary>
    /// The parts of the request the generator does not honour - so a caller can react to them
    /// rather than reading the message.
    /// </summary>
    public MusicRequestFeatures UnhonouredFeatures { get; }
}
