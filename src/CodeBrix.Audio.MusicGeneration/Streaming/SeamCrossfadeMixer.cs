using System;
using System.Collections.Concurrent;
using System.Threading;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// The synthesizer a session plays through when fresh seams are crossfaded: ONE set of instruments
/// most of the time, and TWO for the length of a crossfade - the outgoing piece's, fading out, and
/// the incoming piece's, fading in - mixed into the one output the host plays.
/// </summary>
/// <remarks>
/// <para>
/// WHY TWO SETS. Both pieces use the same MIDI channels, and very often the same programs on them,
/// so they cannot share one routing synthesizer: a note-off meant for one piece would silence the
/// other, and the incoming piece's program changes would re-voice the outgoing one mid-phrase. So
/// the incoming piece gets a voicer - and a routing synthesizer - of its own, over the same
/// instrument library, BUILT ON THE PUMP THREAD like every other instrument. Nothing is ever built
/// here.
/// </para>
/// <para>
/// HOW THE TWO PIECES REACH IT. The timeline carries the outgoing piece up to the start of the
/// fade, a CUE (see <see cref="SeamCrossfadePlan"/>), and the incoming piece from there on. The
/// outgoing piece's last moments are not on the timeline at all: they came with the plan as
/// ready-made messages, and this mixer plays them to the outgoing instruments itself, timed in
/// frames from the cue. So everything that arrives through <see cref="ProcessMidiMessage"/> belongs
/// to the piece that is IN - with one exception, the note-offs of notes the outgoing piece started
/// on the timeline before the cue and that are still sounding. Those are recognised by key: the
/// mixer counts what each piece has sounding, and a note-off for a key the outgoing piece still
/// holds is the outgoing piece's. When BOTH hold the same key on the same channel the older note
/// - the outgoing one - takes the first note-off, which is the one sharp edge of the scheme: in
/// that rare case one of the two notes is released at the other's time.
/// </para>
/// <para>
/// WHAT HAPPENS TO THE OUTGOING PIECE AT THE END OF THE FADE. Its gain has reached nought, so its
/// instruments are LET GO there and then, whatever is still ringing in them: they are silent
/// already. A note-off still to come for one of its notes is swallowed rather than sent to the
/// incoming instruments.
/// </para>
/// <para>
/// THE GAIN FOLLOWS THE FADE CURVE FRAME BY FRAME, the outgoing piece along it and the incoming
/// piece along its mirror image, so an equal-power curve keeps the loudness steady and a straight
/// line keeps the sum of the two gains at one. With no crossfade in progress the mixer renders the
/// one set of instruments straight into the output and touches nothing.
/// </para>
/// <para>
/// THREADS. <see cref="Arm"/> and <see cref="Disarm"/> are the pump thread's; everything else runs
/// on the rendering thread, inside the sequencer's message hook or its render call, and allocates
/// nothing.
/// </para>
/// </remarks>
internal sealed class SeamCrossfadeMixer : IMidiSynthesizer
{
    private const int Channels = 16;
    private const int Notes = 128;
    private const int Keys = Channels * Notes;
    private const int ScratchFrames = 512;
    private const int NoteOffCommand = 0x80;
    private const int NoteOnCommand = 0x90;
    private const int ControlChangeCommand = 0xB0;
    private const int CueWireChannel = SeamCrossfadePlan.CueChannel - 1;

    private readonly float[] scratchLeft = new float[ScratchFrames];
    private readonly float[] scratchRight = new float[ScratchFrames];

    // THE PLANS HANDED OVER AND NOT BEGUN YET, in the order their cues lie on the timeline. There
    // can be more than one: the engine commits a cue - and may hand over the next plan - well
    // before the rendering thread reaches that cue.
    private readonly ConcurrentQueue<SeamCrossfadePlan> armed = new ConcurrentQueue<SeamCrossfadePlan>();

