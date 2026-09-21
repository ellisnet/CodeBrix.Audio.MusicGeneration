using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// The host that opens NO audio device: it renders the music when the application asks it to,
/// which is what an application that owns its own audio output wants.
/// </summary>
/// <remarks>
/// It is a <c>MidiStreamSequencer</c> and nothing else - the same sequencer the device host has
/// inside it - so the music, the pre-roll and the starvation rules are identical either way. The
/// play head moves only as the application pulls audio, which is exactly right: the head is where
/// the listener has got to, and nobody has heard anything nobody has rendered.
/// </remarks>
internal sealed class RendererMusicHost : IMusicHost
{
    private MidiStreamSequencer sequencer;
    private MidiStream stream;
    private bool playing;

    /// <summary>Creates the host at the rate the application renders at.</summary>
    /// <param name="sampleRate">The rate the application pulls audio at, in Hz.</param>
    /// <exception cref="ArgumentOutOfRangeException">The rate is not positive.</exception>
    public RendererMusicHost(int sampleRate)
    {
        if (sampleRate < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate,
                "The sample rate must be a positive value.");
        }

        SampleRate = sampleRate;
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public IAudioRenderer Renderer => sequencer;

    /// <inheritdoc />
    public TimeSpan Position => sequencer == null ? TimeSpan.Zero : sequencer.Position;

    /// <inheritdoc />
    public bool IsStarved => sequencer != null && sequencer.IsStarved;

    /// <inheritdoc />
    public bool IsFinished => playing && sequencer.EndOfStream;

    /// <inheritdoc />
    public void Load(MidiStream stream, Func<int, IMidiSynthesizer> createSynthesizer,
        MidiSequencer.MessageHook messageHook)
    {
        Stop();

        sequencer = new MidiStreamSequencer(createSynthesizer(SampleRate))
        {
            OnSendMessage = messageHook
        };

        this.stream = stream;
    }

    /// <inheritdoc />
    public void Play()
    {
        if (sequencer == null || stream == null)
        {
            return;
        }

        sequencer.Play(stream);
        playing = true;
    }

    /// <inheritdoc />
    public void Stop()
    {
        playing = false;

        if (sequencer != null)
        {
            sequencer.Stop();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        sequencer = null;
        stream = null;
    }
}
