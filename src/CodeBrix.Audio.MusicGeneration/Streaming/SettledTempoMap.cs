using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// The engine's own record of every tempo that has gone past, so that it can turn a tick into a
/// moment of music and a moment of music back into a tick.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE ENGINE KEEPS ITS OWN. Everything the streaming rules are measured in is SECONDS OF
/// MUSIC - how far ahead of the play head to commit, how much to generate ahead - and seconds and
/// ticks are only the same thing while the tempo holds still. The timeline itself does not offer
/// the conversion for an arbitrary tick, and the music that would answer the question has not been
/// written to it yet. So the engine keeps the map as the music passes through it.
/// </para>
/// <para>
/// It starts at 120 beats to the minute, which is what a MIDI file means when it says nothing, and
/// it is extended in tick order as tempo events are released.
/// </para>
/// </remarks>
internal sealed class SettledTempoMap
{
    private const double DefaultMicrosecondsPerQuarterNote = 500000.0;

    private readonly int ticksPerQuarterNote;
    private readonly List<Section> sections = new List<Section>();

    /// <summary>Creates a map at a tick resolution, holding the default tempo.</summary>
    /// <param name="ticksPerQuarterNote">The resolution ticks are counted at.</param>
    /// <exception cref="ArgumentOutOfRangeException">The resolution is not positive.</exception>
    public SettledTempoMap(int ticksPerQuarterNote)
    {
        if (ticksPerQuarterNote < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote), ticksPerQuarterNote,
                "The number of ticks per quarter note must be a positive value.");
        }

        this.ticksPerQuarterNote = ticksPerQuarterNote;
        sections.Add(new Section(0L, 0.0, SecondsPerTick(DefaultMicrosecondsPerQuarterNote)));
    }

    /// <summary>
    /// Records a tempo that takes effect at a tick. Tempos must arrive in tick order, which is
    /// what the reorder buffer guarantees; one that does not is ignored.
    /// </summary>
    /// <param name="tick">The tick the tempo takes effect at.</param>
    /// <param name="microsecondsPerQuarterNote">The new tempo, as a MIDI file writes it.</param>
    public void Add(long tick, int microsecondsPerQuarterNote)
    {
        if (microsecondsPerQuarterNote < 1)
        {
            return;
        }

        var last = sections[sections.Count - 1];

        if (tick < last.Tick)
        {
            return;
        }

        var seconds = last.Seconds + ((tick - last.Tick) * last.SecondsPerTick);
        var secondsPerTick = SecondsPerTick(microsecondsPerQuarterNote);

        if (tick == last.Tick)
        {
            sections[sections.Count - 1] = new Section(tick, last.Seconds, secondsPerTick);

            return;
        }

        sections.Add(new Section(tick, seconds, secondsPerTick));
    }

    /// <summary>
    /// Moves every tempo from a tick onwards later by a number of ticks, as though a rest had been
    /// written in front of them. The rest itself takes the tempo in force where it begins.
    /// </summary>
    /// <param name="fromTick">The first tick that moves.</param>
    /// <param name="delta">How far to move it. Zero or less does nothing.</param>
    /// <remarks>
    /// It is what keeps seconds and ticks agreeing when the engine HOLDS MUSIC BACK by a whole
    /// number of bars: the music that was going to be heard at a tempo is still heard at that
    /// tempo, only later.
    /// </remarks>
    public void HoldFrom(long fromTick, long delta)
    {
        if (delta <= 0L)
        {
            return;
        }

        // Section 0 is the start of the timeline and never moves. Each section is recomputed from
        // the one before it, which has already been moved, so the sums stay consistent.
        for (var i = 1; i < sections.Count; i++)
        {
            if (sections[i].Tick < fromTick)
            {
                continue;
            }

            var previous = sections[i - 1];
            var tick = sections[i].Tick + delta;
            var seconds = previous.Seconds + ((tick - previous.Tick) * previous.SecondsPerTick);

            sections[i] = new Section(tick, seconds, sections[i].SecondsPerTick);
        }
    }

    /// <summary>
    /// Forgets every tempo change at or after a tick. The start of the timeline is never forgotten,
    /// so the map always has a tempo in force.
    /// </summary>
    /// <param name="fromTick">The first tick forgotten.</param>
    public void DiscardFrom(long fromTick)
    {
        for (var i = sections.Count - 1; i > 0; i--)
        {
            if (sections[i].Tick < fromTick)
            {
                return;
            }

            sections.RemoveAt(i);
        }
    }

    /// <summary>When a tick happens, measured from the start of the timeline.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The moment the music reaches that tick.</returns>
    public TimeSpan TimeOfTick(long tick)
    {
        if (tick <= 0L)
        {
            return TimeSpan.Zero;
        }

        var section = SectionAtTick(tick);

        return TimeSpan.FromSeconds(section.Seconds + ((tick - section.Tick) * section.SecondsPerTick));
    }

    /// <summary>Which tick the music has reached at a moment.</summary>
    /// <param name="time">The moment, measured from the start of the timeline.</param>
    /// <returns>The tick, rounded down, and never negative.</returns>
    public long TickAtTime(TimeSpan time)
    {
        var seconds = time.TotalSeconds;

        if (seconds <= 0.0)
        {
            return 0L;
        }

        var section = SectionAtTime(seconds);
        var ticks = section.Tick + ((seconds - section.Seconds) / section.SecondsPerTick);

        if (ticks >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)Math.Floor(ticks);
    }

    private double SecondsPerTick(double microsecondsPerQuarterNote) =>
        microsecondsPerQuarterNote / 1000000.0 / ticksPerQuarterNote;

    private Section SectionAtTick(long tick)
    {
        for (var i = sections.Count - 1; i > 0; i--)
        {
            if (sections[i].Tick <= tick)
            {
                return sections[i];
            }
        }

        return sections[0];
    }

    private Section SectionAtTime(double seconds)
    {
        for (var i = sections.Count - 1; i > 0; i--)
        {
            if (sections[i].Seconds <= seconds)
            {
                return sections[i];
            }
        }

        return sections[0];
    }

    private readonly struct Section
    {
        public Section(long tick, double seconds, double secondsPerTick)
        {
            Tick = tick;
            Seconds = seconds;
            SecondsPerTick = secondsPerTick;
        }

        public long Tick { get; }

        public double Seconds { get; }

        public double SecondsPerTick { get; }
    }
}
