using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// What carries generated music onto a timeline that is already being played: it pulls from the
/// generator, reorders what arrives, keeps the long lookahead in memory, and commits a short
/// window of it to the timeline ahead of the play head.
/// </summary>
/// <remarks>
/// <para>
/// THREE STAGES, and each one exists for a reason the one before it cannot solve.
/// </para>
/// <para>
/// 1. THE REORDER BUFFER takes what the generator yields, which may be out of tick order, and lets
/// events out only once the generator has settled their ticks - because a timeline can only be
/// written forwards, and because pre-roll and starvation are only honest numbers if the timeline's
/// horizon is the SETTLED horizon.
/// </para>
/// <para>
/// 2. THE SETTLED BUFFER holds released events in memory, in tick order, with a tempo map. It
/// exists because a timeline is append-only and a follow-up prompt has to be able to drop the
/// lookahead it is replacing. What cannot be taken off the timeline must never go on it early.
/// </para>
/// <para>
/// 3. THE COMMIT moves events onto the timeline, but only as far as
/// <see cref="CommitWindow"/> ahead of the play head. A SETTLED STRETCH WITH NOTHING IN IT still
/// has to move the timeline's horizon, or a long rest would look exactly like a generator that has
/// stalled - so the commit appends one harmless text meta event at the settled tick, which no
/// synthesizer will ever be shown and which moves the horizon all the same.
/// </para>
/// <para>
/// <see cref="Pump"/> is the whole of stages 2 and 3 and is driven by a light background timer, so
/// that the work is never done on the audio thread and never in the application's way. The timer
/// runs on an injected <see cref="System.TimeProvider"/>, so a test drives the engine by hand and
/// never sleeps.
/// </para>
/// <para>
/// THE LIFE OF A STREAM, built on those three stages:
/// </para>
/// <list type="bullet">
/// <item><description>
/// IT KEEPS GOING. When a generator's pass ends, <see cref="EndOfPiece"/> decides whether the
/// timeline is completed or the SAME generator is asked for the next segment, with the music so
/// far in view, placed at the next BAR LINE.
/// </description></item>
/// <item><description>
/// IT RUNS ONLY AS FAR AHEAD AS IT SHOULD. Pulling stops when there is <see cref="GenerateAhead"/>
/// of settled music ahead of the play head and starts again when that falls to half. Not pulling
/// IS pausing: a generator does no work while nobody asks it for anything.
/// </description></item>
/// <item><description>
/// IT CAN BE RE-PROMPTED WHILE IT PLAYS. <see cref="FollowUp"/> starts a second generation
/// alongside the first; when that one has its own pre-roll it takes over at the next bar line
/// beyond what is committed, and the old lookahead is dropped. A follow-up that names the
/// generator ALREADY GENERATING is a HAND-OVER rather than a race: that generation is cancelled
/// and the new one starts once it has ended, because one generator writes one piece at a time.
/// </description></item>
/// <item><description>
/// IT DEGRADES GRACEFULLY. When the measured <see cref="RealTimeFactor"/> falls below real time it
/// stops streaming and delivers a whole segment at a time instead - see
/// <see cref="MusicDeliveryMode"/>.
/// </description></item>
/// <item><description>
/// IT IS NEVER LATE, AND IT WAITS IN WHOLE BARS. Nothing is ever committed at or behind the play
/// head. A note written with a duration pushes the timeline's horizon out to where that note ENDS,
/// and the head may run as far as the horizon - so after a rest, or during a stall, the head can
/// run on past the music the engine has committed. When the head has reached the place the music
/// that is coming belongs, everything not yet committed is HELD BACK by a whole number of bars,
/// far enough that it lands a clear margin in front of the head: the phrase finishes, the ringing
/// notes ring out as written, there is a rest of whole bars, and the music carries on IN TIME.
/// </description></item>
/// </list>
/// <para>
/// ONE THREAD DOES ALL OF IT. Every decision above is taken inside <see cref="Pump"/>, under one
/// lock, on the pump thread. The pull tasks only hand events in; nothing is ever built, decided or
/// routed on the audio thread.
/// </para>
/// </remarks>
internal sealed class MusicEngine : IDisposable
{
    /// <summary>
    /// How far ahead of the play head music is committed, as a multiple of the pre-roll.
    /// </summary>
    /// <remarks>
    /// It is TWICE the pre-roll, and that is the smallest sensible number. Playback will not start,
    /// and will not restart after running dry, until a whole pre-roll is written ahead of the head,
    /// so a window of exactly one pre-roll would sit on the threshold and every pump's worth of
    /// granularity would push it under. Twice leaves a whole pre-roll of slack above the threshold
    /// while still keeping the timeline short enough that dropping the lookahead means something.
    /// </remarks>
    public const double CommitWindowFactor = StreamingDefaults.CommitWindowFactor;

    /// <summary>The shortest commit window, whatever the pre-roll is.</summary>
    public static readonly TimeSpan MinimumCommitWindow = StreamingDefaults.MinimumCommitWindow;

    /// <summary>How often the background timer pumps.</summary>
    public static readonly TimeSpan DefaultPumpInterval = StreamingDefaults.PumpInterval;

    /// <summary>How much settled music to keep ahead of the play head, unless told otherwise.</summary>
    public static readonly TimeSpan DefaultGenerateAhead = StreamingDefaults.GenerateAhead;

    private readonly object gate = new object();
    private readonly MidiStream stream;
    private readonly RenditionVoicer voicer;
    private readonly IMusicHost host;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan preroll;
    private readonly SettledMusicBuffer settled;

    // TWO VIEWS OF THE SAME MUSIC, and the difference between them is the lookahead. The SETTLED
    // view knows everything the generator has finished writing, which is what the next segment
    // carries on from and where the bar lines of that music are. The COMMITTED view knows only
    // what has reached the timeline, which is what a seam is measured against and what a follow-up
    // prompt switches after - because a follow-up throws the lookahead away, and the settled view
    // is put back to the committed one when it does.
    private readonly SettledBarGrid settledBars;
    private readonly SettledBarGrid committedBars;
    private readonly CarriedMusicState settledState = new CarriedMusicState();
    private readonly CarriedMusicState committedState = new CarriedMusicState();
    private readonly List<MidiEvent> settledMusic = new List<MidiEvent>();
    private readonly Queue<long> seamTicks = new Queue<long>();
    private readonly List<long> segmentStartTicks = new List<long>();

