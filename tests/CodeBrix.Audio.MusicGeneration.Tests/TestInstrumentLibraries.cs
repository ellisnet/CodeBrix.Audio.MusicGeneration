using System.Collections.Generic;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.MusicGeneration.Internal;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Where every test gets an instrument library from: this project registers them, and every test
/// asks for one BY NAME.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING HERE READS THE DEFAULT. The first library registered in a process is the default, so
/// which library that is depends on which test ran first - a test that read the default would be a
/// test about test ordering. Reading or changing the default is gated and runs by itself.
/// </para>
/// <para>
/// REGISTERING IS THE CONSUMER'S JOB, and a test project is a consumer. This is the one place that
/// calls <c>GeneralMidiInstrumentLibrary.Register()</c>.
/// </para>
/// </remarks>
internal static class TestInstrumentLibraries
{
    /// <summary>The name the General MIDI library provided by ModestSynth registers under.</summary>
    public const string GeneralMidiName = GeneralMidiInstrumentLibrary.LibraryName;

    /// <summary>The name of this project's own library, which covers everything.</summary>
    public const string CompleteName = "TestPresets";

    /// <summary>The name of a library that covers only a handful of programs.</summary>
    public const string SparseName = "TestPresetsSparse";

    /// <summary>The name of the library whose instruments hold one constant level.</summary>
    public const string ConstantToneName = "TestConstantTone";

    private static readonly object Gate = new object();
    private static readonly Dictionary<string, TestInstrumentLibrary> Sparse =
        new Dictionary<string, TestInstrumentLibrary>();

    private static TestInstrumentLibrary complete;
    private static ConstantToneInstrumentLibrary constantTone;
    private static bool generalMidiRegistered;

    /// <summary>Registers ModestSynth's General MIDI library and hands it back by name.</summary>
    /// <returns>The library.</returns>
    public static IInstrumentLibrary GeneralMidi()
    {
        lock (Gate)
        {
            if (!generalMidiRegistered)
            {
                GeneralMidiInstrumentLibrary.Register();
                generalMidiRegistered = true;
            }
        }

        return InstrumentLibraryRegistry.Resolve(GeneralMidiName);
    }

    /// <summary>Registers this project's complete library and hands it back by name.</summary>
    /// <returns>The library.</returns>
    public static TestInstrumentLibrary Complete()
    {
        lock (Gate)
        {
            if (complete == null)
            {
                complete = TestInstrumentLibrary.Complete(CompleteName);
                complete.Register();
            }

            return complete;
        }
    }

    /// <summary>
    /// Registers the library whose instruments hold one constant level and hands it back. It is
    /// what makes the shape of a fade measurable sample by sample.
    /// </summary>
    /// <returns>The library.</returns>
    public static ConstantToneInstrumentLibrary ConstantTone()
    {
        lock (Gate)
        {
            if (constantTone == null)
            {
                constantTone = new ConstantToneInstrumentLibrary(ConstantToneName);
                constantTone.Register();
            }

            return constantTone;
        }
    }

    /// <summary>
    /// Registers a library covering only the programs given, under a name of its own, and hands it
    /// back. Asking twice for the same programs gives the same library.
    /// </summary>
    /// <param name="programs">The programs it covers.</param>
    /// <returns>The library.</returns>
    public static TestInstrumentLibrary Covering(params int[] programs)
    {
        var name = SparseName + "-" + string.Join("-", programs);

        lock (Gate)
        {
            if (!Sparse.TryGetValue(name, out var library))
            {
                library = TestInstrumentLibrary.Covering(name, programs);
                library.Register();
                Sparse.Add(name, library);
            }

            return library;
        }
    }

    /// <summary>
    /// A lookup that finds a named library through the real registry, and says that at least one
    /// library is registered - which is what a test that is not about the empty registry wants.
    /// </summary>
    /// <returns>The lookup.</returns>
    public static IInstrumentLibraryLookup Lookup()
    {
        GeneralMidi();

        return InstrumentLibraries.Registered;
    }

    /// <summary>
    /// A lookup with NOTHING registered. The real registry cannot be emptied from outside
    /// CodeBrix.Audio, so this is how the no-library exception is proved without a gate.
    /// </summary>
    /// <returns>The lookup.</returns>
    public static IInstrumentLibraryLookup EmptyLookup() => new NoLibraries();

    private sealed class NoLibraries : IInstrumentLibraryLookup
    {
        public bool HasAny => false;

        public IInstrumentLibrary Resolve(string name) => null;
    }
}
