using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// ONE CALL TO ONE GENERATOR: the generator, the request it was asked with, the buffer its events
/// are reordered in, and the task pulling it.
/// </summary>
/// <remarks>
/// <para>
/// A generation is normally placed on the timeline the moment it starts. A FOLLOW-UP PROMPT is the
/// exception: it starts at once, alongside the music still playing, and nobody knows where it will
/// land until it has enough music to take over - the bar line it takes over at depends on how much
/// has been committed by then. So a generation may be PENDING, holding what it has produced in a
/// list, and is given its place when it is promoted.
/// </para>
/// <para>
/// NOTHING HERE LOCKS. The engine owns one of these at a time and touches it under its own lock;
/// the only thing that reaches it from another thread is the pull task, which does so under that
/// same lock.
/// </para>
/// </remarks>
internal sealed class SegmentGeneration
{
    private readonly List<GeneratedMusicEvent> waiting = new List<GeneratedMusicEvent>();
    private readonly int ticksPerQuarterNote;

    private SettledTempoMap waitingTempo;

    private long waitingSettledTick = -1L;

    /// <summary>Creates a generation.</summary>
    /// <param name="generator">The generator to pull from.</param>
    /// <param name="baseRequest">
    /// The request as the application asked for it, WITHOUT anything the engine adds for a
    /// continuation. Later segments of this music are built from it.
    /// </param>
    /// <param name="request">The request this particular pass is pulled with.</param>
    /// <param name="reorder">Where the events go, or null for a generation not yet placed.</param>
    /// <param name="isContinuation">Whether it follows music that was already playing.</param>
    /// <param name="cancellation">What stops it.</param>
    /// <param name="ticksPerQuarterNote">The resolution its ticks are counted at.</param>
    public SegmentGeneration(IMusicGenerator generator, MusicRequest baseRequest, MusicRequest request,
        ReorderBuffer reorder, bool isContinuation, CancellationTokenSource cancellation,
        int ticksPerQuarterNote)
    {
        Generator = generator;
        BaseRequest = baseRequest;
        Request = request;
        Reorder = reorder;
        IsContinuation = isContinuation;
        Cancellation = cancellation;
        this.ticksPerQuarterNote = ticksPerQuarterNote;
        waitingTempo = new SettledTempoMap(ticksPerQuarterNote);
    }

    /// <summary>The generator this pass came from.</summary>
    public IMusicGenerator Generator { get; }

    /// <summary>The request as the application asked for it, which later segments are built from.</summary>
    public MusicRequest BaseRequest { get; }

    /// <summary>The request this pass was pulled with.</summary>
    public MusicRequest Request { get; }

    /// <summary>Where the events go, or null while the generation has not been placed yet.</summary>
    public ReorderBuffer Reorder { get; private set; }

    /// <summary>Whether this generation follows music that was already playing.</summary>
    public bool IsContinuation { get; private set; }

    /// <summary>
    /// What kind of segment this is: the first piece, a primed or a fresh segment the engine asked
    /// for at the end of a pass, or a follow-up prompt.
    /// </summary>
    public MusicSegmentKind Kind { get; set; } = MusicSegmentKind.FirstPiece;

    /// <summary>What stops the pull.</summary>
    public CancellationTokenSource Cancellation { get; }

    /// <summary>The task pulling from the generator.</summary>
    public Task Task { get; set; }

    /// <summary>Whether the generator's pass is over, for whatever reason.</summary>
    public bool Finished { get; set; }

    /// <summary>What the generator threw, or null.</summary>
    public Exception Failure { get; set; }

    /// <summary>Whether the generation has been given its place on the timeline.</summary>
    public bool IsPlaced => Reorder != null;

    /// <summary>
    /// How much settled music a generation that has not been placed yet is holding. This is what
    /// decides when a follow-up prompt has its OWN pre-roll and can take over.
    /// </summary>
    public TimeSpan WaitingSettledTime =>
        waitingSettledTick < 0L ? TimeSpan.Zero : waitingTempo.TimeOfTick(waitingSettledTick);