    private SegmentGeneration current;
    private SegmentGeneration incoming;
    private ITimer timer;
    private bool started;
    private bool stopped;
    private bool disposed;

    private int segmentCount;
    private long committedThroughTick;
    private long writtenThroughTick = -1L;

    private int holdCount;
    private int heldBarCount;
    private double heldSeconds;

    private bool pullingAllowed = true;
    private bool wasPulling = true;

    private bool fallbackActive;
    private long fallbackCommittedThroughTick = -1L;

    private bool hasSample;
    private long lastSampleTimestamp;
    private double lastMusicSeconds;
    private double producedSeconds;
    private double generatingSeconds;
    private double measuredGeneratingSeconds;

    private bool wasStarved;
    private bool musicHasStarted;
    private int starvationGapCount;

    /// <summary>Builds an engine over one generator, one request and one timeline.</summary>
    /// <param name="generator">The generator to pull music from.</param>
    /// <param name="request">The request to pull it with. It is used as given.</param>
    /// <param name="stream">The timeline to write to, which nothing else may be writing to.</param>
    /// <param name="voicer">What voices the parts as they are committed.</param>
    /// <param name="host">What is playing the timeline.</param>
    /// <param name="preroll">How much music must be written ahead of the head before playing starts.</param>
    /// <param name="timeProvider">The clock the background pump runs on.</param>
    /// <param name="segmentStartTick">Where this segment sits on the timeline.</param>
    public MusicEngine(IMusicGenerator generator, MusicRequest request, MidiStream stream,
        RenditionVoicer voicer, IMusicHost host, TimeSpan preroll, TimeProvider timeProvider,
        long segmentStartTick)
    {
        this.stream = stream;
        this.voicer = voicer;
        this.host = host;
        this.timeProvider = timeProvider;
        this.preroll = preroll;

        settled = new SettledMusicBuffer(stream.TicksPerQuarterNote);
        settledBars = new SettledBarGrid(stream.TicksPerQuarterNote);
        committedBars = new SettledBarGrid(stream.TicksPerQuarterNote);
        current = new SegmentGeneration(generator, request, request, new ReorderBuffer(segmentStartTick),
            false, new CancellationTokenSource(), stream.TicksPerQuarterNote);
        segmentCount = 1;
        segmentStartTicks.Add(segmentStartTick);
        committedThroughTick = segmentStartTick - 1L;
        fallbackCommittedThroughTick = segmentStartTick - 1L;

        var window = TimeSpan.FromTicks((long)(preroll.Ticks * CommitWindowFactor));
        CommitWindow = window < MinimumCommitWindow ? MinimumCommitWindow : window;

        stream.Preroll = preroll;
    }

    /// <summary>The timeline the music is written to.</summary>
    public MidiStream Stream => stream;

    /// <summary>How far ahead of the play head music is committed.</summary>
    public TimeSpan CommitWindow { get; }

    /// <summary>
    /// How much settled music to keep ahead of the play head before pulling stops. Pulling starts
    /// again when the lead falls to half of it.
    /// </summary>
    public TimeSpan GenerateAhead { get; set; } = DefaultGenerateAhead;

    /// <summary>
    /// What happens when the generator's pass ends. THE ENGINE'S OWN DEFAULT IS
    /// <see cref="EndOfPiecePolicy.Stop"/> - one pass, then the timeline is completed - because an
    /// engine is a mechanism and the shortest thing it can honestly do is play what it was given.
    /// <see cref="MusicSession"/> sets it from the options, whose default is the other way round.
    /// </summary>
    public EndOfPiecePolicy EndOfPiece { get; set; } = EndOfPiecePolicy.Stop;

    /// <summary>
    /// Whether to keep pulling from the generator. Leaving it null is what an application does: the
    /// engine then applies its own generate-ahead window, where "stop pulling" is the whole of
    /// pausing generation. A test replaces it to drive the pull loop by hand.
    /// </summary>
    public Func<bool> CanPull { get; set; }

    /// <summary>
    /// Whether music that the play head has reached is HELD BACK to a later bar line, which is what
    /// keeps a stream from ever committing anything behind the head.
    /// </summary>
    /// <remarks>
    /// IT IS TRUE FOR ANYTHING WITH A PLAY HEAD, which is both of the streaming hosts. An OFFLINE
    /// RENDER has no play head - nothing is being listened to, and the renderer consumes exactly
    /// what has been written, whenever it is written - so a render sets this false and no music is
    /// ever moved: what is rendered is tick for tick what the generator wrote. The head-based
    /// starvation counting goes with it, for the same reason.
    /// </remarks>
    public bool KeepsMusicAheadOfTheHead { get; set; } = true;

    /// <summary>
    /// Whether a generator slower than real time makes the engine stop streaming and deliver a
    /// whole segment at a time instead - see <see cref="MusicDeliveryMode"/>.
    /// </summary>
    /// <remarks>
    /// IT IS TRUE FOR ANYTHING WITH A PLAY HEAD, because a head that runs out mid-phrase sounds
    /// broken and a phrase followed by a rest sounds intentional. An OFFLINE RENDER sets it false
    /// and sets <see cref="KeepsMusicAheadOfTheHead"/> false with it: there is no head to protect,
    /// nothing can stall mid-phrase when nobody is listening, and holding a whole segment back
    /// would only make a render write its file in bursts. A render is flat out by definition, so
    /// its measured rate would keep the fallback away anyway - but it says so rather than relying
    /// on it.
    /// </remarks>
    public bool FallsBackToSegmentAtATime { get; set; } = true;

    /// <summary>The generator whose music is REACHING THE TIMELINE, which a follow-up changes at the switch.</summary>
    public IMusicGenerator ActiveGenerator
    {
        get { lock (gate) { return current.Generator; } }
    }

    /// <summary>Whether a follow-up prompt is generating but has not taken over yet.</summary>
    public bool HasPendingFollowUp
    {
        get { lock (gate) { return incoming != null; } }
    }

