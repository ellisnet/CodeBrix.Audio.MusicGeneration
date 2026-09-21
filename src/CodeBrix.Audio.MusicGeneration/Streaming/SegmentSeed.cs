namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// Where a continuation segment's seed comes from: the piece's own seed and the segment number,
/// mixed so that every segment is different and the whole piece is still reproducible.
/// </summary>
/// <remarks>
/// <para>
/// KEEP THE CHARACTER, CHANGE THE SEED. Asking a model to carry on with the SAME seed writes the
/// same music again, so every segment needs its own - but the temperature, the top-k and the top-p
/// stay exactly as they were, because those are what the music SOUNDS like and a listener would
/// hear them change at a seam.
/// </para>
/// <para>
/// IT IS DERIVED RATHER THAN RANDOM so that a piece generated from a given seed is the same piece
/// every time, however many segments long it grows.
/// </para>
/// </remarks>
internal static class SegmentSeed
{
    /// <summary>Works out the seed for a segment.</summary>
    /// <param name="seed">The seed the caller asked the whole piece for.</param>
    /// <param name="segmentNumber">Which segment this is, counting the first piece as 1.</param>
    /// <returns>A non-negative seed, different for each segment number and the same every time.</returns>
    public static int Derive(int seed, int segmentNumber)
    {
        unchecked
        {
            var mixed = (uint)seed;

            mixed ^= (uint)segmentNumber * 2654435761U;
            mixed = (mixed * 2246822519U) + 3266489917U;
            mixed ^= mixed >> 15;
            mixed = (mixed * 2654435761U) + (uint)segmentNumber;
            mixed ^= mixed >> 13;

            return (int)(mixed & 0x7FFFFFFFU);
        }
    }
}
