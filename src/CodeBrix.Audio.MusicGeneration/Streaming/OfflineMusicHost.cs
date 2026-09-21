using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// The host an OFFLINE RENDER plays into: no audio device, no application pulling audio, and a
/// play head that is simply how much audio has been written to the file so far.
/// </summary>
/// <remarks>
/// <para>
/// IT IS THE SAME SEQUENCER AND THE SAME ENGINE the streaming hosts use, which is the whole point:
/// a rendered file is what the same music would have sounded like, not a second implementation of
/// it. What differs is only who pulls the audio and how fast.
/// </para>
/// <para>
/// NOTHING HERE IS EVER STARVED. A renderer that has caught up with the music does not play
/// silence, it WAITS - <see cref="CanRender"/> says when there is enough written to render the
/// next block, and the render loop pumps the engine until there is. That is what keeps a rendered
/// file free of gaps the music does not have, and it is why an engine driven by this host also
/// turns off the rules that exist to protect a play head.
/// </para>
/// </remarks>
internal sealed class OfflineMusicHost : IMusicHost
{
    private readonly int sampleRate;

    private MidiStreamSequencer sequencer;
    private IMidiSynthesizer synthesizer;
    private MidiStream stream;
    private long framesWritten;
    private bool playing;

    /// <summary>Creates the host at the rate the file will be written at.</summary>
    /// <param name="sampleRate">The rate everything renders at, in Hz.</param>
    /// <exception cref="ArgumentOutOfRangeException">The rate is not positive.</exception>
    public OfflineMusicHost(int sampleRate)
    {
        if (sampleRate < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate,
                "The sample rate must be a positive value.");
        }

        this.sampleRate = sampleRate;
    }

    /// <inheritdoc />
    public int SampleRate => sampleRate;

    /// <inheritdoc />
    /// <remarks>
    /// The render loop is what pulls from it, block by block, through <see cref="Render"/>.
    /// </remarks>
    public IAudioRenderer Renderer => sequencer;

    /// <summary>
    /// How much audio has been written to the file. It is the play head of a render, and it is
    /// what the engine commits music ahead of.
    /// </summary>
    public TimeSpan Position => TimeOfFrames(framesWritten);

    /// <summary>How many frames have been written.</summary>
    public long FramesWritten => framesWritten;

    /// <inheritdoc />
    /// <remarks>ALWAYS FALSE. A render waits for music; it never plays silence instead of it.</remarks>
    public bool IsStarved => false;

    /// <inheritdoc />
    public bool IsFinished => playing && sequencer.EndOfStream;

    /// <summary>
    /// Whether the timeline has been completed and every event on it has been rendered, so that
    /// all that can be left is the ring-out of what was still sounding.
    /// </summary>
    public bool MusicHasEnded => playing && sequencer.EndOfStream;

    /// <summary>How many voices are still sounding, which is what a ring-out waits for.</summary>
    public int ActiveVoiceCount => synthesizer == null ? 0 : synthesizer.ActiveVoiceCount;

    /// <summary>
    /// How far the music that has been written reaches, in time - the moment the last thing on the
    /// timeline ends at.
    /// </summary>
    public TimeSpan MusicLength => sequencer == null ? TimeSpan.Zero : sequencer.Length;

    /// <summary>
    /// How many frames the synthesizer renders at a time, which is the smallest step the play head
    /// can take and therefore how closely a render can land on the end of the music.
    /// </summary>
    public int BlockFrames => synthesizer == null ? 1 : synthesizer.BlockSize;

    /// <inheritdoc />
    public void Load(MidiStream stream, Func<int, IMidiSynthesizer> createSynthesizer,
        MidiSequencer.MessageHook messageHook)
    {
        Stop();

        synthesizer = createSynthesizer(sampleRate);
        sequencer = new MidiStreamSequencer(synthesizer)
        {
            OnSendMessage = messageHook
        };

        this.stream = stream;
        framesWritten = 0L;
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
        synthesizer = null;
        stream = null;
    }

    /// <summary>
    /// Whether enough music has been written to render the next frames without the sequencer
    /// running out part way through them.
    /// </summary>
    /// <param name="frames">How many frames the render is about to ask for.</param>
    /// <returns>True when they can be rendered now.</returns>
    /// <remarks>
    /// THE SEQUENCER CLAMPS ITS HEAD AT THE HORIZON, so a block rendered past the end of what has
    /// been written would put audio in the file at a moment the music has not reached - a gap the
    /// music does not have. The margin is one synthesizer block, because the head moves a block at
    /// a time and the comparison it makes is the one made here.
    /// </remarks>
    public bool CanRender(int frames)
    {
        if (sequencer == null || stream == null || !playing)
        {
            return false;
        }

        if (stream.IsCompleted)
        {
            // A completed stream is never clamped and never starved: it drains to its last event.
            return true;
        }

        var blockSize = synthesizer == null ? 0 : synthesizer.BlockSize;

        return sequencer.Length > sequencer.Position + TimeOfFrames(frames + blockSize);
    }

    /// <summary>Renders the next frames of audio and counts them as written.</summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel, which must be the same length.</param>
    public void Render(Span<float> left, Span<float> right)
    {
        sequencer.Render(left, right);
        framesWritten += left.Length;
    }

    private TimeSpan TimeOfFrames(long frames) => TimeSpan.FromSeconds((double)frames / sampleRate);
}