    /// <summary>
    /// Where the next segment would start: the tick the generator has settled through, which for a
    /// generator that ends its pass on a bar line is a bar line.
    /// </summary>
    public long NextSegmentStartTick
    {
        get
        {
            lock (gate)
            {
                return current.Reorder.HasSettled
                    ? current.Reorder.SettledThroughTick
                    : current.Reorder.SegmentStartTick;
            }
        }
    }

    /// <summary>Whether the generator has finished and everything it wrote has been committed.</summary>
    public bool IsFinished
    {
        get { lock (gate) { return stream.IsCompleted; } }
    }

    /// <summary>
    /// What went wrong inside the generator, or null. A generator that throws stops the music
    /// rather than leaving the timeline waiting in silence for ever.
    /// </summary>
    public Exception GenerationError { get; private set; }

    /// <summary>How much settled music is waiting in memory rather than on the timeline.</summary>
    public int UncommittedCount
    {
        get { lock (gate) { return settled.Count + current.Reorder.HeldCount; } }
    }

    /// <summary>How many segments have been generated, counting the first piece as one.</summary>
    public int SegmentCount
    {
        get { lock (gate) { return segmentCount; } }
    }

    /// <summary>
    /// How many times the play head has caught up with the music and had to wait, AFTER the music
    /// started. The wait for the first pre-roll is not one: nothing has run out yet.
    /// </summary>
    public int StarvationGapCount
    {
        get { lock (gate) { return starvationGapCount; } }
    }

    /// <summary>
    /// How many times the music has been held back to a later bar line so that it never lands at
    /// or behind the play head.
    /// </summary>
    public int HoldCount
    {
        get { lock (gate) { return holdCount; } }
    }

    /// <summary>How many whole bars of rest those holds have inserted altogether.</summary>
    public int HeldBarCount
    {
        get { lock (gate) { return heldBarCount; } }
    }

    /// <summary>
    /// Seconds of settled music produced for every second spent generating, or null while there
    /// has not been enough generating to measure.
    /// </summary>
    public double? RealTimeFactor
    {
        get { lock (gate) { return MeasuredRealTimeFactor(); } }
    }

    /// <summary>Whether music is reaching the timeline as a stream or a whole segment at a time.</summary>
    public MusicDeliveryMode Mode
    {
        get
        {
            lock (gate)
            {
                return fallbackActive ? MusicDeliveryMode.SegmentAtATime : MusicDeliveryMode.Streaming;
            }
        }
    }

    /// <summary>How much settled music is waiting ahead of the play head.</summary>
    public TimeSpan Lead
    {
        get { lock (gate) { return CurrentLead(); } }
    }

    /// <summary>
    /// How much music the generator has finished writing, in seconds, whether or not any of it has
    /// reached the timeline. It is the frontier of generation, where
    /// <see cref="WrittenThroughTime"/> is the frontier of the timeline.
    /// </summary>
    /// <remarks>
    /// An offline render measures its own "there is enough music for the file now" against this,
    /// because a render generates flat out and its commit is held back by its own head.
    /// </remarks>
    public TimeSpan SettledThroughTime
    {
        get { lock (gate) { return settled.TimeOfTick(current.Reorder.SettledThroughTick); } }
    }

    /// <summary>
    /// The tick each segment of the music starts at, the first one included and in order. Every
    /// one of them is a bar line, and they are the seams a continuation joins at.
    /// </summary>
    public IReadOnlyList<long> SegmentStartTicks
    {
        get { lock (gate) { return segmentStartTicks.ToArray(); } }
    }

    /// <summary>
    /// The tick the timeline is complete THROUGH: everything at or before it has been written, and
    /// nothing after it has. It is not the timeline's horizon, which a note written with a duration
    /// pushes out as far as that note's end.
    /// </summary>
    public long CommittedThroughTick
    {
        get { lock (gate) { return committedThroughTick; } }
    }

    /// <summary>
    /// The tick the music has really been WRITTEN through: what has been committed AND settled, so
    /// nothing at or before it is still to come. It is -1 until anything is written.
    /// </summary>
    /// <remarks>
    /// It is the engine's own honest horizon, and it is deliberately not
    /// <c>MidiStream.HorizonTicks</c> - which a note's own length pushes out past music nobody has
    /// written yet - nor <see cref="CommittedThroughTick"/>, which is a commit window that may have
    /// reached past everything the generator has settled.
    /// </remarks>
    public long WrittenThroughTick
    {
        get { lock (gate) { return writtenThroughTick; } }
    }

    /// <summary>
    /// Where a play head that had heard everything written would be: the moment
    /// <see cref="WrittenThroughTick"/> falls at, by the engine's own tempo map.
    /// </summary>
    public TimeSpan WrittenThroughTime
    {
        get { lock (gate) { return settled.TimeOfTick(writtenThroughTick); } }
    }

    /// <summary>Starts pulling from the generator, and starts the background pump.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (disposed || started)
            {
                return;
            }

