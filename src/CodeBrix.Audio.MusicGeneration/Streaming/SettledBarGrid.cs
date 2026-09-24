using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// Where the bar lines of the music ON THE TIMELINE fall: it watches the metre changes as they are
/// committed and answers the one question the seam rules ask - which tick is a bar line.
/// </summary>
/// <remarks>
/// <para>
/// A SEGMENT STARTS ON A BAR LINE, NEVER MID-BAR. That is the first of the seam levers and the one
/// a listener notices immediately when it is missing, so every place a new segment is put on the
/// timeline - the end of a pass, a follow-up prompt taking over - asks this.
/// </para>
/// <para>
/// It only ever sees metre changes that have been COMMITTED, so the grid describes the music that
/// is really going to be heard rather than a lookahead that might still be thrown away.
/// </para>
/// </remarks>
internal sealed class SettledBarGrid
{
    private readonly int ticksPerQuarterNote;
    private readonly List<MeterChange> changes = new List<MeterChange>();

    private MusicMeter current = MusicMeter.CommonTime;

    /// <summary>Creates a grid at a tick resolution, in common time until the music says otherwise.</summary>
    /// <param name="ticksPerQuarterNote">The resolution the timeline counts ticks at.</param>
    public SettledBarGrid(int ticksPerQuarterNote) => this.ticksPerQuarterNote = ticksPerQuarterNote;

    /// <summary>The metre in force at the last tick seen.</summary>
    public MusicMeter CurrentMeter => current;

    /// <summary>How long a bar is at the metre in force.</summary>
    public long CurrentTicksPerBar => current.TicksPerBar(ticksPerQuarterNote);

    /// <summary>Takes in one committed event, and records it when it is a metre change.</summary>
    /// <param name="midiEvent">The event, at its tick on the timeline.</param>
    public void Observe(MidiEvent midiEvent)
    {
        if (midiEvent is not TimeSignatureEvent signature)
        {
            return;
        }

        var numerator = signature.Numerator;
        var denominator = signature.Denominator;

        if (numerator < 1 || denominator < 0 || denominator > 7)
        {
            // Not a metre anything can count bars in. The grid keeps what it had.
            return;
        }

        var meter = new MusicMeter(numerator, 1 << denominator);

        if (changes.Count > 0 && changes[changes.Count - 1].Tick == signature.AbsoluteTime)
        {
            changes[changes.Count - 1] = new MeterChange(signature.AbsoluteTime, meter);
        }
        else if (changes.Count == 0 || changes[changes.Count - 1].Tick < signature.AbsoluteTime)
        {
            changes.Add(new MeterChange(signature.AbsoluteTime, meter));
        }
        else
        {
            // Out of order, which committed music never is. Nothing sensible to do but keep the grid.
            return;
        }

        current = meter;
    }

    /// <summary>
    /// Takes the whole of another grid, which is how the bar lines OF THE LOOKAHEAD are put back to
    /// the bar lines OF WHAT IS PLAYING when a follow-up prompt throws that lookahead away.
    /// </summary>
    /// <param name="other">The grid to take.</param>
    public void CopyFrom(SettledBarGrid other)
    {
        changes.Clear();
        changes.AddRange(other.changes);
        current = other.current;
    }

    /// <summary>
    /// Moves every metre change from a tick onwards later by a number of ticks, which is what
    /// happens to the metre of music that has been HELD BACK: it travels with the music it belongs
    /// to, and the bars of the rest in front of it are counted in the metre already in force.
    /// </summary>
    /// <param name="fromTick">The first tick that moves.</param>
    /// <param name="delta">
    /// How far to move it, which the engine keeps a whole number of bars so that everything after
    /// the rest still falls on the grid.
    /// </param>
    public void HoldFrom(long fromTick, long delta)
    {
        if (delta <= 0L)
        {
            return;
        }

        for (var i = 0; i < changes.Count; i++)
        {
            if (changes[i].Tick >= fromTick)
            {
                changes[i] = new MeterChange(changes[i].Tick + delta, changes[i].Meter);
            }
        }
    }

    /// <summary>
    /// Forgets every metre change at or after a tick, which is what happens to the bar lines of
    /// music that has left the timeline: the metre in force goes back to the last change before it.
    /// </summary>
    /// <param name="fromTick">The first tick forgotten.</param>
    public void DiscardFrom(long fromTick)
    {
        changes.RemoveAll(change => change.Tick >= fromTick);
        current = changes.Count == 0 ? MusicMeter.CommonTime : changes[changes.Count - 1].Meter;
    }

    /// <summary>The bar line at or after a tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The tick of that bar line.</returns>
    public long BarLineAtOrAfter(long tick) => BarGrid.RoundUpToBar(changes, ticksPerQuarterNote, tick);

    /// <summary>The bar line at or before a tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The tick of that bar line.</returns>
    public long BarLineAtOrBefore(long tick) => BarGrid.RoundDownToBar(changes, ticksPerQuarterNote, tick);
}