    /// <summary>How many items a generation that has not been placed yet is holding.</summary>
    public int WaitingCount => waiting.Count;

    /// <summary>Takes in one item from the generator.</summary>
    /// <param name="item">The item, at a tick measured from the start of the segment.</param>
    public void Accept(GeneratedMusicEvent item)
    {
        if (Reorder != null)
        {
            Reorder.Add(item);

            return;
        }

        if (item.HasEvent && item.Event is TempoEvent tempo)
        {
            waitingTempo.Add(tempo.AbsoluteTime, tempo.MicrosecondsPerQuarterNote);
        }

        waiting.Add(item);

        if (item.HasSettled && item.SettledThroughTick > waitingSettledTick)
        {
            waitingSettledTick = item.SettledThroughTick;
        }
    }

    /// <summary>
    /// The metre a generation that has not been placed yet states at its very first tick, or null
    /// when it states none there.
    /// </summary>
    public MusicMeter? OpeningMeter
    {
        get
        {
            for (var i = 0; i < waiting.Count; i++)
            {
                if (waiting[i].HasEvent && waiting[i].Event is TimeSignatureEvent signature &&
                    signature.AbsoluteTime == 0L && signature.Numerator >= 1 &&
                    signature.Denominator >= 0 && signature.Denominator <= 7)
                {
                    return new MusicMeter(signature.Numerator, 1 << signature.Denominator);
                }
            }

            return null;
        }
    }

    /// <summary>Whether the session tempo has been stated at this generation's start.</summary>
    public bool SessionTempoStated { get; set; }

    /// <summary>
    /// Whether the session-tempo decision has been taken for this generation.
    /// </summary>
    public bool TempoDecided { get; set; }

    /// <summary>
    /// Whether this generation plays at the session tempo: its own tempo events are dropped and the
    /// session tempo is stated where it starts.
    /// </summary>
    public bool CarriesSessionTempo { get; set; }

    /// <summary>
    /// What a generation has released and the session-tempo decision is holding back, until its
    /// first note shows what tempo it will sound at.
    /// </summary>
    public List<MidiEvent> TempoPending { get; } = new List<MidiEvent>();

    /// <summary>The tempo the piece was written at where it first sounds, once decided.</summary>
    public int IncomingTempo { get; set; }