            started = true;
            StartPull(current);
            timer = timeProvider.CreateTimer(_ => PumpSafely(), null, DefaultPumpInterval,
                DefaultPumpInterval);
        }
    }

    /// <summary>
    /// Moves settled music from memory onto the timeline, as far as the commit window reaches, and
    /// decides what happens when a pass ends. Tests call it directly; in an application the
    /// background timer does.
    /// </summary>
    /// <remarks>
    /// THE SPINE IS RELEASE, COMMIT, THEN DECIDE WHAT COMES NEXT, all under one lock on one thread.
    /// The steps around them are measurements and decisions, not work: what they decide is how far
    /// the commit reaches, where music that the head has caught up with is moved to, and whether
    /// the pull loop is allowed to pull.
    /// </remarks>
    public void Pump()
    {
        SegmentGeneration replaced = null;

        lock (gate)
        {
            if (disposed || stream.IsCompleted)
            {
                return;
            }

            CountStarvation();
            Release();
            KeepTheMusicAheadOfTheHead();
            TakeOverIfFollowUpIsReady(ref replaced);
            Measure();
            ChooseDeliveryMode();
            Commit();
            UpdatePullPolicy();
            ContinueOrComplete();
        }

        // OUTSIDE THE LOCK. Cancelling runs whatever is registered on the token there and then,
        // and what is registered on this one is a pull loop that takes this very lock.
        StopPulling(replaced);
    }

    /// <summary>
    /// Starts generating from a new request - and, if one is named, a different generator - while
    /// the music that is playing carries on.
    /// </summary>
    /// <param name="generator">The generator to ask. It may be the one already playing.</param>
    /// <param name="request">The request to ask it with.</param>
    /// <remarks>
    /// <para>
    /// IT DOES NOT WAIT FOR THE LOOKAHEAD TO PLAY OUT. The new generation starts alongside the old
    /// one; when it has its OWN pre-roll of settled music it takes over at the next bar line beyond
    /// what has been committed, the old generation is cancelled and its uncommitted lookahead is
    /// dropped. So a prompt change takes a few seconds however far ahead the engine was running.
    /// </para>
    /// <para>
    /// THE SAME GENERATOR TWICE IS A HAND-OVER, NOT A RACE, and it is the commonest follow-up there
    /// is: the same model, a new prompt. A model writes ONE piece at a time - ask a real one for a
    /// second generation while the first is running and it refuses or makes the caller wait - so
    /// when the follow-up names the generator that is already generating, the engine stops pulling
    /// from that generation, cancels it, WAITS for it to end, and only then starts the new one.
    /// The waiting happens on a task of its own: it never holds this call, the pump or the audio
    /// thread. THE MUSIC DOES NOT STOP MEANWHILE - everything the old generation had already
    /// settled, up to <see cref="GenerateAhead"/> of it, is still committed and played, and the
    /// take-over still happens at a bar line once the new generation has its own pre-roll. If the
    /// settled music runs out first, that is an ordinary rest. A follow-up naming a DIFFERENT
    /// generator starts at once, alongside the old one, as it always has.
    /// </para>
    /// <para>
    /// A SECOND FOLLOW-UP BEFORE THE FIRST HAS TAKEN OVER REPLACES IT: the first is cancelled where
    /// it stands and nothing of it is ever heard.
    /// </para>
    /// </remarks>
    public void FollowUp(IMusicGenerator generator, MusicRequest request)
    {
        SegmentGeneration replaced;
        SegmentGeneration pending;
        var ending = new List<SegmentGeneration>();

        lock (gate)
        {
            if (disposed || stopped || stream.IsCompleted)
            {
                return;
            }

            replaced = incoming;
            pending = new SegmentGeneration(generator, request, request, null, true,
                new CancellationTokenSource(), stream.TicksPerQuarterNote);
            incoming = pending;

            // WHAT IS ALREADY RUNNING ON THIS VERY GENERATOR: the music that is playing, and a
            // follow-up that has not taken over yet. Both are being cancelled here anyway; what
            // the hand-over adds is waiting for them to let go before asking for anything new.
            AddIfGenerating(ending, current, generator);
            AddIfGenerating(ending, replaced, generator);

            if (ending.Count == 0)
            {
                StartPull(pending);
            }
        }

        StopPulling(replaced);

        if (ending.Count == 0)
        {
            return;
        }

        for (var i = 0; i < ending.Count; i++)
        {
            StopPulling(ending[i]);
        }

        StartWhenTheGeneratorIsFree(pending, ending);
    }

    /// <summary>
    /// Throws away every settled event not yet committed, and everything still being held back.
    /// What is already on the timeline stays there, because it may already have been heard.
    /// </summary>
    /// <remarks>
    /// This is what a follow-up prompt does to the music it is replacing, and it is only possible
    /// because the lookahead lives in memory rather than on the append-only timeline.
    /// </remarks>
    public void DiscardUncommitted()
    {
        lock (gate)
        {
            current.Reorder.DiscardHeld();
            settled.DiscardUncommitted();
        }
    }

    /// <summary>Stops pulling from the generator. What has been committed still plays.</summary>
    public void Stop()
    {
        SegmentGeneration running;
        SegmentGeneration pending;

        lock (gate)
        {
            stopped = true;
            running = current;
            pending = incoming;

            if (timer != null)
            {
                timer.Dispose();
                timer = null;
            }
        }

        StopPulling(running);
        StopPulling(pending);
    }

    /// <summary>Stops everything and gives up what the engine is holding.</summary>
    public void Dispose()
    {
        Stop();

        lock (gate)
        {
            disposed = true;
        }
    }

    // EACH GENERATION HAS ITS OWN, AND NOT A LINKED ONE. A token linked to an engine-wide one
    // registers itself on that one and stays registered, so music that keeps generating for an
    // hour would leave a registration behind for every segment it played.
    private static void StopPulling(SegmentGeneration generation)
    {
        if (generation != null)
        {
            generation.Cancellation.Cancel();
        }
    }

    private static void AddIfGenerating(List<SegmentGeneration> ending, SegmentGeneration generation,
        IMusicGenerator generator)
    {
        // REFERENCE EQUALITY, deliberately: it is ONE INSTANCE that holds the model, so two
        // generators over the same files are two generators and may run at once. A generation that
        // has not been started, or has already ended, is nothing to wait for.
        if (generation != null && generation.Task != null && !generation.Finished &&
            ReferenceEquals(generation.Generator, generator))
        {
            ending.Add(generation);
        }
    }

    private void StartPull(SegmentGeneration generation) =>
        generation.Task = Task.Run(() => PullAsync(generation), CancellationToken.None);

    // OFF THE PUMP AND OFF THE CALLER'S THREAD. Waiting for a model to notice a cancelled token and
    // let go of its turn is work for neither of them: the caller gets its thread back at once and
    // the pump carries on committing the music that has already settled.
    private void StartWhenTheGeneratorIsFree(SegmentGeneration pending,
        List<SegmentGeneration> ending)
    {
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < ending.Count; i++)
            {
                var task = ending[i].Task;

                if (task == null)
                {
                    continue;
                }

                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A pull task reports what went wrong on GenerationError and never faults;
                    // whatever it did, the generator has let go, which is all this waits for.
                }
            }

            lock (gate)
            {
                // A SECOND FOLLOW-UP MAY HAVE ARRIVED while this one waited, and it replaced this
                // one: starting it now would be starting music nobody asked for any more.
                if (disposed || stopped || !ReferenceEquals(incoming, pending))
                {
                    return;
                }

                StartPull(pending);
            }
        }, CancellationToken.None);
    }

    private async Task PullAsync(SegmentGeneration generation)
    {
        var cancellationToken = generation.Cancellation.Token;

        try
        {
            await using var events = generation.Generator
                .GenerateAsync(generation.Request, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (await events.MoveNextAsync().ConfigureAwait(false))
            {
                lock (gate)
                {
                    generation.Accept(events.Current);
                }

                while (!MayPull(generation) && !cancellationToken.IsCancellationRequested)
                {
                    // NOT PULLING IS PAUSING: a generator does no work while nobody asks it for
                    // anything, so the generate-ahead window costs exactly this loop.
                    await Task.Delay(DefaultPumpInterval, timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping is not a failure.
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                generation.Failure = exception;
            }

            // A follow-up that fails must not stop music that is playing perfectly well; the engine
            // drops it at the next pump and reports it here all the same.
            GenerationError = exception;
        }
        finally
        {
            lock (gate)
            {
                generation.Finished = true;
            }
        }
    }

    private bool MayPull(SegmentGeneration generation)
    {
        var pull = CanPull;

        lock (gate)
        {
            if (generation != current)
            {
                // A follow-up builds its own pre-roll flat out: until it has one it cannot take
                // over, and until it takes over it is not what the lead is measured against.
                return true;
            }

            if (pull == null)
            {
                return pullingAllowed;
            }
        }

        return pull();
    }

    private void PumpSafely()
    {
        try
        {
            Pump();
        }
        catch (Exception exception)
        {
            // A timer callback that throws takes the process with it, and the music is not worth
            // that. Whatever went wrong is reported the same way a failing generator is.
            GenerationError = exception;

            lock (gate)
            {
                if (timer != null)
                {
                    timer.Dispose();
                    timer = null;
                }
            }
        }
    }

    // A GAP IS THE MUSIC RUNNING OUT, AND IT CANNOT RUN OUT BEFORE IT HAS STARTED. Every host
    // reports itself starved while it waits for its first pre-roll - there is nothing written yet,
    // so there is nothing to play - and counting that wait would tell a consumer that an ordinary
    // start was a fault. Counting begins at the first pump where music has reached the timeline AND
    // the head is not waiting on it; everything after that is a real gap.
    private void CountStarvation()
    {
        var starved = host.IsStarved || HeadHasRunOutOfMusic();

        if (!musicHasStarted)
        {
            musicHasStarted = !starved && writtenThroughTick >= 0L;
            wasStarved = starved;

            return;
        }

        if (starved && !wasStarved)
        {
            // A gap is counted once, however long the head waits in it.
            starvationGapCount++;
        }

        wasStarved = starved;
    }

    private bool HeadHasRunOutOfMusic()
    {
        if (!KeepsMusicAheadOfTheHead || writtenThroughTick < 0L || stream.IsCompleted)
        {
            return false;
        }

        // A RINGING NOTE MASKS A GAP. The timeline is not starved while a note written with a
        // duration still sounds over the end of the music - its note-off is ahead of the head, so
        // there is something left to play - but the head has run out of MUSIC all the same, which
        // is what a consumer watching this number wants to know.
        return settled.TickAtTime(host.Position) >= writtenThroughTick;
    }

    // NOTHING IS EVER COMMITTED AT OR BEHIND THE PLAY HEAD, AND THE MUSIC ONLY EVER WAITS IN WHOLE
    // BARS. It runs before anything is committed, and it asks two questions. Is something still
    // SOUNDING over the place the music that is coming belongs - which is the only way the head can
    // get past that place at all, because the head never goes beyond the timeline's horizon? And
    // has the head REACHED that place? If both, everything not yet committed - what is waiting,
    // what is still held back, and whatever the generator has yet to write - is moved later by a
    // whole number of bars, far enough to land clear of the head. What has been committed is not
    // touched: it may already have been heard.
    private void KeepTheMusicAheadOfTheHead()
    {
        if (!KeepsMusicAheadOfTheHead)
        {
            return;
        }

        if (current.Finished && current.Reorder.HeldCount == 0 && settled.IsEmpty)
        {
            // The pass is over and everything it wrote has been committed: there is no music to
            // hold back, and the next segment - if there is one - is placed against the head when
            // it starts. Holding here would only put a rest on the end of a finished piece.
            return;
        }

        var anchor = EarliestUncommittedTick();

        if (stream.HorizonTicks <= anchor)
        {
            // NOTHING SOUNDS OVER THE MUSIC THAT IS COMING, so the head cannot get past it: the
            // head never goes beyond the timeline's horizon, so when the music runs out the head
            // HOLDS where it is and the music carries on, in time, whenever it arrives.
            return;
        }

        if (settled.TimeOfTick(anchor) > host.Position)
        {
            // The head has not reached the place that music belongs yet. It may be running towards
            // it under a note that is still sounding - if it gets there before the music does,
            // this runs again, and nothing is committed in between.
            return;
        }

        HoldByWholeBars(anchor);
    }

    private void HoldByWholeBars(long anchor)
    {
        // THE HEAD MAY RUN AS FAR AS THE HORIZON, so the music waits until beyond whichever of the
        // two is further on - which is the same thing as letting the notes that are still sounding
        // ring out, exactly as they were written, before the music carries on.
        var reach = host.Position;
        var horizon = settled.TimeOfTick(stream.HorizonTicks);

        if (horizon > reach)
        {
            reach = horizon;
        }

        var earliest = settled.TickAtTime(reach + HoldMargin());

        // WHOLE BARS OF THE METRE IN FORCE IN THE COMMITTED MUSIC. A metre change inside the music
        // that is waiting travels with that music, so it is not what the rest is counted in; and
        // moving the music by a whole number of bars leaves every note where it was in its bar.
        var ticksPerBar = committedBars.CurrentTicksPerBar;
        var bars = ((earliest - anchor) + ticksPerBar - 1L) / ticksPerBar;

        if (bars < 1L)
        {
            bars = 1L;
        }

        var delta = bars * ticksPerBar;
        var wasAt = settled.TimeOfTick(anchor);

        settled.HoldFrom(anchor, delta);
        current.Reorder.HoldBy(delta);
        settledBars.HoldFrom(anchor, delta);
        HoldRemembered(anchor, delta);
        HoldSeamTicks(anchor, delta);
        HoldSegmentStarts(anchor, delta);

        if (fallbackCommittedThroughTick < (anchor + delta) - 1L)
        {
            // A segment is measured from where its music now begins, so the rest does not count
            // towards the length that decides when a whole segment is ready.
            fallbackCommittedThroughTick = (anchor + delta) - 1L;
        }

        // THE REST IS SILENCE THE ENGINE INSERTED, not music the generator produced: the real-time
        // factor must not improve because the music waited.
        heldSeconds += (settled.TimeOfTick(anchor + delta) - wasAt).TotalSeconds;

        holdCount++;
        heldBarCount += (int)bars;
    }

    private long EarliestUncommittedTick()
    {
        // Where music that has not been committed can begin: what the generator may still write,
        // and whatever is already waiting in front of it.
        var earliest = current.Reorder.HasSettled
            ? current.Reorder.SettledThroughTick + 1L
            : current.Reorder.SegmentStartTick;

        var waiting = settled.NextTick;

        if (waiting >= 0L && waiting < earliest)
        {
            earliest = waiting;
        }

        var held = current.Reorder.EarliestHeldTick;

        if (held >= 0L && held < earliest)
        {
            earliest = held;
        }

        return earliest;
    }

    private TimeSpan HoldMargin() =>
        preroll > StreamingDefaults.MinimumHoldMargin ? preroll : StreamingDefaults.MinimumHoldMargin;

    private void HoldRemembered(long fromTick, long delta)
    {
        // The music is remembered in tick order, and only the part of it that has not been
        // committed can move - everything committed is at a tick before the rest begins.
        for (var i = settledMusic.Count - 1; i >= 0; i--)
        {
            var midiEvent = settledMusic[i];

            if (midiEvent.AbsoluteTime < fromTick)
            {
                return;
            }

            settledMusic[i] = MusicEventPlacement.At(midiEvent, midiEvent.AbsoluteTime + delta);
        }
    }

    private void HoldSegmentStarts(long fromTick, long delta)
    {
        // A segment whose music has been moved starts where its music now starts, so that what a
        // render reports as its seams is where the seams really are.
        for (var i = segmentStartTicks.Count - 1; i >= 0; i--)
        {
            if (segmentStartTicks[i] < fromTick)
            {
                return;
            }

            segmentStartTicks[i] += delta;
        }
    }

    private void HoldSeamTicks(long fromTick, long delta)
    {
        var count = seamTicks.Count;

        for (var i = 0; i < count; i++)
        {
            var tick = seamTicks.Dequeue();

            seamTicks.Enqueue(tick >= fromTick ? tick + delta : tick);
        }
    }

    private void Release()
    {
        foreach (var midiEvent in current.Reorder.TakeReleased())
        {
            settledState.Observe(midiEvent);
            settledBars.Observe(midiEvent);
            Remember(midiEvent);
            settled.Add(midiEvent);
        }

        settled.SettleThrough(current.Reorder.SettledThroughTick);
    }

    private void Measure()
    {
        var timestamp = timeProvider.GetTimestamp();
        var music = MusicSeconds();

        if (!hasSample || music <= 0.0)
        {
            // NOTHING IS MEASURED UNTIL THERE IS MUSIC. A generator with a model behind it may
            // spend seconds LOADING before it writes a note, and counting that against its rate
            // would make every piece start by deciding the machine cannot keep up.
            hasSample = true;
            lastSampleTimestamp = timestamp;
            lastMusicSeconds = music;

            return;
        }

        var wall = timeProvider.GetElapsedTime(lastSampleTimestamp, timestamp).TotalSeconds;
        var produced = music - lastMusicSeconds;

        lastSampleTimestamp = timestamp;
        lastMusicSeconds = music;

        if (!wasPulling || wall <= 0.0)
        {
            // Time the engine spent paused because it was far enough ahead is not time the
            // generator spent failing to keep up.
            return;
        }

        // A MOVING AVERAGE WEIGHTED TOWARDS THE RECENT PAST: both sums decay with a time constant
        // measured in seconds of GENERATING, so the factor answers "can it keep up now" rather
        // than "how did the whole session average out". A model's rate falls as a piece grows.
        var decay = Math.Exp(-wall / StreamingDefaults.RealTimeFactorWindow.TotalSeconds);

        producedSeconds = (producedSeconds * decay) + (produced > 0.0 ? produced : 0.0);
        generatingSeconds = (generatingSeconds * decay) + wall;
        measuredGeneratingSeconds += wall;
    }

    // NULL IS "NOT MEASURED YET", AND IT IS NOT A NUMBER ON PURPOSE. A factor of zero would read as
    // "this machine produces no music at all", which is the one answer a game loop must not be given
    // every time a piece starts.
    private double? MeasuredRealTimeFactor()
    {
        if (generatingSeconds <= 0.0 ||
            measuredGeneratingSeconds < StreamingDefaults.MinimumMeasuredGenerating.TotalSeconds)
        {
            return null;
        }

        return producedSeconds / generatingSeconds;
    }

    private void ChooseDeliveryMode()
    {
        if (!FallsBackToSegmentAtATime)
        {
            // An offline render never falls back, however slow the generator is: there is no play
            // head to protect and no such thing as stalling mid-phrase in a file.
            return;
        }

        var factor = MeasuredRealTimeFactor();

        if (!factor.HasValue)
        {
            return;
        }

        if (!fallbackActive && factor.Value < StreamingDefaults.FallbackEngagesBelow)
        {
            fallbackActive = true;
            fallbackCommittedThroughTick = Math.Max(fallbackCommittedThroughTick, committedThroughTick);
        }
        else if (fallbackActive && factor.Value > StreamingDefaults.FallbackLeavesAbove)
        {
            fallbackActive = false;
        }
    }

    private void Commit()
    {
        long limit;

        if (fallbackActive)
        {
            limit = WholeSegmentLimit();

            if (limit < 0L)
            {
                // Nothing of this segment is heard until the whole of it is ready.
                return;
            }

            fallbackCommittedThroughTick = limit;
        }
        else
        {
            limit = settled.TickAtTime(host.Position + CommitWindow);
        }

        CommitThrough(limit);
    }

    private void CommitThrough(long limit)
    {
        foreach (var midiEvent in settled.TakeThrough(limit))
        {
            while (seamTicks.Count > 0 && seamTicks.Peek() < midiEvent.AbsoluteTime)
            {
                seamTicks.Dequeue();
            }

            if (seamTicks.Count > 0 && seamTicks.Peek() == midiEvent.AbsoluteTime &&
                committedState.IsRedundant(midiEvent))
            {
                // THE SEAM EMITS NO SPURIOUS SET OF CHANGES: a new segment restating the tempo, the
                // metre, the key or an instrument that has not changed says nothing, so nothing is
                // said. What HAS changed goes through untouched.
                continue;
            }

            committedState.Observe(midiEvent);
            committedBars.Observe(midiEvent);

            // The part is voiced BEFORE the event can reach a synthesizer, never after.
            voicer.Observe(midiEvent);
            stream.Append(midiEvent);
        }

        var written = Math.Min(settled.SettledThroughTick, limit);

        CarryHorizon(written);

        if (written > writtenThroughTick)
        {
            writtenThroughTick = written;
        }

        if (limit > committedThroughTick)
        {
            committedThroughTick = limit;
        }
    }

    private void Remember(MidiEvent midiEvent)
    {
        settledMusic.Add(midiEvent);

        var keepFrom = midiEvent.AbsoluteTime - (TailTicks() * 2L);

        if (keepFrom <= 0L || settledMusic.Count < 256 || settledMusic[0].AbsoluteTime >= keepFrom)
        {
            return;
        }

        var drop = 0;

        while (drop < settledMusic.Count && settledMusic[drop].AbsoluteTime < keepFrom)
        {
            drop++;
        }

        settledMusic.RemoveRange(0, drop);
    }

    private long TailTicks() => settledBars.CurrentTicksPerBar * StreamingDefaults.ContinuationTailBars;

    private void CarryHorizon(long throughTick)
    {
        if (throughTick <= stream.HorizonTicks)
        {
            return;
        }

        // Settled silence is settled music. Without this the timeline's horizon would stop at the
        // last note before a long rest, and a player would call that starvation.
        stream.Append(new TextEvent(string.Empty, MetaEventType.TextEvent, throughTick));
    }

    private long WholeSegmentLimit()
    {
        var settledTick = current.Reorder.SettledThroughTick;

        if (settledTick <= fallbackCommittedThroughTick)
        {
            return -1L;
        }

        if (current.Finished && current.Reorder.HeldCount == 0)
        {
            // The whole pass has arrived: all of it plays, and the rest comes after it.
            return settledTick;
        }

        var barLine = settledBars.BarLineAtOrBefore(settledTick);

        if (barLine <= fallbackCommittedThroughTick)
        {
            return -1L;
        }

        var from = settled.TimeOfTick(fallbackCommittedThroughTick < 0L ? 0L : fallbackCommittedThroughTick);

        return settled.TimeOfTick(barLine) - from >= GenerateAhead ? barLine : -1L;
    }

    private TimeSpan CurrentLead()
    {
        var lead = settled.TimeOfTick(current.Reorder.SettledThroughTick) - host.Position;

        return lead < TimeSpan.Zero ? TimeSpan.Zero : lead;
    }

    private void UpdatePullPolicy()
    {
        var external = CanPull;

        if (fallbackActive)
        {
            // In this mode the generate-ahead window IS the segment: keep pulling until a whole one
            // is ready, then stop until it has been handed over.
            pullingAllowed = WholeSegmentLimit() < 0L;
        }
        else
        {
            var lead = CurrentLead();
            var resume = TimeSpan.FromTicks((long)(GenerateAhead.Ticks *
                StreamingDefaults.GenerateAheadResumeFactor));

            if (lead >= GenerateAhead)
            {
                pullingAllowed = false;
            }
            else if (lead <= resume)
            {
                pullingAllowed = true;
            }
        }

        // What the REAL decision was, not what the engine's own policy would have been: a caller
        // holding the pull loop back is not a generator failing to keep up, and must not be
        // measured as one.
        wasPulling = (external == null ? pullingAllowed : external()) && !current.Finished;
    }

    private void TakeOverIfFollowUpIsReady(ref SegmentGeneration replaced)
    {
        var pending = incoming;

        if (pending == null)
        {
            return;
        }

        if (pending.Failure != null)
        {
            // The music that is playing carries on; the failure is on GenerationError.
            incoming = null;

            return;
        }

        if (!pending.Finished && pending.WaitingSettledTime < preroll)
        {
            return;
        }

        if (pending.WaitingCount == 0)
        {
            // A follow-up that produced nothing at all is not something to switch to.
            if (pending.Finished)
            {
                incoming = null;
            }

            return;
        }

        // THE NEXT BAR LINE STRICTLY BEYOND WHAT IS COMMITTED. The old music plays right up to it;
        // nothing of the old music is ever heard past it.
        //
        // IT IS MEASURED BY HOW FAR THE COMMIT HAS REACHED, not by the timeline's horizon. A note
        // written with a duration pushes the horizon out to where that note ENDS, so a held pad
        // would otherwise put a prompt change off for as long as it rings - and a note that runs
        // across the seam is exactly what is meant to happen: it keeps its whole length.
        var switchTick = committedBars.BarLineAtOrAfter(EarliestSwitchTick());

        CommitThrough(switchTick - 1L);

        replaced = current;
        current.Reorder.DiscardHeld();
        settled.DiscardUncommitted();
        settled.RewindSettledTo(switchTick - 1L);

        // The lookahead is gone, so everything that only knew about the lookahead goes back to
        // knowing what is playing and nothing more.
        settledState.CopyFrom(committedState);
        settledBars.CopyFrom(committedBars);
        settledMusic.RemoveAll(midiEvent => midiEvent.AbsoluteTime >= switchTick);
        seamTicks.Clear();
        seamTicks.Enqueue(switchTick);

        pending.Promote(switchTick);
        current = pending;
        incoming = null;
        segmentCount++;
        segmentStartTicks.Add(switchTick);
        fallbackCommittedThroughTick = switchTick - 1L;
    }

    private long EarliestSwitchTick()
    {
        var beyondTheCommittedMusic = committedThroughTick + 1L;

        if (!KeepsMusicAheadOfTheHead)
        {
            return beyondTheCommittedMusic;
        }

        // AND NEVER AT OR BEHIND THE HEAD, so that this rule and the one that holds music back
        // cannot disagree. In a healthy stream what is committed already runs a commit window
        // ahead of the head, which is further than this, so it changes nothing; after a rest or a
        // stall the head may be past what is committed, and the switch goes in front of the head
        // instead. It is measured against THE HEAD and not the horizon: a note ringing across the
        // seam is what is meant to happen, and waiting for it to finish would put a prompt change
        // off for as long as it rang.
        var aheadOfTheHead = settled.TickAtTime(host.Position + HoldMargin());

        return aheadOfTheHead > beyondTheCommittedMusic ? aheadOfTheHead : beyondTheCommittedMusic;
    }

    private void ContinueOrComplete()
    {
        // THE PASS IS OVER once the generator has stopped and everything it wrote has been let out
        // of the reorder buffer. It does NOT have to have been committed: the lookahead lives in
        // the settled buffer, and making the next segment wait for it to drain would cap the
        // lookahead at one segment and make the generate-ahead window mean nothing.
        if (!current.Finished || current.Reorder.HeldCount > 0)
        {
            return;
        }

        // It is the CURRENT generation's failure that ends the music. A follow-up that failed is
        // reported and forgotten; what is playing is still playing.
        if (current.Failure == null && !stopped && EndOfPiece == EndOfPiecePolicy.KeepGenerating &&
            !AFollowUpIsTakingOverFromThisGenerator() && StartNextSegment())
        {
            return;
        }

        if (incoming != null)
        {
            // A follow-up is on its way: the music has run out, which is silence, not the end.
            return;
        }

        if (!settled.IsEmpty)
        {
            // There is music still to be heard, so the timeline is not finished with.
            return;
        }

        if (current.Failure == null && settled.SettledThroughTick > stream.HorizonTicks)
        {
            // Still a settled stretch of silence to carry across before the piece is over.
            return;
        }

        stream.Complete();
    }

    // A HAND-OVER IS NOT A PLACE TO ASK FOR MORE OF THE OLD MUSIC. The follow-up is waiting for
    // exactly this generator to be free, so another segment of what it was playing would be the
    // second generation the wait exists to avoid - and it would be dropped at the switch anyway.
    private bool AFollowUpIsTakingOverFromThisGenerator() =>
        incoming != null && ReferenceEquals(incoming.Generator, current.Generator);

    private bool StartNextSegment()
    {
        // A SEGMENT STARTS ON A BAR LINE, NEVER MID-BAR.
        var startTick = settledBars.BarLineAtOrAfter(current.Reorder.SettledThroughTick);

        if (startTick <= current.Reorder.SegmentStartTick)
        {
            // A pass that produced no music would otherwise be asked for again for ever.
            return false;
        }

        var next = new SegmentGeneration(current.Generator, current.BaseRequest,
            NextSegmentRequest(startTick), new ReorderBuffer(startTick), true,
            new CancellationTokenSource(), stream.TicksPerQuarterNote);

        current = next;
        segmentCount++;
        segmentStartTicks.Add(startTick);
        seamTicks.Enqueue(startTick);
        StartPull(next);

        return true;
    }

    private MusicRequest NextSegmentRequest(long startTick)
    {
        var request = current.BaseRequest.Clone();
        var honours = current.Generator.Honours;

        if ((honours & MusicRequestFeatures.Continuation) == MusicRequestFeatures.Continuation)
        {
            // THE MUSIC SO FAR IN VIEW: the biggest seam lever there is.
            var continuation = settledState.ToContinuation();
            var tailEnd = TailEndTick(startTick);
            var tailTicks = TailTicks();

            continuation.Tail = ContinuationTail.Build(settledMusic, tailEnd, tailTicks,
                stream.TicksPerQuarterNote);

            // WHERE THE TAIL ENDS IS NOT WHERE ITS LAST EVENT IS. A generator writes its segment
            // from the tail's END, and the tail runs to the bar line the next segment starts at.
            continuation.TailTicks = continuation.Tail == null
                ? 0L
                : (tailEnd < tailTicks ? tailEnd : tailTicks);
            request.Continuation = continuation;
        }

        if (request.Seed.HasValue &&
            (honours & MusicRequestFeatures.Seed) == MusicRequestFeatures.Seed)
        {
            // KEEP THE CHARACTER, CHANGE THE SEED. The sampling controls are left exactly as they
            // are, so the music does not change its manner half-way through a piece.
            request.Seed = SegmentSeed.Derive(current.BaseRequest.Seed.Value, segmentCount + 1);
        }

        return request;
    }

    private long TailEndTick(long startTick)
    {
        var lastMusic = ContinuationTail.LastMusicTick(settledMusic);

        if (lastMusic < 0L)
        {
            return startTick;
        }

        // THE TAIL IS MUSIC, NOT SILENCE. Where the music has been held back, the bars immediately
        // before a new segment are the REST that was inserted, so the tail ends at the bar line
        // after the last music there really is. With nothing held back the two are the same tick.
        var endOfTheMusic = settledBars.BarLineAtOrAfter(lastMusic + 1L);

        return endOfTheMusic < startTick ? endOfTheMusic : startTick;
    }

    private double MusicSeconds()
    {
        // The rests the engine inserts to keep the music ahead of the head are not music the
        // generator produced, so they are taken back off again before anything is measured.
        var seconds = settled.TimeOfTick(current.Reorder.SettledThroughTick).TotalSeconds - heldSeconds;

        return seconds < 0.0 ? 0.0 : seconds;
    }
}
