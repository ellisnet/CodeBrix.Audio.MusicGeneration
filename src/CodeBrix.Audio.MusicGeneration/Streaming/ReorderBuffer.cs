using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// The first stage of the engine: it takes what a generator yields, holds each event until the
/// generator has said that its tick has settled, and lets it out IN TICK ORDER, placed where the
/// segment sits on the timeline.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS. A generator may write a bar and then go back and fill in a part, so its events do
/// not arrive in tick order - and a timeline can only be written forwards. Each item carries the
/// generator's SETTLED TICK, its promise that nothing at or before that tick will change or be
/// added, and this buffer is where that promise is turned into an ordered stream of events.
/// </para>
/// <para>
/// IT PLACES THE SEGMENT. A generator's ticks start at 0 every time; a segment's place on the
/// timeline is the caller's business. Everything that comes out of here has
/// <see cref="SegmentStartTick"/> added to it, and A NOTE IS REBUILT RATHER THAN MOVED - setting
/// <c>AbsoluteTime</c> on a note-on leaves its note-off where it was and silently changes the
/// note's length.
/// </para>
/// <para>
/// EVENTS THAT SHARE A TICK COME OUT IN A SETTLED ORDER: tempo, metre, key and the other meta
/// events first, then programs and controllers, then the notes - because a tempo describes the bar
/// it opens and a program change must not arrive after the note it voices. Within one of those
/// groups the generator's own order is kept.
/// </para>
/// </remarks>
internal sealed class ReorderBuffer
{
    private readonly List<Held> held = new List<Held>();

    private long settledThroughTick;
    private bool sorted = true;
    private int arrival;

    /// <summary>Creates a buffer for a segment that starts at a tick on the timeline.</summary>
    /// <param name="segmentStartTick">Where the segment starts, in ticks from the start of the timeline.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tick is negative.</exception>
    public ReorderBuffer(long segmentStartTick)
    {
        if (segmentStartTick < 0L)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentStartTick), segmentStartTick,
                "A segment starts at a non-negative tick on the timeline.");
        }

        SegmentStartTick = segmentStartTick;
        settledThroughTick = segmentStartTick - 1L;
    }

    /// <summary>Where the segment starts, in ticks from the start of the timeline.</summary>
    /// <remarks>
    /// It moves later when the music is HELD BACK - see <see cref="HoldBy"/> - and never otherwise.
    /// </remarks>
    public long SegmentStartTick { get; private set; }

    /// <summary>
    /// The timeline tick through which the generator has settled: everything at or before it may
    /// be released. It is one tick BEFORE the segment's start until the generator settles anything.
    /// </summary>
    public long SettledThroughTick => settledThroughTick;

    /// <summary>Whether the generator has settled anything at all yet.</summary>
    public bool HasSettled => settledThroughTick >= SegmentStartTick;

    /// <summary>How many events are still being held back.</summary>
    public int HeldCount => held.Count;

    /// <summary>
    /// The earliest tick anything still being held back sits at, or -1 when nothing is held. It is
    /// part of the answer to "where does the music that has not been committed begin".
    /// </summary>
    public long EarliestHeldTick
    {
        get
        {
            if (held.Count == 0)
            {
                return -1L;
            }

            var earliest = held[0].Tick;

            for (var i = 1; i < held.Count; i++)
            {
                if (held[i].Tick < earliest)
                {
                    earliest = held[i].Tick;
                }
            }

            return earliest;
        }
    }

    /// <summary>Takes in one item from the generator.</summary>
    /// <param name="item">The item, at a tick measured from the start of the segment.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Add(GeneratedMusicEvent item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (item.HasEvent)
        {
            var source = item.Event;
            var tick = SegmentStartTick + source.AbsoluteTime;
            var placed = Place(source, tick);

            if (held.Count > 0 && Precedes(tick, RankOf(placed), held[held.Count - 1]))
            {
                sorted = false;
            }

            held.Add(new Held(tick, RankOf(placed), arrival++, placed));
        }

        if (item.HasSettled)
        {
            var settled = SegmentStartTick + item.SettledThroughTick;

            if (settled > settledThroughTick)
            {
                settledThroughTick = settled;
            }
        }
    }

    /// <summary>
    /// Releases everything the generator has settled, in tick order. Events at a tick beyond the
    /// settled tick stay here until it passes them.
    /// </summary>
    /// <returns>The events, at their ticks on the timeline, in the order they are to be played.</returns>
    public IReadOnlyList<MidiEvent> TakeReleased()
    {
        if (held.Count == 0)
        {
            return Array.Empty<MidiEvent>();
        }

        if (!sorted)
        {
            held.Sort(CompareHeld);
            sorted = true;
        }

        var count = 0;

        while (count < held.Count && held[count].Tick <= settledThroughTick)
        {
            count++;
        }

        if (count == 0)
        {
            return Array.Empty<MidiEvent>();
        }

        var released = new MidiEvent[count];

        for (var i = 0; i < count; i++)
        {
            released[i] = held[i].Event;
        }

        held.RemoveRange(0, count);

        return released;
    }

    /// <summary>
    /// Throws away everything still being held. This is what a follow-up prompt does to the music
    /// it is replacing: what has already been released is gone, and what has not is not played.
    /// </summary>
    public void DiscardHeld()
    {
        held.Clear();
        sorted = true;
    }

    /// <summary>
    /// Moves the whole of this segment later by a number of ticks: everything still held, the
    /// settled tick, and the place the segment sits at - so that everything the generator has yet
    /// to write lands later too.
    /// </summary>
    /// <param name="delta">How far to move it. Zero or less does nothing.</param>
    /// <remarks>
    /// IT IS HOW THE MUSIC WAITS. The engine holds music back by a whole number of bars when the
    /// play head has reached the place that music belongs; what has already been released is not
    /// here any more and is not touched.
    /// </remarks>
    public void HoldBy(long delta)
    {
        if (delta <= 0L)
        {
            return;
        }

        SegmentStartTick += delta;
        settledThroughTick += delta;

        for (var i = 0; i < held.Count; i++)
        {
            var moved = held[i].Tick + delta;

            held[i] = new Held(moved, held[i].Rank, held[i].Arrival,
                MusicEventPlacement.At(held[i].Event, moved));
        }
    }

    // A NOTE IS REBUILT RATHER THAN MOVED - MusicEventPlacement says why.
    private static MidiEvent Place(MidiEvent source, long tick) =>
        MusicEventPlacement.At(source, tick);

    private static int RankOf(MidiEvent midiEvent)
    {
        if (midiEvent.CommandCode == MidiCommandCode.MetaEvent)
        {
            return 0;
        }

        return midiEvent.CommandCode == MidiCommandCode.NoteOn ? 2 : 1;
    }

    private static bool Precedes(long tick, int rank, in Held other)
    {
        if (tick != other.Tick)
        {
            return tick < other.Tick;
        }

        return rank < other.Rank;
    }

    private static int CompareHeld(Held left, Held right)
    {
        var byTick = left.Tick.CompareTo(right.Tick);

        if (byTick != 0)
        {
            return byTick;
        }

        var byRank = left.Rank.CompareTo(right.Rank);

        return byRank != 0 ? byRank : left.Arrival.CompareTo(right.Arrival);
    }

    private readonly struct Held
    {
        public Held(long tick, int rank, int arrival, MidiEvent midiEvent)
        {
            Tick = tick;
            Rank = rank;
            Arrival = arrival;
            Event = midiEvent;
        }

        public long Tick { get; }

        public int Rank { get; }

        public int Arrival { get; }

        public MidiEvent Event { get; }
    }
}
