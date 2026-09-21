using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Pulls a whole segment out of a generator, moving a <see cref="ManualTimeProvider"/> forward only
/// when the generator is actually waiting on it, and recording the moment each item was released.
/// </summary>
/// <remarks>
/// The clock never runs ahead of what the generator is waiting for, so a recorded moment is the
/// moment the item was due - which is what makes the pacing assertions exact.
/// </remarks>
public static class GenerationPump
{
    private const int SpinLimit = 100000;

    /// <summary>Pulls a whole segment, recording when each item arrived.</summary>
    /// <param name="source">The segment.</param>
    /// <param name="time">The clock the generator is pacing against.</param>
    /// <param name="cancellationToken">Stops the pull.</param>
    /// <returns>Every item, in the order it was released, with the moment it was released.</returns>
    public static async Task<IReadOnlyList<ReleasedItem>> DrainAsync(
        IAsyncEnumerable<GeneratedMusicEvent> source, ManualTimeProvider time,
        CancellationToken cancellationToken)
    {
        var released = new List<ReleasedItem>();

        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            var move = enumerator.MoveNextAsync();

            await PumpAsync(move, time).ConfigureAwait(false);

            if (!await move.ConfigureAwait(false))
            {
                break;
            }

            released.Add(new ReleasedItem(enumerator.Current, time.GetUtcNow()));
        }

        return released;
    }

    /// <summary>Pulls one item, moving the clock only when the generator waits on it.</summary>
    /// <param name="enumerator">The segment being pulled.</param>
    /// <param name="time">The clock the generator is pacing against.</param>
    /// <returns>True when an item arrived, false at the end of the segment.</returns>
    public static async Task<bool> MoveNextAsync(
        IAsyncEnumerator<GeneratedMusicEvent> enumerator, ManualTimeProvider time)
    {
        var move = enumerator.MoveNextAsync();

        await PumpAsync(move, time).ConfigureAwait(false);

        return await move.ConfigureAwait(false);
    }

    private static async Task PumpAsync(ValueTask<bool> move, ManualTimeProvider time)
    {
        var spins = 0;

        while (!move.IsCompleted)
        {
            if (!time.AdvanceToNextDue())
            {
                spins++;

                if (spins > SpinLimit)
                {
                    throw new InvalidOperationException(
                        "The generator stopped making progress: it is neither producing an item nor " +
                        "waiting on the clock.");
                }
            }
            else
            {
                spins = 0;
            }

            await Task.Yield();
        }
    }
}
