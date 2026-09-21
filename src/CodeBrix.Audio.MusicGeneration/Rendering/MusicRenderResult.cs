using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Rendering;

/// <summary>
/// What a finished render was: how long the file is, what made the music, where the seams fell,
/// and the music itself.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Music"/> IS THE WHOLE POINT OF THE LAST FIELD. A render owns the music it just
/// rendered, so an application can write the <c>.mid</c> of it beside the audio in one line:
/// </para>
/// <code>
/// var result = await music.RenderToFileAsync("theme.wav", render, cancellationToken);
///
/// MidiFile.Export("theme.mid", result.Music);
/// </code>
/// </remarks>
public sealed class MusicRenderResult
{
    private readonly long[] seamTicks;
    private readonly string[] diagnostics;

    /// <summary>Builds the result of one render.</summary>
    /// <param name="path">Where the file was written, or null for a stream.</param>
    /// <param name="format">The extension the format was chosen by.</param>
    /// <param name="frameCount">How many frames were written.</param>
    /// <param name="sampleRate">The rate they were rendered at.</param>
    /// <param name="ending">How the file ends.</param>
    /// <param name="targetLength">How long it was meant to be, or null.</param>
    /// <param name="reachedTargetLength">Whether it got there.</param>
    /// <param name="fadeLength">The fade actually applied.</param>
    /// <param name="fadeCurve">The shape that fade followed.</param>
    /// <param name="source">What made the music.</param>
    /// <param name="segmentCount">How many segments it took.</param>
    /// <param name="seamTicks">Where they joined.</param>
    /// <param name="music">The music itself.</param>
    /// <param name="diagnostics">Anything a caller should know.</param>
    internal MusicRenderResult(string path, string format, long frameCount, int sampleRate,
        RenderEnding ending, TimeSpan? targetLength, bool reachedTargetLength, TimeSpan fadeLength,
        MusicFadeCurve fadeCurve, ActiveMusicSource source, int segmentCount, long[] seamTicks,
        MidiEventCollection music, string[] diagnostics)
    {
        Path = path;
        Format = format;
        FrameCount = frameCount;
        SampleRate = sampleRate;
        Ending = ending;
        TargetLength = targetLength;
        ReachedTargetLength = reachedTargetLength;
        FadeLength = fadeLength;
        FadeCurve = fadeCurve;
        Source = source;
        SegmentCount = segmentCount;
        this.seamTicks = seamTicks;
        Music = music;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// The file that was written, or null when the audio went to a stream the caller supplied.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The extension the format was chosen by, lower case and with its leading dot - ".wav",
    /// ".opus".
    /// </summary>
    public string Format { get; }

    /// <summary>How long the file is, to the frame.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);

    /// <summary>
    /// How many frames were written. One frame is one sample of every channel, so a stereo file
    /// holds twice this many samples.
    /// </summary>
    public long FrameCount { get; }

    /// <summary>The rate the audio was rendered at, in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>The number of channels. A render is always stereo.</summary>
    public int Channels => 2;

    /// <summary>How the file ends.</summary>
    public RenderEnding Ending { get; }

    /// <summary>How long the file was meant to be, or null when the render had no target.</summary>
    public TimeSpan? TargetLength { get; }

    /// <summary>
    /// Whether the file really is <see cref="TargetLength"/> long. It is false when the music ran
    /// out first - a generator that ended and could not be continued - and
    /// <see cref="Diagnostics"/> says so. It is true when there was no target to reach.
    /// </summary>
    public bool ReachedTargetLength { get; }

    /// <summary>
    /// The fade that was APPLIED, which is zero for an ending that does not fade and is shorter
    /// than <see cref="MusicRenderOptions.FadeLength"/> when the fade was longer than the file.
    /// </summary>
    public TimeSpan FadeLength { get; }

    /// <summary>The shape that fade followed.</summary>
    public MusicFadeCurve FadeCurve { get; }

    /// <summary>
    /// What really made the music: the generator, the instrument library, the rendition, and what
    /// every part that sounded was played with.
    /// </summary>
    public ActiveMusicSource Source { get; }

    /// <summary>
    /// How many segments the music took. It is 1 for a piece the generator wrote in one pass, and
    /// one more for each time it was continued to reach the target length.
    /// </summary>
    public int SegmentCount { get; }

    /// <summary>
    /// The tick each segment starts at, the first of them included. Every one of them is a BAR
    /// LINE: a continuation never starts mid-bar.
    /// </summary>
    public IReadOnlyList<long> SeamTicks => seamTicks;

    /// <summary>
    /// THE MUSIC ITSELF, as it was rendered, ready to be written as a <c>.mid</c> with
    /// <c>MidiFile.Export</c>. It is every segment on one timeline, at the resolution the request
    /// asked for.
    /// </summary>
    public MidiEventCollection Music { get; }

    /// <summary>
    /// Anything about this render a caller should know: a fade that was clamped, a target that was
    /// not reached, a part that could not be voiced. It is empty when there is nothing to say.
    /// </summary>
    public IReadOnlyList<string> Diagnostics => diagnostics;

    /// <summary>A line a console application can write straight out.</summary>
    /// <returns>The description.</returns>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture,
            "{0}: {1:mm\\:ss\\.fff} ({2} frames at {3} Hz), {4}, {5} segment(s)",
            Path ?? Format, Duration, FrameCount, SampleRate, Ending, SegmentCount);
}
