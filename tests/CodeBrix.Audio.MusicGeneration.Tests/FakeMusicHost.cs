using System;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A host whose play head the test moves by hand, so that a test about the commit window or about
/// what was committed does not also have to synthesize minutes of audio.
/// </summary>
/// <remarks>
/// THREE HEADS, and each of them is one a real player really has:
/// <list type="bullet">
/// <item><description>
/// STILL - <see cref="HeadPosition"/>, which nothing moves but the test. It is a player that has
/// not started.
/// </description></item>
/// <item><description>
/// KEEPING UP - <see cref="FollowTheMusic"/>: the head sits at the end of the music that has been
/// WRITTEN and never a tick past it, which is the best a play head can ever do. It reads that
/// point from <see cref="EndOfTheMusic"/>, which the rig wires to the engine's own
/// <c>WrittenThroughTime</c>. It deliberately does NOT sit at the timeline's horizon: a note
/// written with a duration pushes the horizon out to where that note ENDS, so a head pinned there
/// would be a head that had run PAST the music - which is the state the engine now holds music
/// back from, and not a thing a player does while music is still arriving.
/// </description></item>
/// <item><description>
/// RUNNING - <see cref="RunTheHead"/>: the head runs in real time on the test's clock, holds when
/// it reaches the horizon, and starts again once a pre-roll is written ahead of it - which is
/// <c>MidiStream</c>'s own rule. It is the only head that can run ON past the end of the written
/// music under a note that is still sounding, so it is the head the never-late tests use.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class FakeMusicHost : IMusicHost
{
    private readonly object gate = new object();

    private MidiStream stream;
    private bool follows;

    private TimeProvider clock;
    private TimeSpan preroll;
    private DateTimeOffset lastRead;
    private TimeSpan running;
    private bool moving;

    /// <summary>Creates a host with its head at the start.</summary>
    /// <param name="sampleRate">The rate the synthesizer is built at.</param>
    public FakeMusicHost(int sampleRate = 44100) => SampleRate = sampleRate;

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <summary>The synthesizer the engine built, so that a test can look at the routing table.</summary>
    public IMidiSynthesizer Synthesizer { get; private set; }

    /// <summary>The hook the engine installed, so that a test can deliver a message through it.</summary>
    public MidiSequencer.MessageHook MessageHook { get; private set; }

    /// <inheritdoc />
    public IAudioRenderer Renderer => Synthesizer;

    /// <summary>
    /// Where the music that has been WRITTEN ends, which is where a head that never falls behind
    /// sits. The rig wires it to the engine's own honest horizon; without it a following head falls
    /// back to the timeline's horizon, which includes the ring-out of anything still sounding.
    /// </summary>
    public Func<TimeSpan> EndOfTheMusic { get; set; }

    /// <summary>Where the head is.</summary>
    public TimeSpan Position
    {
        get
        {
            lock (gate)
            {
                if (follows)
                {
                    var music = EndOfTheMusic;

                    if (music != null)
                    {
                        return music();
                    }

                    return stream == null ? TimeSpan.Zero : stream.HorizonTime;
                }

                return clock == null ? HeadPosition : RunningPosition();
            }
        }
    }

    /// <summary>The head position a test set.</summary>
    public TimeSpan HeadPosition { get; set; }

    /// <summary>
    /// Whether the head has run out of music. A test sets it, because an engine test about
    /// COUNTING gaps should not also have to synthesize enough audio to cause one.
    /// </summary>
    public bool Starved { get; set; }

    /// <inheritdoc />
    public bool IsStarved
    {
        get
        {
            lock (gate)
            {
                if (Starved)
                {
                    return true;
                }

                // A RUNNING HEAD ANSWERS THE WAY A REAL ONE DOES: it is starved only once it has
                // caught up with the timeline's HORIZON, so a note still sounding over the end of
                // the music means the stream does not report a gap at all.
                return clock != null && !follows && stream != null &&
                       RunningPosition() >= stream.HorizonTime;
            }
        }
    }

    /// <inheritdoc />
    public bool IsFinished => stream != null && stream.IsCompleted;

    /// <summary>
    /// The head keeps up with everything written and holds at the end of it - a player that never
    /// falls behind. See the remarks on this class for why that is the end of the WRITTEN music
    /// rather than the timeline's horizon.
    /// </summary>
    public void FollowTheMusic() => follows = true;

    /// <summary>
    /// The head runs in real time on a clock the test moves, never past the timeline's horizon,
    /// starting - and starting again after a gap - once a pre-roll is written ahead of it.
    /// </summary>
    /// <param name="timeProvider">The clock the test moves.</param>
    /// <param name="streamPreroll">The pre-roll the timeline was given.</param>
    public void RunTheHead(TimeProvider timeProvider, TimeSpan streamPreroll)
    {
        lock (gate)
        {
            clock = timeProvider;
            preroll = streamPreroll;
            lastRead = timeProvider.GetUtcNow();
            running = TimeSpan.Zero;
            moving = false;
        }
    }

    /// <inheritdoc />
    public void Load(MidiStream midiStream, Func<int, IMidiSynthesizer> createSynthesizer,
        MidiSequencer.MessageHook messageHook)
    {
        stream = midiStream;
        Synthesizer = createSynthesizer(SampleRate);
        MessageHook = messageHook;
    }

    /// <inheritdoc />
    public void Play()
    {
    }

    /// <inheritdoc />
    public void Stop()
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    // Callers hold the gate. The head is advanced as it is read, which is exactly when the engine
    // asks where it is; reading it twice at one moment on the clock gives the same answer twice.
    private TimeSpan RunningPosition()
    {
        if (stream == null)
        {
            return running;
        }

        var now = clock.GetUtcNow();
        var elapsed = now - lastRead;

        lastRead = now;

        var horizon = stream.HorizonTime;

        if (!moving)
        {
            // THE PRE-ROLL IS A RESUME THRESHOLD, not a gap the head keeps: once it is running it
            // carries on until it really does catch up with the horizon.
            if (horizon - running < preroll)
            {
                return running;
            }

            moving = true;
        }

        if (elapsed > TimeSpan.Zero)
        {
            running += elapsed;
        }

        if (running >= horizon)
        {
            running = horizon;
            moving = false;
        }

        return running;
    }
}