    private RenditionVoicer main;
    private RenditionVoicer outgoing;
    private SeamCrossfadePlan fading;
    private int[] mainHeld = new int[Keys];
    private int[] outgoingHeld = new int[Keys];
    private long fadePosition;
    private int tailIndex;
    private int crossfadesStarted;
    private int warmedUp;
    private bool warmedUpOnThreadPool;
    private ThreadPriority warmUpPriority;

    /// <summary>Builds the mixer over the instruments the music starts with.</summary>
    /// <param name="first">The voicer the first piece plays through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="first"/> is null.</exception>
    public SeamCrossfadeMixer(RenditionVoicer first)
    {
        if (first == null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        main = first;
        SampleRate = first.Router.SampleRate;
        BlockSize = first.Router.BlockSize;
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public int BlockSize { get; }

    /// <inheritdoc />
    public int ActiveVoiceCount
    {
        get
        {
            var count = main.Router.ActiveVoiceCount;
            var fadingOut = outgoing;

            return fadingOut == null ? count : count + fadingOut.Router.ActiveVoiceCount;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// It scales the MIXED output. Each set of instruments keeps the rendition's own master gain,
    /// which its voicer set when it built it.
    /// </remarks>
    public float MasterVolume { get; set; } = 1.0F;

    /// <summary>Whether a crossfade is sounding right now.</summary>
    public bool IsCrossfading => Volatile.Read(ref fading) != null;

    /// <summary>How many crossfades have begun - how many cues have been met - since the mixer was built.</summary>
    public int CrossfadesStarted => Volatile.Read(ref crossfadesStarted);

    /// <summary>
    /// Whether <see cref="WarmUp"/> has run: whether every code path a crossfade takes on the
    /// rendering thread has already run once somewhere else.
    /// </summary>
    public bool IsWarmedUp => Volatile.Read(ref warmedUp) != 0;

    /// <summary>Whether the warm-up ran on a thread-pool thread - which it never should.</summary>
    public bool WarmedUpOnThreadPool => warmedUpOnThreadPool;

    /// <summary>The priority of the thread the warm-up ran on.</summary>
    public ThreadPriority WarmUpPriority => warmUpPriority;

    /// <summary>The voicer of the piece that is IN: the one the timeline's messages are going to.</summary>
    public RenditionVoicer Current => Volatile.Read(ref main);

    /// <summary>
    /// Hands over a crossfade that will begin when its cue is met on the timeline. Called on the
    /// pump thread, before the cue is written, and in the order the cues lie on the timeline.
    /// </summary>
    /// <param name="plan">The crossfade.</param>
    public void Arm(SeamCrossfadePlan plan) => armed.Enqueue(plan);

    /// <summary>
    /// Takes back a crossfade whose cue will never be written - because a follow-up prompt threw
    /// away the music it belonged to. A plan that has already begun is not affected.
    /// </summary>
    /// <param name="plan">The crossfade.</param>
    public void Disarm(SeamCrossfadePlan plan) => plan.Disarm();

    /// <summary>
    /// Runs a whole crossfade ONCE, OFF THE RENDERING THREAD, through a throwaway mixer over two
    /// throwaway voicers, so that the first real crossfade does not pay for anything the first
    /// time on the audio callback: the cue, the switch, the outgoing piece's messages, the mixing
    /// loop along the curve and the letting go are all compiled by then. It touches nothing this
    /// mixer is playing. Called when the music starts, before the host starts pulling audio.
    /// </summary>
    /// <param name="createVoicer">Builds a voicer exactly as the engine will for an incoming piece.</param>
    /// <param name="curve">The curve the real crossfades will follow.</param>
    /// <exception cref="ArgumentNullException"><paramref name="createVoicer"/> is null.</exception>
    public void WarmUp(Func<RenditionVoicer> createVoicer, MusicFadeCurve curve)
    {
        if (createVoicer == null)
        {
            throw new ArgumentNullException(nameof(createVoicer));
        }

        const int Frames = 256;

        var practice = new SeamCrossfadeMixer(createVoicer());
        var plan = new SeamCrossfadePlan(1, 0L, 1L, createVoicer(), Frames, curve,
            WarmUpTail(SampleRate));
        var left = new float[BlockSize];
        var right = new float[BlockSize];

        practice.ProcessMidiMessage(0, NoteOnCommand, 60, 1);
        practice.Arm(plan);
        practice.ProcessMidiMessage(CueWireChannel, ControlChangeCommand, SeamCrossfadePlan.CueController,
            plan.Cue);
        practice.ProcessMidiMessage(0, NoteOnCommand, 62, 1);
        practice.ProcessMidiMessage(0, NoteOffCommand, 60, 0);

        for (var rendered = 0; rendered < Frames * 2; rendered += BlockSize)
        {
            practice.Render(left, right);
        }

        practice.ProcessMidiMessage(0, NoteOffCommand, 62, 0);
        practice.Disarm(plan);
        warmedUpOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
        warmUpPriority = Thread.CurrentThread.Priority;
        Volatile.Write(ref warmedUp, 1);
    }

    /// <inheritdoc />
    /// <remarks>
    /// RUNS ON THE RENDERING THREAD. The cue of the next plan handed over is swallowed and starts the
    /// crossfade; a note-off for a key the outgoing piece still holds goes to the outgoing
    /// instruments (or nowhere, once they are gone); everything else goes to the piece that is in,
    /// after that piece's voicer has handed its routing table whatever the pump thread has built.
    /// </remarks>
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        if (command == ControlChangeCommand && channel == CueWireChannel &&
            data1 == SeamCrossfadePlan.CueController)
        {
            while (armed.TryPeek(out var plan))
            {
                if (plan.IsDisarmed)
                {
                    // Its cue was never written; it is waiting in front of the one that was.
                    armed.TryDequeue(out _);

                    continue;
                }

                if (plan.Cue != data2)
                {
                    break;
                }

                armed.TryDequeue(out _);
                Begin(plan);

                return;
            }
        }

        var key = KeyOf(channel, data1);

        if (IsNoteOff(command, data2))
        {
            if (key >= 0 && outgoingHeld[key] > 0)
            {
                // THE OUTGOING PIECE'S OWN NOTE, started before the cue and ending after it.
                outgoingHeld[key]--;

                var fadingOut = outgoing;

                if (fadingOut != null)
                {
                    Deliver(fadingOut, channel, command, data1, data2);
                }

                return;
            }

            if (key >= 0 && mainHeld[key] > 0)
            {
                mainHeld[key]--;
            }
        }
        else if (command == NoteOnCommand && key >= 0)
        {
            mainHeld[key]++;
        }

        Deliver(main, channel, command, data1, data2);
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate)
    {
        main.Router.NoteOffAll(immediate);

        var fadingOut = outgoing;

        if (fadingOut != null)
        {
            fadingOut.Router.NoteOffAll(immediate);
        }

        Array.Clear(mainHeld);
        Array.Clear(outgoingHeld);
    }

    /// <inheritdoc />
    /// <remarks>A crossfade in progress ends at once: the outgoing instruments are let go.</remarks>
    public void Reset()
    {
        main.Router.Reset();
        LetGoOfTheOutgoingPiece();
        Array.Clear(mainHeld);
        Array.Clear(outgoingHeld);
    }

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("The output buffers for the left and right must be the same length.");
        }

        var done = 0;

        while (done < left.Length)
        {
            var plan = fading;

            if (plan == null)
            {
                main.Router.Render(left.Slice(done), right.Slice(done));

                break;
            }

            PlayDueTail(plan);

            var frames = Math.Min(left.Length - done, ScratchFrames);
            var untilTheEnd = plan.FadeFrames - fadePosition;

            if (untilTheEnd < frames)
            {
                frames = (int)untilTheEnd;
            }

            if (tailIndex < plan.TailCount)
            {
                // The outgoing piece's messages land on their own frame, not on the next block.
                var untilTheNext = plan.TailFrame(tailIndex) - fadePosition;

                if (untilTheNext > 0L && untilTheNext < frames)
                {
                    frames = (int)untilTheNext;
                }
            }

            var mainLeft = left.Slice(done, frames);
            var mainRight = right.Slice(done, frames);
            var outLeft = scratchLeft.AsSpan(0, frames);
            var outRight = scratchRight.AsSpan(0, frames);

            main.Router.Render(mainLeft, mainRight);
            outgoing.Router.Render(outLeft, outRight);

            for (var index = 0; index < frames; index++)
            {
                var progress = (double)(fadePosition + index) / plan.FadeFrames;
                var fallingGain = MusicFade.GainAt(plan.Curve, progress);
                var risingGain = MusicFade.GainAt(plan.Curve, 1.0 - progress);

                mainLeft[index] = (mainLeft[index] * risingGain) + (outLeft[index] * fallingGain);
                mainRight[index] = (mainRight[index] * risingGain) + (outRight[index] * fallingGain);
            }

            fadePosition += frames;
            done += frames;

            if (fadePosition >= plan.FadeFrames)
            {
                LetGoOfTheOutgoingPiece();
            }
        }

        var volume = MasterVolume;

        if (volume != 1.0F)
        {
            for (var index = 0; index < left.Length; index++)
            {
                left[index] *= volume;
                right[index] *= volume;
            }
        }
    }

    private static TimedTailEvent[] WarmUpTail(int rate)
    {
        // One note, on and off inside the practice fade, so the outgoing piece's own messages are
        // played the way a real tail's are.
        var note = new NoteOnEvent(0L, 1, 64, 1, 1);

        return new[]
        {
            new TimedTailEvent(note, TimeSpan.FromSeconds(1.0 / rate), TimeSpan.FromSeconds(100.0 / rate))
        };
    }

    private static bool IsNoteOff(int command, int velocity) =>
        command == NoteOffCommand || (command == NoteOnCommand && velocity == 0);

    private static int KeyOf(int channel, int note) =>
        channel >= 0 && channel < Channels && note >= 0 && note < Notes ? (channel * Notes) + note : -1;

    private static void Deliver(RenditionVoicer voicer, int channel, int command, int data1, int data2)
    {
        // The hook's two lines, for whichever set of instruments the message belongs to: hand the
        // routing table what has been built, then deliver.
        voicer.ApplyPending(channel, command, data1);
        voicer.Router.ProcessMidiMessage(channel, command, data1, data2);
    }

    private void Begin(SeamCrossfadePlan plan)
    {
        // A crossfade that has not finished when the next one begins is cut short: its outgoing
        // piece is two pieces ago and all but silent.
        LetGoOfTheOutgoingPiece();

        outgoing = main;

        var swap = outgoingHeld;

        outgoingHeld = mainHeld;
        mainHeld = swap;
        Array.Clear(mainHeld);

        Volatile.Write(ref main, plan.Incoming);
        fadePosition = 0L;
        tailIndex = 0;
        Volatile.Write(ref fading, plan);
        Interlocked.Increment(ref crossfadesStarted);
    }

    private void PlayDueTail(SeamCrossfadePlan plan)
    {
        while (tailIndex < plan.TailCount && plan.TailFrame(tailIndex) <= fadePosition)
        {
            Deliver(outgoing, plan.TailChannel(tailIndex), plan.TailCommand(tailIndex),
                plan.TailData1(tailIndex), plan.TailData2(tailIndex));
            tailIndex++;
        }
    }

    private void LetGoOfTheOutgoingPiece()
    {
        // Its gain is nought by now, so whatever still rings in it is already silent. What it
        // still holds on the timeline is remembered in outgoingHeld, so that those note-offs are
        // swallowed rather than sent to the piece that is in.
        outgoing = null;
        Volatile.Write(ref fading, null);
    }
}
