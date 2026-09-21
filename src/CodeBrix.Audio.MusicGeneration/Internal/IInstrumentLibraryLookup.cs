using CodeBrix.Audio.Instruments;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>
/// How a session finds an instrument library. It exists so that "nothing is registered" can be
/// tested without emptying a process-wide registry that has no way to be emptied from here.
/// </summary>
/// <remarks>
/// The real one is <see cref="InstrumentLibraries"/> and does nothing but call
/// <c>InstrumentLibraryRegistry</c>. Nothing else in this library knows the registry exists, which
/// is also what keeps the no-library exception in ONE place.
/// </remarks>
internal interface IInstrumentLibraryLookup
{
    /// <summary>Whether any instrument library at all has been registered.</summary>
    bool HasAny { get; }

    /// <summary>
    /// Gets an instrument library by name, or the default library when no name is given.
    /// </summary>
    /// <param name="name">The library name, or null for the default.</param>
    /// <returns>The library.</returns>
    IInstrumentLibrary Resolve(string name);
}
