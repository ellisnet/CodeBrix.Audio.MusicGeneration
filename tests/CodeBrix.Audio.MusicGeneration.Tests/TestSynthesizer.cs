using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// One part's instrument in a test: a real ModestSynth preset, so it makes a real sound, wrapped
/// in a recorder so that a test can see exactly which messages reached which part.
/// </summary>
/// <remarks>
/// It is built on a preset rather than on the General MIDI bank deliberately: proving that a
/// session plays the library it was NAMED needs a library that is audibly and provably not the
/// General MIDI one, and a preset costs nothing and needs no asset file.
/// </remarks>
public sealed class TestSynthesizer : IMidiSynthesizer
{
    private readonly ModestSynthesizer inner;
    private readonly List<string> messages = new List<string>();

    /// <summary>Creates the instrument for one part.</summary>
    /// <param name="program">The General MIDI program it stands for, or -1 for the percussion kit.</param>
    /// <param name="presetName">The ModestSynth preset it really plays.</param>
    /// <param name="sampleRate">The rate it renders at.</param>
    public TestSynthesizer(int program, string presetName, int sampleRate)
    {
        Program = program;
        PresetName = presetName;
        inner = new ModestSynthesizer(ModestSynthPresets.Create(presetName), sampleRate);
    }

    /// <summary>The General MIDI program this instrument stands for, or -1 for the percussion kit.</summary>
    public int Program { get; }

    /// <summary>The ModestSynth preset behind it.</summary>
    public string PresetName { get; }

    /// <summary>Whether this is the percussion kit rather than a melodic instrument.</summary>
    public bool IsPercussion => Program < 0;

    /// <summary>Every message that reached this part, in the order it arrived.</summary>
    public IReadOnlyList<string> Messages => messages;

    /// <summary>How many note-ons reached this part.</summary>
    public int NoteOnCount { get; private set; }

    /// <inheritdoc />
    public int SampleRate => inner.SampleRate;

    /// <inheritdoc />
    public int BlockSize => inner.BlockSize;

    /// <inheritdoc />
    public int ActiveVoiceCount => inner.ActiveVoiceCount;

    /// <inheritdoc />
    public float MasterVolume
    {
        get => inner.MasterVolume;
        set => inner.MasterVolume = value;
    }

    /// <inheritdoc />
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        messages.Add($"{command:X2} ch{channel} {data1} {data2}");

        if (command == 0x90 && data2 > 0)
        {
            NoteOnCount++;
        }

        inner.ProcessMidiMessage(channel, command, data1, data2);
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate) => inner.NoteOffAll(immediate);

    /// <inheritdoc />
    public void Reset() => inner.Reset();

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right) => inner.Render(left, right);
}
