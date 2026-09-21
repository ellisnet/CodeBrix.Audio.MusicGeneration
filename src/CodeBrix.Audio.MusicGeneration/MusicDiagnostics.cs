using System;
using System.Globalization;
using CodeBrix.Audio.MusicGeneration.Rendition;

namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// How the music is doing, as one cheap snapshot: a game loop can read
/// <see cref="MusicSession.Diagnostics"/> every frame without slowing anything down.
/// </summary>
/// <remarks>
/// <para>
/// WHEN TO PRE-GENERATE OR RENDER AHEAD INSTEAD OF STREAMING. Watch
/// <see cref="RealTimeFactor"/>. It is how many seconds of music the generator produces for every
/// second it spends generating, so 1 is exactly real time, and it is NULL until there has been
/// enough generating to measure - a value that is there is always a measurement. A value that
/// STAYS BELOW 1 means the device cannot compose this music as fast as it plays, and no amount of
/// buffering fixes that - a bigger lookahead only postpones the gap. On such a device:
/// </para>
/// <list type="bullet">
/// <item><description>
/// render the music to an audio file ahead of time and play the file, which is what the offline
/// render is for - generation then happens once, at whatever speed the machine manages;
/// </description></item>
/// <item><description>
/// or generate during a loading screen and play what was generated;
/// </description></item>
/// <item><description>
/// or accept <see cref="MusicDeliveryMode.SegmentAtATime"/>, which the engine switches to by
/// itself: phrases separated by rests, which sounds intentional, instead of a stream that stalls
/// mid-phrase, which sounds broken.
/// </description></item>
/// </list>
/// <para>
/// A factor that is comfortably above 1 and a <see cref="StarvationGapCount"/> that stays put mean
/// the stream is healthy. Gaps that keep appearing while the factor is above 1 point at the
/// pre-roll being too short for how bursty the generator is, not at the machine.
/// </para>
/// <para>
/// SILENCE IS NOT A FAILURE. Every number here exists so that an application can SEE what the music
/// is doing and decide for itself; none of them is an error condition.
/// </para>
/// </remarks>
public sealed class MusicDiagnostics
{
    internal MusicDiagnostics(int starvationGapCount, double? realTimeFactor, MusicDeliveryMode mode,
        TimeSpan lead, int segmentCount, int lateEventCount, int holdCount, int heldBarCount,
        MusicVoicing voicing)
    {
        StarvationGapCount = starvationGapCount;
        RealTimeFactor = realTimeFactor;
        Mode = mode;
        Lead = lead;
        SegmentCount = segmentCount;
        LateEventCount = lateEventCount;
        HoldCount = holdCount;
        HeldBarCount = heldBarCount;
        Voicing = voicing;
    }

    /// <summary>
    /// How many times the play head has caught up with the music and had to wait. It counts GAPS,
    /// not the moments inside one: a single long wait is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT COUNTS THE GAP EVEN WHEN A NOTE IS STILL SOUNDING OVER IT. A note written with a duration
    /// keeps the timeline from reporting that it has run dry, but the head has run out of MUSIC all
    /// the same, and that is what is counted here.
    /// </para>
    /// <para>
    /// THE WAIT FOR THE FIRST PRE-ROLL IS NOT A GAP. A gap is the music RUNNING OUT, and it cannot
    /// run out before it has started, so an ordinary start reports nought however long the first
    /// few seconds take.
    /// </para>
    /// </remarks>
    public int StarvationGapCount { get; }

    /// <summary>
    /// Seconds of settled music produced for every second of wall-clock time SPENT GENERATING -
    /// time the engine was paused because it was far enough ahead does not count against it. It is
    /// NULL until there has been enough generating to measure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a moving average weighted towards the recent past, because a model's rate falls as a
    /// piece grows and the useful question is "can it keep up NOW".
    /// </para>
    /// <para>
    /// NULL MEANS "NOT MEASURED YET", AND IT IS NOT A NUMBER ON PURPOSE - a game loop asking
    /// <c>RealTimeFactor &lt; 1.0</c> of a nought would be told, every time music started, that the
    /// machine could not keep up. A value that is there is always a measurement, so
    /// <c>diagnostics.RealTimeFactor &lt; 1.0</c> reads correctly: it is false while nothing is
    /// known.
    /// </para>
    /// </remarks>
    public double? RealTimeFactor { get; }

    /// <summary>Whether music is reaching the timeline as a stream or a whole segment at a time.</summary>
    public MusicDeliveryMode Mode { get; }

    /// <summary>
    /// Whether the engine has fallen back to delivering a whole segment at a time, which it does by
    /// itself when generation is measured slower than real time.
    /// </summary>
    public bool IsSegmentAtATime => Mode == MusicDeliveryMode.SegmentAtATime;

    /// <summary>
    /// How much settled music is waiting ahead of the play head. It is the number the generate-ahead
    /// window is measured in, and it never goes below zero.
    /// </summary>
    public TimeSpan Lead { get; }

    /// <summary>
    /// How many segments have been generated. It counts the first piece, every continuation the
    /// engine asked for at the end of a pass, and every follow-up prompt that took over.
    /// </summary>
    public int SegmentCount { get; }

    /// <summary>
    /// How many events reached the timeline at a tick the play head had already passed. They are
    /// played at once and never dropped, but a number that keeps climbing says the pre-roll is too
    /// short for this generator.
    /// </summary>
    public int LateEventCount { get; }

    /// <summary>
    /// How many times the music has been HELD BACK to a later bar line because the play head had
    /// reached the place it belonged - after a rest, or while the generator was behind.
    /// </summary>
    /// <remarks>
    /// It is why <see cref="LateEventCount"/> stays at zero however slow the machine is. Each hold
    /// is heard as a rest of whole bars: the phrase finishes, the notes still sounding ring out as
    /// they were written, and the music carries on in time. A count that keeps climbing says the
    /// generator is not keeping up, which <see cref="RealTimeFactor"/> says in seconds.
    /// </remarks>
    public int HoldCount { get; }

    /// <summary>How many whole bars of rest those holds have inserted altogether.</summary>
    public int HeldBarCount { get; }

    /// <summary>
    /// What every part that has sounded is being played with, and why. It is null until the music
    /// has started.
    /// </summary>
    public MusicVoicing Voicing { get; }

    /// <summary>Describes the state of the stream in one line, for a log.</summary>
    /// <returns>The line.</returns>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture,
            "{0}: lead {1:0.0} s, real-time factor {2}, {3} segment(s), {4} gap(s), " +
            "{5} late event(s), {6} hold(s) of {7} bar(s)",
            Mode, Lead.TotalSeconds, DescribeRealTimeFactor(), SegmentCount, StarvationGapCount,
            LateEventCount, HoldCount, HeldBarCount);

    private string DescribeRealTimeFactor() =>
        RealTimeFactor.HasValue
            ? RealTimeFactor.Value.ToString("0.00", CultureInfo.InvariantCulture)
            : "not measured yet";
}
