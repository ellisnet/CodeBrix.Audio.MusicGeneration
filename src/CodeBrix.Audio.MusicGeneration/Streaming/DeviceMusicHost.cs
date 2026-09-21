using System;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// The host that opens the audio device and plays the music out loud, which is what happens unless
/// the application says it owns its own audio output.
/// </summary>
/// <remarks>
/// THE DEVICE DECIDES THE SAMPLE RATE, not the options: an already-built synthesizer cannot change
/// the rate it renders at, and one built at the wrong rate plays the whole piece transposed and
/// stretched. So the synthesizer is built inside the player's own factory, which is handed the
/// device's rate, and <see cref="SampleRate"/> reports what that turned out to be.
/// </remarks>
internal sealed class DeviceMusicHost : IMusicHost
{
    private readonly MidiMusicPlayer player = new MidiMusicPlayer();

    private bool loaded;

    /// <summary>The rate the device turned out to run at. It is zero until the music is loaded.</summary>
    public int SampleRate { get; private set; }

    /// <summary>Always null: this host renders to the device itself.</summary>
    public IAudioRenderer Renderer => null;

    /// <inheritdoc />
    public TimeSpan Position => loaded ? player.Position : TimeSpan.Zero;

    /// <inheritdoc />
    public bool IsStarved => loaded && player.IsStarved;

    /// <inheritdoc />
    public bool IsFinished => loaded && player.PlaybackState == PlaybackState.Stopped;

    /// <summary>The volume the device plays at, where 1 is unity.</summary>
    public float Volume
    {
        get => player.Volume;
        set => player.Volume = value;
    }

    /// <inheritdoc />
    public void Load(MidiStream stream, Func<int, IMidiSynthesizer> createSynthesizer,
        MidiSequencer.MessageHook messageHook)
    {
        player.MidiMessageFilter = messageHook;
        player.Load(rate =>
        {
            SampleRate = rate;

            return createSynthesizer(rate);
        }, stream);

        loaded = true;
    }

    /// <inheritdoc />
    public void Play() => player.Play();

    /// <inheritdoc />
    public void Stop()
    {
        if (loaded)
        {
            player.Stop();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        loaded = false;
        player.Dispose();
    }
}
