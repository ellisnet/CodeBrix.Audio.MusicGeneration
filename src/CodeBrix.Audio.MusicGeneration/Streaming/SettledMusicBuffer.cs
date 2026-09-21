using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// The second stage of the engine: settled music, in tick order, held IN MEMORY rather than on the
/// timeline, with the tempo map needed to measure it in seconds.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT IS NOT THE TIMELINE. A <c>MidiStream</c> is append-only: nothing removes anything from
/// it, and nothing truncates it. A long lookahead written straight onto the timeline could
/// therefore never be taken back - and taking it back is exactly what a follow-up prompt has to
/// do, dropping the old lookahead and taking over at the next bar line. So the long lookahead
/// lives here, where it can be dropped, and only a short window of it is ever committed.
/// </para>
/// <para>
/// THE TEMPO MAP IS KEPT AS THE MUSIC PASSES THROUGH, because everything the streaming rules
/// measure is measured in seconds of music and the tempo is part of the music.
/// </para>
/// </remarks>
internal sealed class SettledMusicBuffer
{
    private readonly Queue<MidiEvent> waiting = new Queue<MidiEvent>();
    private readonly SettledTempoMap tempoMap;

    private long settledThroughTick = -1L;

    /// <summary>Creates an empty buffer at a tick resolution.</summary>
    /// <param name="ticksPerQuarterNote">The resolution the timeline counts ticks at.</param>
    /// <exception cref="ArgumentOutOfRangeException">The resolution is not positive.</exception>
    public SettledMusicBuffer(int ticksPerQuarterNote)
    {
        TicksPerQuarterNote = ticksPerQuarterNote;
        tempoMap = new SettledTempoMap(ticksPerQuarterNote);
    }

    /// <summary>The resolution the timeline counts ticks at.</summary>
    public int TicksPerQuarterNote { get; }

    /// <summary>How many settled events are waiting to be committed.</summary>
    public int Count => waiting.Count;

    /// <summary>Whether everything settled has been committed.</summary>
    public bool IsEmpty => waiting.Count == 0;

    /// <summary>
    /// The tick through which the music is settled, which may be well beyond the last event when a
    /// settled stretch of silence has gone by. -1 until anything settles.
    /// </summary>
    public long SettledThroughTick => settledThroughTick;

    /// <summary>The tick of the next event waiting, or -1 when nothing is waiting.</summary>
    public long NextTick => waiting.Count == 0 ? -1L : waiting.Peek().AbsoluteTime;

    /// <summary>Takes in one settled event. Events must arrive in tick order.</summary>
    /// <param name="midiEvent">The event, at its tick on the timeline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="midiEvent"/> is null.</exception>
    public void Add(MidiEvent midiEvent)
    {
        if (midiEvent == null)
        {
            throw new ArgumentNullException(nameof(midiEvent));
        }

        if (midiEvent is TempoEvent tempo)
        {
            tempoMap.Add(tempo.AbsoluteTime, tempo.MicrosecondsPerQuarterNote);
        }

        waiting.Enqueue(midiEvent);
    }

    /// <summary>Records how far the music is settled.</summary>
    /// <param name="tick">The tick through which nothing will change or be added.</param>
    public void SettleThrough(long tick)
    {
        if (tick > settledThroughTick)
        {
            settledThroughTick = tick;
        }
    }

    /// <summary>Takes everything waiting up to and including a tick, in tick order.</summary>
    /// <param name="throughTick">The last tick to take.</param>
    /// <returns>The events, in the order they are to be played.</returns>
    public IReadOnlyList<MidiEvent> TakeThrough(long throughTick)
    {
        if (waiting.Count == 0 || waiting.Peek().AbsoluteTime > throughTick)
        {
            return Array.Empty<MidiEvent>();
        }

        var taken = new List<MidiEvent>();

        while (waiting.Count > 0 && waiting.Peek().AbsoluteTime <= throughTick)
        {
            taken.Add(waiting.Dequeue());
        }

        return taken;
    }

    /// <summary>
    /// Throws away everything not yet committed. The tempo map is kept: the tempo of the music
    /// already played is a fact, whatever happens to the music that was going to follow it.
    /// </summary>
    public void DiscardUncommitted() => waiting.Clear();

    /// <summary>
    /// Puts the settled point back to a tick. It is how a FOLLOW-UP PROMPT takes over: the music
    /// that was going to follow has been thrown away, so the buffer must stop claiming it settled.
    /// </summary>
    /// <param name="tick">
    /// The tick the music is settled through from now on, which is the tick before the new
    /// segment's first.
    /// </param>
    /// <remarks>
    /// It is the one thing that moves the settled point BACKWARDS, and nothing but a discard may
    /// call it: a settled tick is a promise, and it may only be taken back over music nobody has
    /// heard. The tempo map is untouched - the tempo of the music already played is a fact.
    /// </remarks>
    public void RewindSettledTo(long tick) => settledThroughTick = tick;

    /// <summary>
    /// Moves everything waiting from a tick onwards later by a number of ticks, and with it the
    /// settled point and the tempo map. It is how the music WAITS: the stretch that opens up in
    /// front of it is a rest, which is settled music too.
    /// </summary>
    /// <param name="fromTick">The first tick that moves. Nothing before it is touched.</param>
    /// <param name="delta">How far to move it. Zero or less does nothing.</param>
    /// <remarks>
    /// Only music that has NOT been committed is ever in here, so this can never disturb what has
    /// already been heard. Each event is rebuilt at its new tick rather than re-timed in place,
    /// because the event object may be remembered elsewhere at the tick it was written at.
    /// </remarks>
    public void HoldFrom(long fromTick, long delta)
    {
        if (delta <= 0L)
        {
            return;
        }

        var count = waiting.Count;

        for (var i = 0; i < count; i++)
        {
            var midiEvent = waiting.Dequeue();

            waiting.Enqueue(midiEvent.AbsoluteTime < fromTick
                ? midiEvent
                : MusicEventPlacement.At(midiEvent, midiEvent.AbsoluteTime + delta));
        }

        tempoMap.HoldFrom(fromTick, delta);

        if (settledThroughTick >= 0L && settledThroughTick >= fromTick - 1L)
        {
            settledThroughTick += delta;
        }
    }

    /// <summary>When a tick happens, measured from the start of the timeline.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The moment the music reaches that tick.</returns>
    public TimeSpan TimeOfTick(long tick) => tempoMap.TimeOfTick(tick);

    /// <summary>Which tick the music has reached at a moment.</summary>
    /// <param name="time">The moment, measured from the start of the timeline.</param>
    /// <returns>The tick, rounded down.</returns>
    public long TickAtTime(TimeSpan time) => tempoMap.TickAtTime(time);
}
