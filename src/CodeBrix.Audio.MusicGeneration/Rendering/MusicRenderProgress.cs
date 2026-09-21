using System;
using System.Globalization;

namespace CodeBrix.Audio.MusicGeneration.Rendering;

/// <summary>
/// How a render is getting on: what it is doing, how much music it has written, and how far
/// through the target that is.
/// </summary>
/// <remarks>
/// It is handed to the <see cref="IProgress{T}"/> a caller passed to the render, on the thread the
/// render is running on. <see cref="Music"/> and <see cref="Fraction"/> NEVER GO BACKWARDS, and
/// the last report of every render that finishes is
/// <see cref="MusicRenderStage.Finished"/> at a fraction of 1.
/// </remarks>
public sealed class MusicRenderProgress
{
    /// <summary>Builds one report.</summary>
    /// <param name="stage">What the render is doing.</param>
    /// <param name="music">How much audio has been written.</param>
    /// <param name="targetLength">How long the file is meant to be, or null.</param>
    /// <param name="fraction">How far through the render is, from 0 to 1.</param>
    /// <param name="segmentCount">How many segments the music has taken so far.</param>
    internal MusicRenderProgress(MusicRenderStage stage, TimeSpan music, TimeSpan? targetLength,
        double fraction, int segmentCount)
    {
        Stage = stage;
        Music = music;
        TargetLength = targetLength;
        Fraction = fraction;
        SegmentCount = segmentCount;
    }

    /// <summary>What the render is doing.</summary>
    public MusicRenderStage Stage { get; }

    /// <summary>
    /// How much audio has been written so far - seconds of music, not seconds of work.
    /// </summary>
    public TimeSpan Music { get; }

    /// <summary>How long the file is meant to be, or null when the render has no target.</summary>
    public TimeSpan? TargetLength { get; }

    /// <summary>
    /// How far through the render is, from 0 to 1: <see cref="Music"/> against
    /// <see cref="TargetLength"/>.
    /// </summary>
    /// <remarks>
    /// WITH NO TARGET LENGTH there is nothing to be a fraction of - a render that stops at the
    /// generator's own ending cannot know where that will be - so it reads 0 until the render
    /// finishes, and 1 then.
    /// </remarks>
    public double Fraction { get; }

    /// <summary>
    /// How many segments the music has taken so far. It is 1 for a piece a generator wrote in one
    /// pass, and one more for every time it was continued to reach the target length.
    /// </summary>
    public int SegmentCount { get; }

    /// <summary>A line a console application can write straight out.</summary>
    /// <returns>The description.</returns>
    public override string ToString() =>
        TargetLength.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "{0}: {1:mm\\:ss} of {2:mm\\:ss} ({3:P0}), {4} segment(s)",
                Stage, Music, TargetLength.Value, Fraction, SegmentCount)
            : string.Format(CultureInfo.InvariantCulture, "{0}: {1:mm\\:ss}, {2} segment(s)",
                Stage, Music, SegmentCount);
}
