using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>
/// A piece of music prepared for replay: every item in tick order, at one tick resolution, with
/// the arithmetic needed to know when each one is due.
/// </summary>
internal sealed class ReplayPiece
{
    private readonly ReplayItem[] items;

    /// <summary>Creates a prepared piece.</summary>
    /// <param name="ticksPerQuarterNote">The resolution the items' ticks are at.</param>
    /// <param name="totalTicks">How long one pass is, rounded up to a whole bar.</param>
    /// <param name="items">The items, in tick order.</param>
    /// <param name="tempoMap">The tempo map, for turning a tick into a moment.</param>
    public ReplayPiece(int ticksPerQuarterNote, long totalTicks, ReplayItem[] items,
        MidiTempoMap tempoMap)
    {
        TicksPerQuarterNote = ticksPerQuarterNote;
        TotalTicks = totalTicks;
        this.items = items;
        TempoMap = tempoMap;
    }

    /// <summary>The resolution the items' ticks are at.</summary>
    public int TicksPerQuarterNote { get; }

    /// <summary>
    /// How long one pass of the piece is, in ticks: the end of its music rounded UP to a whole bar,
    /// so that the pass after it starts on a bar line.
    /// </summary>
    public long TotalTicks { get; }

    /// <summary>The items, in tick order.</summary>
    public IReadOnlyList<ReplayItem> Items => items;

    /// <summary>The tempo map of the piece.</summary>
    public MidiTempoMap TempoMap { get; }

    /// <summary>How long one pass of the piece lasts in real time, at its own tempo.</summary>
    public TimeSpan Duration => TimeOfTick(TotalTicks);

    /// <summary>When a tick happens, measured from the start of the pass.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The moment that tick is reached at the piece's own tempo.</returns>
    public TimeSpan TimeOfTick(long tick) =>
        TempoMap.TimeAt((double)tick / TicksPerQuarterNote);
}
