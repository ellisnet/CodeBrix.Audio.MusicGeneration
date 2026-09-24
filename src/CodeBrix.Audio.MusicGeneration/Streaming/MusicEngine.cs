using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendering;
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
/// IT CAN START A SEGMENT FRESH, AND CROSSFADE INTO IT. <see cref="SegmentPriming"/> decides
/// whether each of those segments is primed with the music so far or is a fresh piece. With
/// <see cref="SeamCrossfade"/> set and a <see cref="Mixer"/> to play through, a FRESH segment is
/// generated alongside the end of the outgoing piece and placed early, so the two overlap and are
/// crossfaded - see <see cref="SeamCrossfadeMixer"/> for the sound and PlaceTheFreshSegmentIfDue
/// for the timing.
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
    private readonly List<MidiEvent> crossfadedTails = new List<MidiEvent>();
    private readonly List<KeyValuePair<long, int>> writtenCues = new List<KeyValuePair<long, int>>();
    private readonly List<SegmentGeneration> tempoDecisions = new List<SegmentGeneration>();
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

    private RenditionVoicer voicer;
    private SegmentGeneration current;
    private SegmentGeneration incoming;

    // A FRESH SEGMENT THAT WILL BE CROSSFADED is generated before it has a place: where it starts
    // depends on how much of it there is by the time the outgoing music has to be committed. Until
    // then it waits here, with the bar line the outgoing piece ends at.
    private SegmentGeneration freshIncoming;
    private long freshSeamEndTick;

    // A crossfade that has been worked out and handed to the mixer, whose cue has not reached the
    // timeline yet.
    private SeamCrossfadePlan pendingPlan;
    private int lastCue;
    private RenditionVoicer spareVoicer;

    // CROSSFADE PREPARATION IS KEPT OFF THE CRITICAL PATH: it is asked for at Play, begins only once
    // the music has started, runs on a background thread of its own below normal priority, and
    // the generation rate is not measured over any interval it touched.
    private bool preparationWanted;
    private bool preparationStarted;
    private int preparationActivity;
    private int preparationInFlight;
    private int preparationActivitySeen;
    private double preparationIgnoredSeconds;
    private readonly TaskCompletionSource preparation =
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private int repromptCount;
    private int primedSegmentCount;
    private int freshSegmentCount;
    private int crossfadeCount;
    private int shortenedCrossfadeCount;
    private int skippedLeadingBarCount;
    private int emptyPassCount;
    private int sessionTempo;
    private int carriedTempoCount;
    private int adoptedTempoCount;
    private int consecutiveEmptyPasses;
    private int failedPassCount;
    private readonly List<string> failures = new List<string>();
    private bool gaveUp;
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

    /// <summary>
    /// How each segment asked for at the end of a pass starts. The engine's default is
    /// <see cref="MusicGeneration.SegmentPriming.Primed"/>, which is what it has always done.
    /// </summary>
    public SegmentPriming SegmentPriming { get; set; } = SegmentPriming.Primed;

    /// <summary>
    /// How long the outgoing and incoming pieces overlap at a FRESH seam. Zero - the default - is a
    /// hard join at a bar line. It only takes effect with a <see cref="Mixer"/> to play through and
    /// a <see cref="CreateVoicer"/> to build the incoming piece's instruments with.
    /// </summary>
    public TimeSpan SeamCrossfade { get; set; } = TimeSpan.Zero;

    /// <summary>The shape a seam crossfade follows.</summary>
    public MusicFadeCurve SeamCrossfadeCurve { get; set; } = MusicFadeCurve.EqualPower;

    /// <summary>
    /// The synthesizer the host plays through when fresh seams are crossfaded, or null when they
    /// are not. It is what the timeline's cue switches, and what mixes the two pieces.
    /// </summary>
    public SeamCrossfadeMixer Mixer { get; set; }

    /// <summary>
    /// Builds a voicer - a routing synthesizer with its own instruments, over the same instrument
    /// library and rendition as the first - for the incoming piece at a crossfaded seam. It is
    /// called on the pump thread, never on the audio thread.
    /// </summary>
    public Func<RenditionVoicer> CreateVoicer { get; set; }

    /// <summary>
    /// Whether the engine keeps what an offline render needs to put the crossfaded music back into
    /// the MIDI it hands over: every outgoing piece's last moments, and every cue it wrote. A live
    /// session leaves it off, because it would grow for as long as the music played.
    /// </summary>
    public bool KeepsCrossfadeRecord { get; set; }

    /// <summary>
    /// The voicer the music reaching the timeline is voiced through: the first one until a
    /// crossfaded seam's cue is committed, and the incoming piece's from then on.
    /// </summary>
    public RenditionVoicer Voicer
    {
        get { lock (gate) { return voicer; } }
    }

    /// <summary>
    /// What kind of segment is being generated now: the newest generation the engine is pulling
    /// from, which is the music a listener hears next.
    /// </summary>
    public MusicSegmentKind GeneratingSegmentKind
    {
        get
        {
            lock (gate)
            {
                var newest = incoming ?? freshIncoming ?? current;

                return newest.Kind;
            }
        }
    }

    /// <summary>How many segments asked for at the end of a pass were primed with the music so far.</summary>
    public int PrimedSegmentCount
    {
        get { lock (gate) { return primedSegmentCount; } }
    }

    /// <summary>How many segments asked for at the end of a pass were fresh pieces.</summary>
    public int FreshSegmentCount
    {
        get { lock (gate) { return freshSegmentCount; } }
    }

    /// <summary>How many fresh seams have been crossfaded, shortened ones included.</summary>
    public int CrossfadeCount
    {
        get { lock (gate) { return crossfadeCount; } }
    }

    /// <summary>
    /// How many fresh seams were given a shorter crossfade than was asked for - down to none at
    /// all - because the incoming piece had not generated enough in time.
    /// </summary>
    public int ShortenedCrossfadeCount
    {
        get { lock (gate) { return shortenedCrossfadeCount; } }
    }

    /// <summary>
    /// Whether fresh pieces play at their own tempo or at the session tempo. The engine's default
    /// is <see cref="SessionTempoPolicy.Adopt"/>, which is what it has always done.
    /// </summary>
    public SessionTempoPolicy TempoPolicy { get; set; } = SessionTempoPolicy.Adopt;

    /// <summary>
    /// How far, as a fraction, a fresh piece's opening tempo may be from the session tempo and
    /// still be adopted under <see cref="SessionTempoPolicy.CarryOutsideBand"/>.
    /// </summary>
    public double TempoBand { get; set; } = MusicGenerationOptions.DefaultTempoBand;

    /// <summary>The session tempo given up front, in beats per minute, or null.</summary>
    public double? SessionBeatsPerMinute { get; set; }

    /// <summary>
    /// The session tempo in beats per minute, or null while there is none - always null under
    /// <see cref="SessionTempoPolicy.Adopt"/>, which keeps no session tempo.
    /// </summary>
    public double? SessionTempo
    {
        get { lock (gate) { return sessionTempo > 0 ? 60000000.0 / sessionTempo : null; } }
    }

    /// <summary>
    /// Every session-tempo decision taken, as one line each - where the piece starts, the tempo it
    /// was written at, and whether it was carried or adopted. Empty unless
    /// <see cref="KeepsCrossfadeRecord"/> is set.
    /// </summary>
    public IReadOnlyList<string> TempoDecisions
    {
        get
        {
            lock (gate)
            {
                var lines = new List<string>(tempoDecisions.Count);
                var session = sessionTempo > 0 ? 60000000.0 / sessionTempo : 0.0;

                foreach (var generation in tempoDecisions)
                {
                    if (generation.Reorder == null)
                    {
                        // Decided, but never placed: the music ended before it was needed.
                        continue;
                    }

                    var tick = generation.Reorder.SegmentStartTick;
                    var incoming = 60000000.0 / generation.IncomingTempo;

                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        generation.Kind == MusicSegmentKind.FirstPiece && !generation.CarriesSessionTempo
                            ? "Piece at tick {0}: {1:0.0} bpm sets the session tempo."
                            : generation.CarriesSessionTempo
                                ? "Piece at tick {0}: written at {1:0.0} bpm, carried to the session tempo of {2:0.0} bpm."
                                : "Piece at tick {0}: written at {1:0.0} bpm, adopted (within the band of the session tempo of {2:0.0} bpm).",
                        tick, incoming, session));
                }

                return lines;
            }
        }
    }

    /// <summary>How many fresh pieces were played at the session tempo rather than their own.</summary>
    public int CarriedTempoCount
    {
        get { lock (gate) { return carriedTempoCount; } }
    }

    /// <summary>
    /// How many fresh pieces were close enough to the session tempo to keep their own, under
    /// <see cref="SessionTempoPolicy.CarryOutsideBand"/>.
    /// </summary>
    public int AdoptedTempoCount
    {
        get { lock (gate) { return adoptedTempoCount; } }
    }

    /// <summary>How many passes failed part-way and were followed by a fresh piece.</summary>
    public int FailedPassCount
    {
        get { lock (gate) { return failedPassCount; } }
    }

    /// <summary>
    /// One line for every pass that failed and was followed by a fresh piece, with what it failed
    /// with. Empty unless <see cref="KeepsCrossfadeRecord"/> is set.
    /// </summary>
    public IReadOnlyList<string> RetriedFailures
    {
        get { lock (gate) { return failures.ToArray(); } }
    }

    /// <summary>
    /// How many passes produced no music at all and were asked for again as a fresh piece.
    /// </summary>
    public int EmptyPassCount
    {
        get { lock (gate) { return emptyPassCount; } }
    }

    /// <summary>
    /// How many silent opening bars of incoming fresh pieces were skipped at crossfaded seams, so
    /// that the fade was into music rather than into silence.
    /// </summary>
    public int SkippedLeadingBarCount
    {
        get { lock (gate) { return skippedLeadingBarCount; } }
    }

    /// <summary>
    /// Every outgoing piece's last moments that were played beside the incoming piece rather than
    /// on the timeline, at the ticks they were written at. Empty unless
    /// <see cref="KeepsCrossfadeRecord"/> is set.
    /// </summary>
    public IReadOnlyList<MidiEvent> CrossfadedTailEvents
    {
        get { lock (gate) { return crossfadedTails.ToArray(); } }
    }

    /// <summary>
    /// Every cue written to the timeline, as its tick and the number it carried. Empty unless
    /// <see cref="KeepsCrossfadeRecord"/> is set.
    /// </summary>
    public IReadOnlyList<KeyValuePair<long, int>> WrittenCues
    {
        get { lock (gate) { return writtenCues.ToArray(); } }
    }

    /// <summary>One line describing where the engine stands, for a watchdog or a log.</summary>
    /// <returns>The line.</returns>
    internal string DescribeState()
    {
        lock (gate)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "head {0:0.000}s; current {1} finished={2} held={3} tempoPending={4} decided={5} " +
                "reorderSettled={6}; settled={7} waiting={8}; committed={9} written={10}; " +
                "fresh={11}{12}; pendingPlan={13}; pulling={14}; completed={15}; failure={16}",
                host.Position.TotalSeconds, current.Kind, current.Finished, current.Reorder.HeldCount,
                current.TempoPending.Count, current.TempoDecided, current.Reorder.SettledThroughTick,
                settled.SettledThroughTick, settled.Count, committedThroughTick, writtenThroughTick,
                freshIncoming != null,
                freshIncoming == null ? string.Empty : string.Format(CultureInfo.InvariantCulture,
                    " (finished={0} waiting={1} settledTime={2:0.0}s note={3})", freshIncoming.Finished,
                    freshIncoming.WaitingCount, freshIncoming.WaitingSettledTime.TotalSeconds,
                    freshIncoming.HasANoteWaiting),
                pendingPlan == null ? "none" : pendingPlan.CueTick.ToString(CultureInfo.InvariantCulture),
                wasPulling, stream.IsCompleted,
                current.Failure == null ? "none" : current.Failure.GetType().Name + ": " + current.Failure.Message);
        }
    }

    /// <summary>Whether a fresh segment is being generated to be crossfaded but has no place yet.</summary>
    public bool HasPendingFreshSegment
    {
        get { lock (gate) { return freshIncoming != null; } }
    }

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
        SegmentGeneration abandoned = null;

        lock (gate)
        {
            if (disposed || stream.IsCompleted)
            {
                return;
            }

            CountStarvation();
            StartPreparingCrossfadesOnceTheMusicHasStarted();
            Release();
            KeepTheMusicAheadOfTheHead();
            TakeOverIfFollowUpIsReady(ref replaced, ref abandoned);
            PlaceTheFreshSegmentIfDue();
            Measure();
            ChooseDeliveryMode();
            Commit();
            UpdatePullPolicy();
            ContinueOrComplete();
        }

        // OUTSIDE THE LOCK. Cancelling runs whatever is registered on the token there and then,
        // and what is registered on this one is a pull loop that takes this very lock.
        StopPulling(replaced);
        StopPulling(abandoned);
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
                new CancellationTokenSource(), stream.TicksPerQuarterNote)
            {
                Kind = MusicSegmentKind.FollowUp
            };
            incoming = pending;

            // WHAT IS ALREADY RUNNING ON THIS VERY GENERATOR: the music that is playing, and a
            // follow-up that has not taken over yet. Both are being cancelled here anyway; what
            // the hand-over adds is waiting for them to let go before asking for anything new.
            AddIfGenerating(ending, current, generator);
            AddIfGenerating(ending, replaced, generator);
            AddIfGenerating(ending, freshIncoming, generator);

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
        SegmentGeneration fresh;

        lock (gate)
        {
            stopped = true;
            running = current;
            pending = incoming;
            fresh = freshIncoming;

            if (timer != null)
            {
                timer.Dispose();
                timer = null;
            }
        }

        StopPulling(running);
        StopPulling(pending);
        StopPulling(fresh);
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
            bool retried;

            lock (gate)
            {
                generation.Failure = exception;
                retried = IsRetriedAfterFailure(generation);
            }

            // A follow-up that fails must not stop music that is playing perfectly well; the engine
            // drops it at the next pump and reports it here all the same. A pass the engine will
            // ask for again is not an error yet - only giving up is.
            if (!retried)
            {
                GenerationError = exception;
            }
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
            if (generation == freshIncoming)
            {
                // A FRESH SEGMENT WAITING FOR ITS PLACE pulls until it has enough to be placed
                // with the whole crossfade, and a little more - but not without end, because it is
                // not what the lead is measured against until it is placed.
                return generation.WaitingSettledTime < FreshSegmentPullLimit();
            }

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
        HoldTheCrossfade(anchor, delta);

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

    private void HoldTheCrossfade(long fromTick, long delta)
    {
        // A crossfade that is still to come moves with the music it joins, like every other seam.
        // The outgoing piece's last moments are timed from the cue, so they move with it.
        if (freshIncoming != null && freshSeamEndTick >= fromTick)
        {
            freshSeamEndTick += delta;
        }

        if (pendingPlan != null && pendingPlan.CueTick >= fromTick)
        {
            pendingPlan.CueTick += delta;
            pendingPlan.StartTick += delta;
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
        if (current.Finished && current.Reorder.HeldCount > 0)
        {
            // THE PASS IS OVER: whatever it still holds can never be settled by it any more.
            current.Reorder.SettleEverythingHeld();
        }

        var released = AtTheSessionTempo(current, current.Reorder.TakeReleased(), out var holding);

        foreach (var midiEvent in released)
        {
            settledState.Observe(midiEvent);
            settledBars.Observe(midiEvent);
            Remember(midiEvent);
            settled.Add(midiEvent);
        }

        if (!holding)
        {
            settled.SettleThrough(current.Reorder.SettledThroughTick);
        }
    }

    // THE SESSION TEMPO, applied as the music is released - before anything else sees it, so the
    // tempo map, the bar grid, the continuation memory and the timeline all only ever see the tempo
    // the music is really played at. It changes nothing unless a policy other than Adopt is set,
    // and only touches the first piece and fresh segments.
    private IReadOnlyList<MidiEvent> AtTheSessionTempo(SegmentGeneration generation,
        IReadOnlyList<MidiEvent> released, out bool holding)
    {
        holding = false;

        if (TempoPolicy == SessionTempoPolicy.Adopt ||
            (generation.Kind != MusicSegmentKind.FirstPiece && generation.Kind != MusicSegmentKind.Fresh))
        {
            return released;
        }

        var start = generation.Reorder.SegmentStartTick;

        if (!generation.TempoDecided)
        {
            // THE DECISION WAITS FOR THE FIRST NOTE. The tempo that matters is the one in force
            // where the piece first SOUNDS: a model may state its tempo a bar in, after silence, or
            // state a default at its first tick and its real tempo later. Everything before the
            // first note is held back - nothing of it is settled yet - until the decision is taken.
            generation.TempoPending.AddRange(released);

            var passIsOver = generation.Finished && generation.Reorder.HeldCount == 0;

            // NEVER AN UNBOUNDED WAIT. The decision is taken without a note when the pass is over,
            // when nothing more is being pulled from it (the generate-ahead window is full, or a
            // render has all it needs) - the note it waits for would never come - or when the piece
            // has settled a whole phrase without one.
            var noMoreIsComing = !IsPulling(generation);
            var longSilence = generation.Reorder.SettledThroughTick - start >=
                              StreamingDefaults.TempoDecisionBars * settledBars.CurrentTicksPerBar;

            if (!ContainsANote(generation.TempoPending) && !passIsOver && !noMoreIsComing && !longSilence)
            {
                holding = generation.TempoPending.Count > 0 || !generation.Reorder.HasSettled;

                return Array.Empty<MidiEvent>();
            }

            DecideTheTempo(generation, TempoAtTheFirstNote(generation.TempoPending));
            released = generation.TempoPending.ToArray();
            generation.TempoPending.Clear();
        }

        if (!generation.CarriesSessionTempo || released.Count == 0)
        {
            return released;
        }

        var kept = new List<MidiEvent>(released.Count + 1);

        if (!generation.SessionTempoStated)
        {
            // The session tempo, stated where the piece starts, in front of everything else there.
            kept.Add(new TempoEvent(sessionTempo, start));
            generation.SessionTempoStated = true;
        }

        for (var i = 0; i < released.Count; i++)
        {
            // A CARRIED PIECE HAS ONE TEMPO: every tempo event of its own is dropped.
            if (released[i] is not TempoEvent)
            {
                kept.Add(released[i]);
            }
        }

        return kept;
    }

    // Whether the generation is still being pulled from: what the pull loop is currently allowed.
    private bool IsPulling(SegmentGeneration generation)
    {
        if (generation != current)
        {
            return true;
        }

        var pull = CanPull;

        return pull == null ? pullingAllowed : pull();
    }

    private static bool ContainsANote(List<MidiEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (MidiEvent.IsNoteOn(events[i]))
            {
                return true;
            }
        }

        return false;
    }

    // The tempo in force where the first note sounds: the last tempo event at or before it, or null
    // when the piece states none there (the tempo already playing is then what it plays at).
    internal static int? TempoAtTheFirstNote(IReadOnlyList<MidiEvent> events)
    {
        var firstNote = long.MaxValue;

        for (var i = 0; i < events.Count; i++)
        {
            if (MidiEvent.IsNoteOn(events[i]) && events[i].AbsoluteTime < firstNote)
            {
                firstNote = events[i].AbsoluteTime;
            }
        }

        int? tempo = null;
        var tempoTick = long.MinValue;

        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is TempoEvent candidate && candidate.AbsoluteTime <= firstNote &&
                candidate.AbsoluteTime >= tempoTick)
            {
                tempo = candidate.MicrosecondsPerQuarterNote;
                tempoTick = candidate.AbsoluteTime;
            }
        }

        return tempo;
    }

    // THE DECISION, taken once per piece: from its opening tempo (null when it states none at its
    // first tick, which is the tempo already playing). Callers hold the gate.
    private void DecideTheTempo(SegmentGeneration generation, int? opening)
    {
        generation.TempoDecided = true;
        generation.IncomingTempo = opening ?? CurrentTempo();

        if (KeepsCrossfadeRecord)
        {
            tempoDecisions.Add(generation);
        }

        if (sessionTempo <= 0)
        {
            var given = SessionBeatsPerMinute ??
                        (generation.Request.Intent == null ? null : generation.Request.Intent.BeatsPerMinute);

            if (given.HasValue && given.Value > 0.0)
            {
                // GIVEN UP FRONT: it is the session tempo from the first note, imposed on the first
                // piece the same way it is on every carried one.
                sessionTempo = (int)Math.Round(60000000.0 / given.Value);
                generation.CarriesSessionTempo = generation.Kind == MusicSegmentKind.FirstPiece ||
                                                 TempoPolicy == SessionTempoPolicy.Carry;

                if (generation.Kind == MusicSegmentKind.FirstPiece)
                {
                    return;
                }
            }
            else
            {
                // THE FIRST PIECE SETS IT, at its own opening tempo.
                sessionTempo = opening ?? CurrentTempo();

                return;
            }
        }

        if (TempoPolicy == SessionTempoPolicy.Carry)
        {
            generation.CarriesSessionTempo = true;
            carriedTempoCount++;

            return;
        }

        var own = opening ?? CurrentTempo();
        var ratio = (double)sessionTempo / own;

        if (Math.Abs(ratio - 1.0) <= TempoBand)
        {
            generation.CarriesSessionTempo = false;
            adoptedTempoCount++;
        }
        else
        {
            generation.CarriesSessionTempo = true;
            carriedTempoCount++;
        }
    }

    private int CurrentTempo()
    {
        var bpm = settledState.BeatsPerMinute;

        return bpm.HasValue ? (int)Math.Round(60000000.0 / bpm.Value) : 500000;
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

        var activity = Volatile.Read(ref preparationActivity);
        var preparationTouchedThisInterval = activity != preparationActivitySeen ||
                                             Volatile.Read(ref preparationInFlight) > 0;

        preparationActivitySeen = activity;

        // BOUNDED: preparation takes milliseconds, so only a little generating time may ever go
        // unmeasured on its account. Past the allowance the rate is measured as usual - a
        // generator slower than real time is still caught, whatever the preparation is doing.
        if (preparationTouchedThisInterval && wall > 0.0)
        {
            if (preparationIgnoredSeconds + wall >
                StreamingDefaults.PreparationMeasurementAllowance.TotalSeconds)
            {
                preparationTouchedThisInterval = false;
            }
            else
            {
                preparationIgnoredSeconds += wall;
            }
        }

        if (!wasPulling || wall <= 0.0 || preparationTouchedThisInterval)
        {
            // Time the engine spent paused because it was far enough ahead is not time the
            // generator spent failing to keep up - and time the engine's own crossfade
            // preparation was running is not held against the generator either, so preparing
            // can never be what tips the stream into delivering a segment at a time.
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

        if (!MayCommitWhileTheHeadWaits())
        {
            return;
        }

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

        if (freshIncoming != null)
        {
            // WHILE A FRESH SEGMENT WAITS FOR ITS PLACE, THE COMMIT STOPS SHORT OF THE FADE, so the
            // outgoing music where the fade would go is still in memory and can still be moved
            // beside the incoming piece. A stream loses nothing by it: the deadline in
            // PlaceTheFreshSegmentIfDue places the segment before the head's own safety margin
            // reaches this point, so everything the head is about to play is still written. An
            // OFFLINE RENDER has no deadline at all - it waits for the whole crossfade, and the
            // renderer waits for music, as it always does, rather than writing silence.
            var beforeTheFade = RequestedFreshStartTick() - 2L;

            if (beforeTheFade < limit)
            {
                limit = beforeTheFade;
            }
        }
        else if (!KeepsMusicAheadOfTheHead && CrossfadesFreshSeams && !gaveUp &&
                 (current.Failure == null || IsRetriedAfterFailure(current)) && TheNextSegmentWillBeFresh())
        {
            // AN OFFLINE RENDER KEEPS A WHOLE FADE IN HAND BEFORE THE FRESH SEGMENT EVEN EXISTS. A
            // render writes audio far faster than a model writes music, so it is always right behind
            // the generator: left alone, by the time the outgoing pass ended its commit would be past
            // where the fade has to begin, and the fade would be shortened to a hard join. So while
            // the outgoing pass is still being written, nothing within one fade of its settled end is
            // committed - the renderer simply waits for music a little earlier than it would have.
            var settledEnd = settled.TimeOfTick(current.Reorder.SettledThroughTick) - SeamCrossfade;
            var keepInHand = (settledEnd <= TimeSpan.Zero ? 0L : settled.TickAtTime(settledEnd)) - 2L;

            if (keepInHand < limit)
            {
                limit = keepInHand;
            }
        }

        CommitThrough(limit);
    }

    // NOTHING IS PUT IN FRONT OF A WAITING HEAD UNTIL IT CAN PLAY THROUGH IT. The sequencer delivers
    // every event the head has reached in every state, starved or not - so a note written at the
    // very place a waiting head stands sounds at once, while the head goes on waiting for a whole
    // pre-roll before it moves, and its note-off, further on, is not reached until then. On a
    // generator slower than real time that is a partial bar and then a note ringing for as long
    // as the wait lasts: a drone, or a stuck note, instead of silence. So while the head is
    // waiting, the commit waits too, until a whole pre-roll of settled music lies ahead of the
    // head - the same amount the head waits for - and then the head plays straight through it.
    private bool MayCommitWhileTheHeadWaits()
    {
        if (!KeepsMusicAheadOfTheHead || preroll <= TimeSpan.Zero || !host.IsStarved)
        {
            return true;
        }

        if (current.Finished && current.Reorder.HeldCount == 0 && NothingMoreWillFollow())
        {
            // The whole pass is here AND the music ends with it: nothing more is coming to wait
            // for, and the head will play what there is through to the end. A finished pass that
            // another segment will follow is NOT an exception - under KeepGenerating a short first
            // piece would otherwise sound at the waiting head for as long as the next one took.
            return true;
        }

        var ahead = settled.TimeOfTick(settled.SettledThroughTick) - host.Position;

        // Never more than the generate-ahead window, or a window set smaller than the pre-roll
        // would stop pulling before there was ever enough to commit.
        var needed = preroll < GenerateAhead ? preroll : GenerateAhead;

        return ahead >= needed;
    }

    // Whether the music ends after the current pass: no next segment will be asked for.
    private bool NothingMoreWillFollow() =>
        stopped || gaveUp || EndOfPiece != EndOfPiecePolicy.KeepGenerating ||
        (current.Failure != null && !IsRetriedAfterFailure(current));

    private void CommitThrough(long limit)
    {
        foreach (var midiEvent in settled.TakeThrough(limit))
        {
            if (pendingPlan != null && midiEvent.AbsoluteTime >= pendingPlan.CueTick)
            {
                WriteTheCue();
            }

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

        if (pendingPlan != null && limit >= pendingPlan.CueTick)
        {
            WriteTheCue();
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

    // THE MOMENT THE INCOMING PIECE TAKES OVER THE TIMELINE. The cue goes on the timeline for the
    // mixer to meet - it is not music, so nothing observes it and no instrument is ever sent it -
    // and from here on what is committed is voiced through the incoming piece's own instruments.
    private void WriteTheCue()
    {
        var plan = pendingPlan;

        pendingPlan = null;
        stream.Append(SeamCrossfadePlan.CueEvent(plan.CueTick, plan.Cue));
        voicer = plan.Incoming;

        if (KeepsCrossfadeRecord)
        {
            writtenCues.Add(new KeyValuePair<long, int>(plan.CueTick, plan.Cue));
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
        stream.AdvanceHorizon(throughTick);
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

    private void TakeOverIfFollowUpIsReady(ref SegmentGeneration replaced,
        ref SegmentGeneration abandoned)
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

        // A CROSSFADE THAT HAS NOT REACHED THE TIMELINE BELONGS TO THE LOOKAHEAD, and goes with
        // it: its cue lies beyond the switch, so everything it would have played - the outgoing
        // piece's last moments included - was lookahead the follow-up replaces. A fresh segment
        // still waiting for its place is dropped for the same reason.
        if (pendingPlan != null)
        {
            if (Mixer != null)
            {
                Mixer.Disarm(pendingPlan);
            }

            pendingPlan = null;
        }

        abandoned = freshIncoming;
        freshIncoming = null;
        repromptCount = 0;

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
        if (!current.Finished || current.Reorder.HeldCount > 0 || current.TempoPending.Count > 0)
        {
            // Still something to let out - including music the session-tempo decision is holding
            // back until the next release.
            return;
        }

        if (freshIncoming != null)
        {
            // The next segment is already generating and waits for its place: the music is
            // neither continued a second time nor over.
            return;
        }

        // It is the CURRENT generation's failure that ends the music. A follow-up that failed is
        // reported and forgotten; what is playing is still playing.
        if ((current.Failure == null || IsRetriedAfterFailure(current)) && !stopped &&
            EndOfPiece == EndOfPiecePolicy.KeepGenerating &&
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

    // A PASS THAT FAILS PART-WAY IS ASKED FOR AGAIN, like an empty one, when music is meant to keep
    // going: a model can refuse what it sampled (MuseCoco refuses a piece that needs more than
    // fifteen melodic channels), and one refusal should not end - or, worse, hang - the music. What
    // it wrote before it failed still plays. A follow-up prompt is never retried, and a first
    // piece that failed before writing anything is an ordinary error (a model that will not load).
    // Callers hold the gate.
    private bool IsRetriedAfterFailure(SegmentGeneration generation) =>
        generation.Failure != null && generation.Failure is not OperationCanceledException &&
        EndOfPiece == EndOfPiecePolicy.KeepGenerating && !stopped &&
        generation.Kind != MusicSegmentKind.FollowUp &&
        (generation.Kind != MusicSegmentKind.FirstPiece ||
         (generation.Reorder != null && generation.Reorder.HasSettled));

    private bool StartNextSegment()
    {
        // A SEGMENT STARTS ON A BAR LINE, NEVER MID-BAR.
        var startTick = settledBars.BarLineAtOrAfter(current.Reorder.SettledThroughTick);

        var retrying = false;
        var failed = current.Failure != null;

        if (failed)
        {
            failedPassCount++;

            if (KeepsCrossfadeRecord)
            {
                failures.Add(string.Format(CultureInfo.InvariantCulture,
                    "The pass starting at tick {0} failed and the music went on with a fresh piece: {1}",
                    current.Reorder.SegmentStartTick, current.Failure.Message));
            }
        }

        if (failed && startTick > current.Reorder.SegmentStartTick)
        {
            // It wrote music before it failed: that plays, and the next piece is fresh.
            consecutiveEmptyPasses++;

            if (consecutiveEmptyPasses >= StreamingDefaults.MaximumConsecutiveEmptyPasses)
            {
                return GiveUp();
            }
        }
        else if (startTick <= current.Reorder.SegmentStartTick)
        {
            // AN EMPTY PASS NEVER ENDS MUSIC THAT IS MEANT TO KEEP GOING. A model can answer with
            // nothing at all - MuPT shown the last bars of a tune that ends on a closing repeat
            // often decides the tune is over - and a session that then went quiet for good, with
            // no error, would look exactly like a piece that ended on purpose. So the segment is
            // asked for again at the same bar line, FRESH, on a newly derived seed; only a run of
            // empty passes ends the music, and then with an error that says so.
            if (!failed)
            {
                emptyPassCount++;
            }

            consecutiveEmptyPasses++;

            if (consecutiveEmptyPasses >= StreamingDefaults.MaximumConsecutiveEmptyPasses)
            {
                return GiveUp();
            }

            startTick = current.Reorder.SegmentStartTick;
            retrying = true;
        }
        else
        {
            consecutiveEmptyPasses = 0;
            repromptCount++;
        }

        // A RETRY IS ALWAYS FRESH: there is no new music to prime it with, and the reprompt it
        // stands in for has already been counted, so the alternation carries on where it was.
        var primed = !retrying && !failed && IsPrimed(current.Generator);
        var request = NextSegmentRequest(startTick, primed);
        var kind = primed ? MusicSegmentKind.Primed : MusicSegmentKind.Fresh;

        if (primed)
        {
            primedSegmentCount++;
        }
        else
        {
            freshSegmentCount++;
        }

        if (!primed && CrossfadesFreshSeams && !retrying)
        {
            // A FRESH SEGMENT TO BE CROSSFADED starts generating now but is not placed yet: where
            // it starts depends on how much of it there is by the time the outgoing music has to
            // be committed - see PlaceTheFreshSegmentIfDue.
            var fresh = new SegmentGeneration(current.Generator, current.BaseRequest, request, null,
                true, new CancellationTokenSource(), stream.TicksPerQuarterNote)
            {
                Kind = kind
            };

            freshIncoming = fresh;
            freshSeamEndTick = startTick;
            segmentCount++;
            StartPull(fresh);

            return true;
        }

        var next = new SegmentGeneration(current.Generator, current.BaseRequest,
            request, new ReorderBuffer(startTick), true,
            new CancellationTokenSource(), stream.TicksPerQuarterNote)
        {
            Kind = kind
        };

        current = next;
        segmentCount++;
        segmentStartTicks.Add(startTick);
        seamTicks.Enqueue(startTick);
        StartPull(next);

        return true;
    }

    // PRIMED OR FRESH. A generator that cannot be shown the music so far is fresh whatever the
    // option says; otherwise the option decides, and ALTERNATE primes the first segment asked for
    // since the music (or the last follow-up prompt) started, then every other one.
    private bool GiveUp()
    {
        gaveUp = true;

        var failure = current.Failure;

        GenerationError = failure == null
            ? new MusicGenerationException(string.Format(CultureInfo.InvariantCulture,
                "The music generator '{0}' produced no music {1} times in a row, so the music has " +
                "stopped. Each empty pass was asked for again as a fresh piece on a new seed before " +
                "giving up.", current.Generator.Name, consecutiveEmptyPasses))
            : new MusicGenerationException(string.Format(CultureInfo.InvariantCulture,
                "The music generator '{0}' failed or produced no music {1} times in a row, so the " +
                "music has stopped. The last failure: {2}", current.Generator.Name,
                consecutiveEmptyPasses, failure.Message), failure);

        return false;
    }

    // Whether the segment asked for at the end of the pass now being written would be fresh.
    private bool TheNextSegmentWillBeFresh() =>
        EndOfPiece == EndOfPiecePolicy.KeepGenerating &&
        (current.Failure != null || !IsPrimed(current.Generator, repromptCount + 1));

    private bool IsPrimed(IMusicGenerator generator) => IsPrimed(generator, repromptCount);

    private bool IsPrimed(IMusicGenerator generator, int reprompt)
    {
        if ((generator.Honours & MusicRequestFeatures.Continuation) != MusicRequestFeatures.Continuation)
        {
            return false;
        }

        switch (SegmentPriming)
        {
            case SegmentPriming.Fresh:
                return false;

            case SegmentPriming.Alternate:
                return reprompt % 2 == 1;

            default:
                return true;
        }
    }

    private bool CrossfadesFreshSeams =>
        SeamCrossfade > TimeSpan.Zero && Mixer != null && CreateVoicer != null;

    private MusicRequest NextSegmentRequest(long startTick, bool primed)
    {
        var request = current.BaseRequest.Clone();
        var honours = current.Generator.Honours;

        if (primed)
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

        // A FRESH SEGMENT is the application's own request again, exactly as a generator that
        // cannot be shown the music so far has always been asked: nothing carried is put in the
        // request, and the seam itself carries the tempo and the instruments across.

        if (request.Seed.HasValue &&
            (honours & MusicRequestFeatures.Seed) == MusicRequestFeatures.Seed)
        {
            // KEEP THE CHARACTER, CHANGE THE SEED. The sampling controls are left exactly as they
            // are, so the music does not change its manner half-way through a piece.
            request.Seed = SegmentSeed.Derive(current.BaseRequest.Seed.Value, segmentCount + 1);
        }

        return request;
    }

    // WHERE A FRESH SEGMENT GOES, AND WHETHER IT IS CROSSFADED. It is generating alongside the end
    // of the outgoing piece, and it is placed as soon as it has the whole crossfade plus its own
    // pre-roll. Meanwhile the commit is held short of the fade. THE DEADLINE is the pump at which
    // the head, plus the hold margin every switch keeps and a little slack, would reach the start
    // of the fade: then it is placed with whatever it has, the fade shortened to leave it its
    // pre-roll, and a fade shortened to nothing is an ordinary hard join at the bar line - with
    // every note of the outgoing piece still on the timeline, because nothing is taken off it
    // until a fade longer than nothing has been decided. So a crossfade never makes the music
    // wait, and never uses music that has not settled.
    private void PlaceTheFreshSegmentIfDue()
    {
        var fresh = freshIncoming;

        if (fresh == null)
        {
            return;
        }

        if (fresh.Failure != null || (fresh.Finished && fresh.WaitingCount == 0))
        {
            // NOTHING TO FADE IN. It is placed where a hard join would have put it, so what
            // happens next - a failure reported, or the music carrying on - is what always did.
            PlaceHard(fresh, false);

            return;
        }

        if (fallbackActive)
        {
            // Segment at a time is phrases separated by rests; there is nothing to overlap.
            PlaceHard(fresh, true);

            return;
        }

        if (pendingPlan != null)
        {
            // THE CROSSFADE BEFORE THIS ONE HAS NOT REACHED THE TIMELINE YET - the piece now
            // ending was itself crossfaded in, and its pass was over before its own cue was
            // committed. Its cue comes first on the timeline, so it is always committed before
            // this fade could begin; until then the mixer has one plan at a time to wait for.
            return;
        }

        // THE INCOMING PIECE'S SILENT OPENING BARS DO NOT COUNT, because a crossfade skips them:
        // a fade into bars of silence is the outgoing piece fading into nothing, which undoes the
        // point of the crossfade. What the segment brings to the fade is its music after them.
        var skipTicks = fresh.LeadingEmptyTicks(FreshTicksPerBar(fresh));
        var wanted = SeamCrossfade;
        var available = fresh.WaitingSettledTimeFrom(skipTicks);

        if (TempoPolicy != SessionTempoPolicy.Adopt && sessionTempo > 0)
        {
            // A PIECE THAT WILL BE CARRIED IS HEARD AT THE SESSION TEMPO, so what it brings to the
            // seam is measured at that tempo, not at the one it was written at.
            if (!fresh.TempoDecided && fresh.HasANoteWaiting)
            {
                // Taken only once the piece has a note: before that, the tempo it will sound at
                // is not known - see AtTheSessionTempo.
                DecideTheTempo(fresh, fresh.TempoAtTheFirstNote);
            }

            if (fresh.TempoDecided && fresh.CarriesSessionTempo)
            {
                available = TimeSpan.FromSeconds(fresh.WaitingSettledTicksFrom(skipTicks) *
                    (sessionTempo / 1000000.0 / stream.TicksPerQuarterNote));
            }
        }
        var ownPreroll = KeepsMusicAheadOfTheHead ? preroll : TimeSpan.Zero;

        // NEVER AN UNBOUNDED WAIT: a fresh segment that has reached its pull limit gets no more music
        // until it is placed, so it is placed with what it has - a fade shortened to that, or a hard
        // join - rather than waited for for ever. An offline render, which has no deadline, relies
        // on this; it happens when a piece's music settles far ahead of its notes.
        var atItsPullLimit = fresh.WaitingSettledTime >= FreshSegmentPullLimit();
        var ready = fresh.Finished || atItsPullLimit || available >= wanted + ownPreroll;

        if (!ready)
        {
            if (!KeepsMusicAheadOfTheHead)
            {
                // An offline render waits for the whole fade - see Commit.
                return;
            }

            // THE DEADLINE IS THE HEAD'S, NOT THE COMMIT WINDOW'S: the commit is being held short of
            // the fade (see Commit), so the segment can wait until the head's own safety margin -
            // the one every switch keeps - is about to reach the start of the fade.
            var headWouldReachTheFade = settled.TickAtTime(host.Position + HoldMargin() +
                StreamingDefaults.FreshSeamDeadlineSlack) >= RequestedFreshStartTick() - 1L;

            if (!headWouldReachTheFade)
            {
                return;
            }
        }

        var fade = wanted;

        if (fresh.Finished || atItsPullLimit)
        {
            // The whole of the incoming piece is here - or all it will be given before it is
            // placed - and it may be shorter than the fade.
            if (available < fade)
            {
                fade = available;
            }
        }
        else if (!ready)
        {
            // THE DEADLINE: as much of a fade as leaves the incoming piece its own pre-roll.
            fade = available - ownPreroll;

            if (fade < TimeSpan.Zero)
            {
                fade = TimeSpan.Zero;
            }
        }

        PlaceCrossfaded(fresh, fade, fade < wanted, skipTicks);
    }

    // A fresh piece's bars are its own: the metre it states at its first tick if it states one,
    // and otherwise the metre the timeline carries on in - which is what the seam restates for it.
    private long FreshTicksPerBar(SegmentGeneration fresh)
    {
        var meter = fresh.OpeningMeter ?? settledBars.CurrentMeter;

        return meter.TicksPerBar(stream.TicksPerQuarterNote);
    }

    private void PlaceHard(SegmentGeneration fresh, bool countAsShortened)
    {
        // EXACTLY WHERE AND HOW A FRESH SEGMENT HAS ALWAYS GONE: at the bar line the outgoing piece
        // ends at, through the same instruments, with the seam saying nothing twice.
        fresh.Promote(freshSeamEndTick);
        current = fresh;
        freshIncoming = null;
        segmentStartTicks.Add(freshSeamEndTick);
        seamTicks.Enqueue(freshSeamEndTick);

        // Its music arrived before it had a place, so it is not measured as if it had just been
        // produced in one pump.
        hasSample = false;

        if (countAsShortened)
        {
            shortenedCrossfadeCount++;
        }
    }

    private void PlaceCrossfaded(SegmentGeneration fresh, TimeSpan fade, bool shortened,
        long skipTicks)
    {
        var endTick = freshSeamEndTick;

        if (fade <= TimeSpan.Zero)
        {
            PlaceHard(fresh, true);

            return;
        }

        var endTime = settled.TimeOfTick(endTick);
        var fadeFrom = endTime - fade;
        var startTick = OnABeat(fadeFrom <= TimeSpan.Zero ? 0L : settled.TickAtTime(fadeFrom), endTick);

        // NEVER ON MUSIC ALREADY COMMITTED, NEVER AT THE HEAD, and never back into the segment
        // before the outgoing one: the fade is shortened instead.
        var earliest = Math.Max(EarliestSwitchTick() + 1L, current.Reorder.SegmentStartTick + 1L);

        if (startTick < earliest)
        {
            startTick = earliest;
            shortened = true;
        }

        if (startTick >= endTick)
        {
            PlaceHard(fresh, true);

            return;
        }

        var cueTick = startTick - 1L;
        var cueTime = settled.TimeOfTick(cueTick);

        // THE OUTGOING PIECE'S LAST MOMENTS LEAVE THE TIMELINE. They are timed now, by the tempo
        // they were written at, and played beside the incoming piece by the mixer - through the
        // outgoing instruments, which are told about them here, on the pump thread, so that
        // anything they need is built before the audio thread ever asks for it.
        var tail = settled.TakeFrom(cueTick);
        var timed = new List<TimedTailEvent>(tail.Count);

        for (var i = 0; i < tail.Count; i++)
        {
            var midiEvent = tail[i];
            var at = settled.TimeOfTick(midiEvent.AbsoluteTime) - cueTime;
            var offAt = midiEvent is NoteOnEvent note && note.OffEvent != null
                ? settled.TimeOfTick(note.OffEvent.AbsoluteTime) - cueTime
                : at;

            voicer.Observe(midiEvent);
            timed.Add(new TimedTailEvent(midiEvent, at, offAt));

            if (KeepsCrossfadeRecord)
            {
                crossfadedTails.Add(midiEvent);
            }
        }

        // Everything that only knew about the outgoing piece's last moments forgets them: the
        // timeline from the cue on is the incoming piece's, at its own tempo and in its own bars.
        settled.DiscardTempoFrom(cueTick);
        settled.RewindSettledTo(cueTick);
        settledBars.DiscardFrom(cueTick);
        settledMusic.RemoveAll(midiEvent => midiEvent.AbsoluteTime >= cueTick);
        settledState.CopyFrom(committedState);

        foreach (var midiEvent in settled.Snapshot())
        {
            settledState.Observe(midiEvent);
        }

        // WHAT A HARD JOIN WOULD HAVE CARRIED ACROSS, said out loud, because the incoming piece's
        // instruments are brand new and remember nothing: the program on every channel, so a part
        // the fresh piece never re-states keeps its instrument - and the metre in force, stated AT
        // THE START OF THE INCOMING PIECE, so its bar lines are counted from where it really
        // starts. Whatever the fresh piece says at its first tick replaces them.
        var meter = settledBars.CurrentMeter;
        var carried = new List<MidiEvent>
        {
            new TimeSignatureEvent(startTick, meter.BeatsPerBar,
                BitOperations.Log2((uint)meter.BeatNoteValue), 24, 8)
        };

        carried.AddRange(settledState.ProgramChangesAt(startTick));

        for (var i = 0; i < carried.Count; i++)
        {
            settledState.Observe(carried[i]);
            settledBars.Observe(carried[i]);
            Remember(carried[i]);
            settled.Add(carried[i]);
        }

        var plan = new SeamCrossfadePlan(NextCue(), cueTick, startTick, TakeIncomingVoicer(),
            (long)Math.Round((endTime - cueTime).TotalSeconds * Mixer.SampleRate,
                MidpointRounding.AwayFromZero),
            SeamCrossfadeCurve, timed);

        Mixer.Arm(plan);
        pendingPlan = plan;

        if (skipTicks > 0L)
        {
            // ONLY HERE - a crossfade really being placed - are the incoming piece's silent
            // opening bars skipped, so its first bar with notes starts at the start of the fade.
            // Every hard join above has already returned, and sounds exactly as it always did.
            var ticksPerBar = FreshTicksPerBar(fresh);

            fresh.SkipLeadingTicks(skipTicks);
            skippedLeadingBarCount += (int)(skipTicks / ticksPerBar);
        }

        fresh.Promote(startTick);
        current = fresh;
        freshIncoming = null;
        segmentStartTicks.Add(startTick);
        hasSample = false;
        crossfadeCount++;

        if (shortened)
        {
            shortenedCrossfadeCount++;
        }
    }

    // THE INCOMING PIECE'S INSTRUMENTS ARE READY BEFORE THEY ARE NEEDED. A voicer is built one fade
    // ahead - by the background preparation once the music is playing, and again each time one is
    // taken - and its routing synthesizer renders once, silently, so that nothing about it is done
    // for the first time on the audio thread. Its instruments are built, as every part's are, on
    // the pump thread when its first notes are committed - before the cue reaches the audio.
    private RenditionVoicer TakeIncomingVoicer()
    {
        // A spare that is not ready yet is built here: it is a routing table and nothing else, so
        // it costs microseconds.
        var voicer = spareVoicer ?? ReadyVoicer();

        spareVoicer = null;

        if (PreparesInBackground)
        {
            RunInBackground(() =>
            {
                var next = ReadyVoicer();

                lock (gate)
                {
                    if (spareVoicer == null)
                    {
                        spareVoicer = next;
                    }
                }
            });
        }
        else
        {
            spareVoicer = ReadyVoicer();
        }

        return voicer;
    }

    private RenditionVoicer ReadyVoicer()
    {
        var ready = CreateVoicer();
        var size = Math.Max(ready.Router.BlockSize, 512);

        ready.Router.Render(new float[size], new float[size]);

        return ready;
    }

    /// <summary>
    /// Whether crossfade preparation runs in the background once the music has started - what a
    /// stream wants - rather than at once on the calling thread, which is what an offline render
    /// wants: it has no play head, and a render that is deterministic is worth more than one that
    /// starts a millisecond sooner.
    /// </summary>
    public bool PreparesInBackground { get; set; } = true;

    /// <summary>
    /// Asks for the crossfade path to be made ready, so that a crossfade costs nothing new on the
    /// audio thread: the mixer's whole crossfade path is run once off it, and the first incoming
    /// voicer is built and its router rendered once. Does nothing unless fresh seams are
    /// crossfaded.
    /// </summary>
    /// <remarks>
    /// NOTHING OF IT IS ON THE CRITICAL PATH OF A STREAM. With <see cref="PreparesInBackground"/>
    /// (the default) this only asks: the work starts on the first pump after the music has
    /// started - the first pre-roll written and the head playing - on a background thread of its
    /// own below normal priority, never on the thread pulling from the generator, and the
    /// generation rate is not measured over any interval it touched. Otherwise it runs at once.
    /// </remarks>
    public void PrepareCrossfades()
    {
        lock (gate)
        {
            if (!CrossfadesFreshSeams)
            {
                preparation.TrySetResult();

                return;
            }

            if (PreparesInBackground)
            {
                preparationWanted = true;

                return;
            }
        }

        PrepareNow();
    }

    /// <summary>Completes when crossfade preparation has run, or when there was none to run.</summary>
    public Task CrossfadePreparation => preparation.Task;

    /// <summary>Whether crossfade preparation has been started.</summary>
    public bool CrossfadePreparationStarted
    {
        get { lock (gate) { return preparationStarted; } }
    }

    private void StartPreparingCrossfadesOnceTheMusicHasStarted()
    {
        // Callers hold the gate.
        if (!preparationWanted || preparationStarted || !musicHasStarted || stopped)
        {
            return;
        }

        preparationStarted = true;
        RunInBackground(PrepareNow);
    }

    private void PrepareNow()
    {
        try
        {
            Mixer.WarmUp(CreateVoicer, SeamCrossfadeCurve);

            var ready = ReadyVoicer();

            lock (gate)
            {
                if (spareVoicer == null)
                {
                    spareVoicer = ready;
                }
            }

            preparation.TrySetResult();
        }
        catch (Exception exception)
        {
            // Preparing is an optimisation: a crossfade works without it, so a failure here is
            // reported with the task and nothing else is touched.
            preparation.TrySetException(exception);
        }
    }

    // A THREAD OF ITS OWN, BELOW NORMAL PRIORITY, marked busy for as long as it runs so that the
    // generation rate is not measured across it. Never the thread pool, which is where the
    // generator is pulled from and where the pump runs.
    private void RunInBackground(Action work)
    {
        var thread = new Thread(() =>
        {
            Interlocked.Increment(ref preparationInFlight);
            Interlocked.Increment(ref preparationActivity);

            try
            {
                work();
            }
            finally
            {
                Interlocked.Decrement(ref preparationInFlight);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "MusicGeneration crossfade preparation"
        };

        thread.Start();
    }

    /// <summary>Whether an incoming voicer is built and waiting for the next crossfade.</summary>
    public bool HasSpareVoicer
    {
        get { lock (gate) { return spareVoicer != null; } }
    }

    // THE JOIN GOES ON A BEAT OF THE OUTGOING PIECE when at least a beat fits, counted back from the
    // bar line it ends at - so the fade is rounded down to whole beats, and the incoming piece's
    // first downbeat lands where the outgoing piece had a beat.
    private long OnABeat(long tick, long endTick)
    {
        var meter = settledBars.CurrentMeter;
        var beat = meter.TicksPerBar(stream.TicksPerQuarterNote) / meter.BeatsPerBar;

        if (beat < 1L || endTick - tick < beat)
        {
            return tick;
        }

        return endTick - (((endTick - tick) / beat) * beat);
    }

    // Where the fade would begin if the whole of it were used - but never back into the segment
    // before the outgoing one, which is where PlaceCrossfaded stops it too.
    private long RequestedFreshStartTick()
    {
        var fadeFrom = settled.TimeOfTick(freshSeamEndTick) - SeamCrossfade;
        var tick = fadeFrom <= TimeSpan.Zero ? 0L : settled.TickAtTime(fadeFrom);
        var outgoingStart = current.Reorder.SegmentStartTick + 1L;

        return tick < outgoingStart ? outgoingStart : tick;
    }

    private TimeSpan FreshSegmentPullLimit()
    {
        var enough = SeamCrossfade + preroll + TimeSpan.FromSeconds(1.0);

        return enough > GenerateAhead ? enough : GenerateAhead;
    }

    private int NextCue()
    {
        // 1 to 127 and round again: a cue only has to differ from the one before it.
        lastCue = (lastCue % 127) + 1;

        return lastCue;
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
