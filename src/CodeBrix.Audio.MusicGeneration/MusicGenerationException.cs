using System;

namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// The base of the errors this library raises for itself: a request a generator will not take, a
/// piece it cannot read, or anything else that is about music generation rather than about a
/// caller's arguments.
/// </summary>
/// <remarks>
/// Argument checking and registry state keep the framework's own exception types, exactly as the
/// registries in CodeBrix.Audio do - a null argument is an <see cref="ArgumentNullException"/> and
/// an unknown generator name is an <see cref="InvalidOperationException"/>. This type is for the
/// cases that are the library's own subject matter, so a consumer can catch those alone.
/// </remarks>
public class MusicGenerationException : Exception
{
    /// <summary>Creates the exception with the default message.</summary>
    public MusicGenerationException()
        : base("Music could not be generated.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong, written for the developer who has to fix it.</param>
    public MusicGenerationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the error that caused it.</summary>
    /// <param name="message">What went wrong, written for the developer who has to fix it.</param>
    /// <param name="innerException">The error underneath this one.</param>
    public MusicGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