    /// <summary>Whether a generation that has not been placed yet holds a note yet.</summary>
    public bool HasANoteWaiting
    {
        get
        {
            for (var i = 0; i < waiting.Count; i++)
            {
                if (waiting[i].HasEvent && MidiEvent.IsNoteOn(waiting[i].Event))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The tempo in force where a generation that has not been placed yet first sounds: the last
    /// tempo event at or before its first note, in microseconds per quarter note - or null when it
    /// states none there.
    /// </summary>
    public int? TempoAtTheFirstNote
    {
        get
        {
            var events = new List<MidiEvent>(waiting.Count);

            for (var i = 0; i < waiting.Count; i++)
            {
                if (waiting[i].HasEvent)
                {
                    events.Add(waiting[i].Event);
                }
            }

            return MusicEngine.TempoAtTheFirstNote(events);
        }
    }

    /// <summary>
    /// How many ticks of settled music a generation that has not been placed yet holds from a tick
    /// on - which, at a tempo other than its own, is what it brings to a seam.
    /// </summary>
    /// <param name="fromTick">The tick counted from.</param>
    /// <returns>The ticks, never negative.</returns>
    public long WaitingSettledTicksFrom(long fromTick) =>
        waitingSettledTick < fromTick ? 0L : (waitingSettledTick - fromTick) + 1L;

    /// <summary>
    /// How many ticks of WHOLE, SETTLED, SILENT BARS a generation that has not been placed yet
    /// opens with: the bars before its first note, counted in whole bars, and only as far as it
    /// has settled - a bar the generator could still put a note in is never counted.
    /// </summary>
    /// <param name="ticksPerBar">How long one of its bars is.</param>
    /// <returns>A whole number of bars, in ticks; zero when it starts with music.</returns>
    public long LeadingEmptyTicks(long ticksPerBar)
    {
        if (ticksPerBar < 1L || waitingSettledTick < 0L)
        {
            return 0L;
        }

        var firstNote = long.MaxValue;

        for (var i = 0; i < waiting.Count; i++)
        {
            var item = waiting[i];

            if (item.HasEvent && MidiEvent.IsNoteOn(item.Event) && item.Event.AbsoluteTime < firstNote)
            {
                firstNote = item.Event.AbsoluteTime;
            }
        }

        var silentThrough = firstNote < waitingSettledTick + 1L ? firstNote : waitingSettledTick + 1L;

        return (silentThrough / ticksPerBar) * ticksPerBar;
    }

    /// <summary>
    /// How much settled music a generation that has not been placed yet holds FROM a tick on -
    /// which is what it would bring to a seam if everything before that tick were skipped.
    /// </summary>
    /// <param name="fromTick">The tick counted from.</param>
    /// <returns>The time, never negative.</returns>
    public TimeSpan WaitingSettledTimeFrom(long fromTick)
    {
        if (waitingSettledTick < 0L)
        {
            return TimeSpan.Zero;
        }

        var time = waitingTempo.TimeOfTick(waitingSettledTick) - waitingTempo.TimeOfTick(fromTick);

        return time < TimeSpan.Zero ? TimeSpan.Zero : time;
    }

    /// <summary>
    /// Skips the first ticks of a generation that has not been placed yet - which the engine only
    /// ever does for whole, settled, silent bars. Every note after them moves earlier by exactly
    /// that much; everything IN them that is not a note (the tempo, the metre, the key, programs
    /// and controllers) is kept and moved to the generation's first tick, so the state the music
    /// starts in is exactly the state it would have had.
    /// </summary>
    /// <param name="ticks">How many ticks to skip.</param>
    public void SkipLeadingTicks(long ticks)
    {
        if (ticks <= 0L)
        {
            return;
        }

        var shifted = new List<GeneratedMusicEvent>(waiting.Count);
        var tempo = new SettledTempoMap(ticksPerQuarterNote);

        for (var i = 0; i < waiting.Count; i++)
        {
            var item = waiting[i];
            var settledThrough = item.HasSettled ? item.SettledThroughTick - ticks : GeneratedMusicEvent.NothingSettled;

            if (!item.HasEvent)
            {
                if (settledThrough >= 0L)
                {
                    shifted.Add(GeneratedMusicEvent.SettledThrough(settledThrough));
                }

                continue;
            }

            var tick = item.Event.AbsoluteTime;
            var moved = MusicEventPlacement.At(item.Event, tick < ticks ? 0L : tick - ticks);

            if (moved is TempoEvent tempoEvent)
            {
                tempo.Add(tempoEvent.AbsoluteTime, tempoEvent.MicrosecondsPerQuarterNote);
            }

            shifted.Add(GeneratedMusicEvent.FromMidiEvent(moved,
                settledThrough < 0L ? GeneratedMusicEvent.NothingSettled : settledThrough));
        }

        waiting.Clear();
        waiting.AddRange(shifted);
        waitingTempo = tempo;
        waitingSettledTick = waitingSettledTick - ticks < 0L ? -1L : waitingSettledTick - ticks;
    }

    /// <summary>
    /// Gives the generation its place on the timeline and hands everything it was holding to the
    /// reorder buffer, in the order it arrived.
    /// </summary>
    /// <param name="segmentStartTick">Where the segment starts, in ticks from the start of the timeline.</param>
    public void Promote(long segmentStartTick)
    {
        Reorder = new ReorderBuffer(segmentStartTick);
        IsContinuation = true;

        for (var i = 0; i < waiting.Count; i++)
        {
            Reorder.Add(waiting[i]);
        }

        waiting.Clear();
    }
}
