using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// An instrument that holds ONE CONSTANT AMPLITUDE, for ever, whatever it is sent.
/// </summary>
/// <remarks>
/// IT EXISTS SO THAT A FADE CAN BE MEASURED. With a real instrument every sample is the sum of an
/// envelope, an oscillator and a release tail, so the shape of a fade over it cannot be read
/// sample by sample. Over a constant, every sample of the rendered file IS the fade's gain, and a
/// test can compare the file against the curve function itself.
/// </remarks>
public sealed class ConstantToneSynthesizer : IMidiSynthesizer
{
    /// <summary>The level it holds, chosen low enough that a layered part still cannot clip.</summary>
    public const float Amplitude = 0.5F;

    /// <summary>Creates the instrument.</summary>
    /// <param name="sampleRate">The rate it claims to render at.</param>
    public ConstantToneSynthesizer(int sampleRate)
    {
        SampleRate = sampleRate;
        MasterVolume = 1.0F;
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public int BlockSize => 64;

    /// <inheritdoc />
    /// <remarks>Always none: nothing here ever rings out, so a ring-out ends at once.</remarks>
    public int ActiveVoiceCount => 0;

    /// <inheritdoc />
    public float MasterVolume { get; set; }

    /// <inheritdoc />
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        // Nothing changes the level: that is the whole point of it.
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate)
    {
    }

    /// <inheritdoc />
    public void Reset()
    {
    }

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right)
    {
        left.Fill(Amplitude * MasterVolume);
        right.Fill(Amplitude * MasterVolume);
    }
}
