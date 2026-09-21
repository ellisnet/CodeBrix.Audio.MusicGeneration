using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// Whatever is playing the timeline: the audio device, or the application's own audio output.
/// </summary>
/// <remarks>
/// The engine writes the same music either way and asks a host only two things - where the play
/// head has got to, and whether it has run out of music - so that the pre-roll, the commit window
/// and the diagnostics mean the same thing in both modes.
/// </remarks>
internal interface IMusicHost : IDisposable
{
    /// <summary>The rate the synthesizer and everything under it renders at.</summary>
    int SampleRate { get; }

    /// <summary>
    /// What the application pulls audio from, when the application owns its audio output. It is
    /// null for a host that opens the device itself, and null before <see cref="Load"/>.
    /// </summary>
    IAudioRenderer Renderer { get; }

    /// <summary>Where the play head has got to.</summary>
    TimeSpan Position { get; }

    /// <summary>Whether the head has caught up with the music and is playing silence.</summary>
    bool IsStarved { get; }

    /// <summary>Whether the timeline has been completed and everything on it has been played.</summary>
    bool IsFinished { get; }

    /// <summary>Takes the timeline and builds the synthesizer that will play it.</summary>
    /// <param name="stream">The timeline, which may be empty and still growing.</param>
    /// <param name="createSynthesizer">
    /// Builds the synthesizer at the rate it is handed. It is called ONCE, on the calling thread,
    /// which is how a host that gets its rate from the device passes that rate on.
    /// </param>
    /// <param name="messageHook">
    /// Runs on the rendering thread for every message delivered, and is responsible for delivering
    /// it. Never null.
    /// </param>
    void Load(MidiStream stream, Func<int, IMidiSynthesizer> createSynthesizer,
        MidiSequencer.MessageHook messageHook);

    /// <summary>Starts playing what has been loaded.</summary>
    void Play();

    /// <summary>Stops playing and releases the timeline.</summary>
    void Stop();
}
