using System;
using System.Collections.Generic;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A small instrument library of this test project's own, built on ModestSynth presets. It is what
/// proves that a session plays the library it was NAMED, and - because its coverage is whatever a
/// test says it is - what proves the coverage fallback.
/// </summary>
/// <remarks>
/// It deliberately needs no asset file and no second package: a library with real instruments in
/// it would add a large restore and a large copy to every test run, and would prove nothing this
/// does not.
/// </remarks>
public sealed class TestInstrumentLibrary : IInstrumentLibrary
{
    private static readonly string[] Presets =
    [
        ModestSynthPresets.SubSineName,
        ModestSynthPresets.SawLeadName,
        ModestSynthPresets.PluckedStringName,
        ModestSynthPresets.ElectricPianoName,
        ModestSynthPresets.WavetablePadName,
        ModestSynthPresets.AdditiveOrganName
    ];

    private readonly List<TestSynthesizer> created = new List<TestSynthesizer>();

    /// <summary>Creates a library with a coverage of its own.</summary>
    /// <param name="name">The name it registers and is asked for under.</param>
    /// <param name="description">What it is.</param>
    /// <param name="coverage">What it covers.</param>
    public TestInstrumentLibrary(string name, string description, InstrumentCoverage coverage)
    {
        Name = name;
        Description = description;
        Coverage = coverage;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public InstrumentCoverage Coverage { get; }

    /// <inheritdoc />
    public bool SupportsPerPart => true;

    /// <inheritdoc />
    public bool SupportsMultiTimbral => false;

    /// <summary>Every instrument this library has been asked to build, in the order it built them.</summary>
    public IReadOnlyList<TestSynthesizer> Created
    {
        get { lock (created) { return created.ToArray(); } }
    }

    /// <summary>Builds a library that covers everything, over the whole keyboard.</summary>
    /// <param name="name">The name it registers under.</param>
    /// <returns>The library.</returns>
    public static TestInstrumentLibrary Complete(string name) =>
        new TestInstrumentLibrary(name, "A test library covering the whole General MIDI set.",
            InstrumentCoverage.General);

    /// <summary>Builds a library that covers only the programs it is given, and no percussion.</summary>
    /// <param name="name">The name it registers under.</param>
    /// <param name="programs">The programs it covers.</param>
    /// <returns>The library.</returns>
    public static TestInstrumentLibrary Covering(string name, params int[] programs) =>
        new TestInstrumentLibrary(name, "A test library covering only a few programs.",
            new InstrumentCoverage(programs, Array.Empty<int>()));

    /// <summary>The instrument that was built for one part, or null when none was.</summary>
    /// <param name="program">The General MIDI program.</param>
    /// <returns>The instrument.</returns>
    public TestSynthesizer BuiltFor(int program)
    {
        foreach (var synthesizer in Created)
        {
            if (synthesizer.Program == program)
            {
                return synthesizer;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IMidiSynthesizer CreateSynthesizer(int program, int sampleRate) =>
        Track(new TestSynthesizer(program, Presets[program % Presets.Length], sampleRate));

    /// <inheritdoc />
    public IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate) =>
        Track(new TestSynthesizer(-1, ModestSynthPresets.PluckedStringName, sampleRate));

    /// <inheritdoc />
    public IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate) =>
        throw new NotSupportedException(
            $"The instrument library '{Name}' plays one part at a time and offers no multi-timbral shape.");

    /// <summary>Registers the library, exactly as any other library's Register() does.</summary>
    public void Register() => InstrumentLibraryRegistry.Register(this);

    private TestSynthesizer Track(TestSynthesizer synthesizer)
    {
        lock (created)
        {
            created.Add(synthesizer);
        }

        return synthesizer;
    }
}
