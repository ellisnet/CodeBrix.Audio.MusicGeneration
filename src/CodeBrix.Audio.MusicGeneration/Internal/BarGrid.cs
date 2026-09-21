using System.Collections.Generic;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>
/// Where the bar lines of a piece fall, given every metre it uses. A segment that ends on a bar
/// line is a segment the next one can start against without a mid-bar join.
/// </summary>
internal static class BarGrid
{
    /// <summary>
    /// Rounds a tick up to the next bar line, or leaves it where it is when it already sits on one.
    /// </summary>
    /// <param name="changes">
    /// Every metre change, in ascending tick order. A change is taken to start a new bar. When the
    /// list is empty, or starts after tick 0, common time is assumed from tick 0.
    /// </param>
    /// <param name="ticksPerQuarterNote">The tick resolution.</param>
    /// <param name="tick">The tick to round up. Zero and negatives read as zero.</param>
    /// <returns>The tick of the bar line at or after <paramref name="tick"/>.</returns>
    public static long RoundUpToBar(IReadOnlyList<MeterChange> changes, int ticksPerQuarterNote, long tick)
    {
        if (tick <= 0L)
        {
            return 0L;
        }

        var grid = Normalise(changes);

        for (var i = 0; i < grid.Count; i++)
        {
            var segmentStart = grid[i].Tick;
            var isLast = i == grid.Count - 1;
            var segmentEnd = isLast ? long.MaxValue : grid[i + 1].Tick;

            if (tick <= segmentStart)
            {
                // A metre change starts a bar, so the change's own tick IS a bar line.
                return segmentStart;
            }

            if (tick >= segmentEnd)
            {
                continue;
            }

            var ticksPerBar = grid[i].Meter.TicksPerBar(ticksPerQuarterNote);
            var offset = tick - segmentStart;
            var bars = (offset + ticksPerBar - 1L) / ticksPerBar;
            var candidate = segmentStart + (bars * ticksPerBar);

            // The metre changed part-way through a bar: the next bar line is the change itself.
            return !isLast && candidate > segmentEnd ? segmentEnd : candidate;
        }

        return tick;
    }

    /// <summary>
    /// Rounds a tick DOWN to the bar line at or before it, or leaves it where it is when it already
    /// sits on one.
    /// </summary>
    /// <param name="changes">
    /// Every metre change, in ascending tick order, read exactly as
    /// <see cref="RoundUpToBar"/> reads them.
    /// </param>
    /// <param name="ticksPerQuarterNote">The tick resolution.</param>
    /// <param name="tick">The tick to round down. Zero and negatives read as zero.</param>
    /// <returns>The tick of the bar line at or before <paramref name="tick"/>.</returns>
    public static long RoundDownToBar(IReadOnlyList<MeterChange> changes, int ticksPerQuarterNote,
        long tick)
    {
        if (tick <= 0L)
        {
            return 0L;
        }

        var grid = Normalise(changes);

        for (var i = grid.Count - 1; i >= 0; i--)
        {
            var segmentStart = grid[i].Tick;

            if (tick < segmentStart)
            {
                continue;
            }

            // A metre change starts a bar, so bars are counted from the change rather than from 0.
            var ticksPerBar = grid[i].Meter.TicksPerBar(ticksPerQuarterNote);
            var bars = (tick - segmentStart) / ticksPerBar;

            return segmentStart + (bars * ticksPerBar);
        }

        return 0L;
    }

    private static List<MeterChange> Normalise(IReadOnlyList<MeterChange> changes)
    {
        var grid = new List<MeterChange>();

        if (changes == null || changes.Count == 0 || changes[0].Tick > 0L)
        {
            grid.Add(new MeterChange(0L, MusicMeter.CommonTime));
        }

        if (changes != null)
        {
            for (var i = 0; i < changes.Count; i++)
            {
                // Two changes at one tick: the later one wins, because it is what is in force.
                if (grid.Count > 0 && grid[grid.Count - 1].Tick == changes[i].Tick)
                {
                    grid[grid.Count - 1] = changes[i];
                }
                else
                {
                    grid.Add(changes[i]);
                }
            }
        }

        return grid;
    }
}
