using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// An instrument library whose every instrument holds one constant amplitude - see
/// <see cref="ConstantToneSynthesizer"/>.
/// </summary>
/// <remarks>
/// It is what makes the SHAPE of a fade measurable: the audio under the fade is a flat line, so
/// every sample of the file is the fade's own gain and nothing else. It is also the fastest thing
/// a render test can use, which keeps the suite quick.
/// </remarks>
public sealed class ConstantToneInstrumentLibrary : IInstrumentLibrary
{
    /// <summary>Creates the library under a name of its own.</summary>
    /// <param name="name">The name it registers and is asked for under.</param>
    public ConstantToneInstrumentLibrary(string name)
    {
        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description => "A test library whose instruments hold one constant level.";

    /// <inheritdoc />
    public InstrumentCoverage Coverage => InstrumentCoverage.General;

    /// <inheritdoc />
    public bool SupportsPerPart => true;

    /// <inheritdoc />
    public bool SupportsMultiTimbral => false;

    /// <inheritdoc />
    public IMidiSynthesizer CreateSynthesizer(int program, int sampleRate) =>
        new ConstantToneSynthesizer(sampleRate);

    /// <inheritdoc />
    public IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate) =>
        new ConstantToneSynthesizer(sampleRate);

    /// <inheritdoc />
    public IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate) =>
        throw new NotSupportedException(
            $"The instrument library '{Name}' plays one part at a time and offers no multi-timbral shape.");

    /// <summary>Registers the library, exactly as any other library's Register() does.</summary>
    public void Register() => InstrumentLibraryRegistry.Register(this);
}
