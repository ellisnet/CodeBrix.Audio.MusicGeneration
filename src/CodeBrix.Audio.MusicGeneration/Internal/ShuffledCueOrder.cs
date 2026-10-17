using System;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>
/// The order a set of cues is played in: a shuffled bag. Every cue is drawn once before any cue is
/// drawn again, and when the bag runs out it is refilled and reshuffled so that its first cue is
/// never the cue that was drawn last - so no cue ever plays twice in a row.
/// </summary>
/// <remarks>
/// <para>
/// PEEKING IS NOT DRAWING. <see cref="Peek"/> says which cue <see cref="Next"/> will hand out, and
/// asking it again gives the same answer until a cue is drawn. That is what lets a generator say
/// how long its next pass will be before it plays it.
/// </para>
/// <para>
/// THE RANDOMNESS IS INJECTED, so a test hands in a seeded <see cref="Random"/> and gets the same
/// order every time, while the library itself hands in an unseeded one. The instance is safe to
/// use from more than one thread: every member takes the same lock.
/// </para>
/// </remarks>
internal sealed class ShuffledCueOrder
{
    private readonly object gate = new object();
    private readonly Random random;
    private readonly int[] bag;

    private int position;
    private int lastDrawn = -1;

    /// <summary>Creates the order for a set of cues.</summary>
    /// <param name="count">How many cues there are; they are numbered from 0.</param>
    /// <param name="random">Where the shuffles come from.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than 1.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
    public ShuffledCueOrder(int count, Random random)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count,
                "A shuffled order needs at least one cue to shuffle.");
        }

        this.random = random ?? throw new ArgumentNullException(nameof(random));
        bag = new int[count];

        // An empty bag: the first Peek or Next fills it.
        position = count;
    }

    /// <summary>How many cues are being shuffled.</summary>
    public int Count => bag.Length;

    /// <summary>The cue the next call to <see cref="Next"/> will hand out, without drawing it.</summary>
    /// <returns>The cue's number, from 0 to <see cref="Count"/> minus 1.</returns>
    public int Peek()
    {
        lock (gate)
        {
            FillIfEmpty();

            return bag[position];
        }
    }

    /// <summary>Draws the next cue.</summary>
    /// <returns>The cue's number, from 0 to <see cref="Count"/> minus 1.</returns>
    public int Next()
    {
        lock (gate)
        {
            FillIfEmpty();

            lastDrawn = bag[position++];

            return lastDrawn;
        }
    }

    private void FillIfEmpty()
    {
        if (position < bag.Length)
        {
            return;
        }

        for (var i = 0; i < bag.Length; i++)
        {
            bag[i] = i;
        }

        // Fisher-Yates.
        for (var i = bag.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }

        if (bag.Length > 1 && bag[0] == lastDrawn)
        {
            // The new bag would open with the cue that just finished. Swap it with any other
            // position, so the cue that ends one bag never opens the next.
            var swap = 1 + random.Next(bag.Length - 1);
            (bag[0], bag[swap]) = (bag[swap], bag[0]);
        }

        position = 0;
    }
}
