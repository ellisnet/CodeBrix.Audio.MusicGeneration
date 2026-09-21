using CodeBrix.Audio.Instruments;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>
/// The real instrument-library lookup: CodeBrix.Audio's own process-wide registry, and nothing
/// added to it.
/// </summary>
/// <remarks>
/// THIS LIBRARY REGISTERS NOTHING. Which instruments an application plays with is the
/// application's decision, so a session reads the registry and never writes to it - not at
/// start-up, and not when the registry turns out to be empty.
/// </remarks>
internal sealed class InstrumentLibraries : IInstrumentLibraryLookup
{
    /// <summary>The one instance, because it holds nothing of its own.</summary>
    public static readonly InstrumentLibraries Registered = new InstrumentLibraries();

    private InstrumentLibraries()
    {
    }

    /// <inheritdoc />
    public bool HasAny => InstrumentLibraryRegistry.DefaultName != null;

    /// <inheritdoc />
    public IInstrumentLibrary Resolve(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? InstrumentLibraryRegistry.Default
            : InstrumentLibraryRegistry.Resolve(name);
}
