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
    private readonly SettledTempoMap waitingTempo;

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
