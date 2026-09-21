using System;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// EVERY STARTING NUMBER THE STREAMING LIFECYCLE RUNS ON, in one place.
/// </summary>
/// <remarks>
/// <para>
/// THESE NUMBERS HAVE BEEN MEASURED AGAINST BOTH MODEL ADAPTERS - the real-time factor of each
/// model on real music, the cost of starting a continuation, the time a model takes to load - and
/// the measurements either confirmed them or corrected them. Keeping them together means that
/// correcting one is a one-line change in a file whose whole purpose is to hold the numbers,
/// rather than a search through the engine. MAINTAINER-README records how to run the measurements
/// again.
/// </para>
/// <para>
/// THE PRINCIPLE BEHIND EVERY ONE OF THEM: the application comes first and the music waits. Silence
/// while music buffers is an accepted outcome; slowing the application down is not. Where a number
/// could lean either way, it leans towards protecting whatever is hosting the music.
/// </para>
/// <para>
/// The public faces of these numbers are <see cref="MusicGenerationOptions.DefaultPreroll"/>,
/// <see cref="MusicGenerationOptions.DefaultGenerateAhead"/> and the constants on
/// <see cref="MusicEngine"/>; they all read their values from here.
/// </para>
/// </remarks>
internal static class StreamingDefaults
{
    /// <summary>
    /// How much music is written ahead of the play head before playing starts, and before it starts
    /// again after running dry. A slightly later start buys longer uninterrupted stretches.
    /// </summary>
    public static readonly TimeSpan Preroll = TimeSpan.FromSeconds(5.0);

    /// <summary>
    /// How far ahead of the play head music is committed to the timeline, as a multiple of the
    /// pre-roll. Twice is the smallest number that keeps a whole pre-roll of slack above the
    /// threshold playback resumes at.
    /// </summary>
    public const double CommitWindowFactor = 2.0;

    /// <summary>The shortest commit window, whatever the pre-roll is.</summary>
    public static readonly TimeSpan MinimumCommitWindow = TimeSpan.FromSeconds(2.0);

    /// <summary>
    /// How often the engine's background timer runs. It is the granularity of everything the engine
    /// decides, and it is small enough to be invisible and large enough to cost nothing.
    /// </summary>
    public static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(100.0);

    /// <summary>
    /// How much settled music to keep ahead of the play head before the engine stops pulling from
    /// the generator.
    /// </summary>
    /// <remarks>
    /// Thirty seconds covers the pause at a continuation seam several times over, keeps what is
    /// thrown away on a new prompt small, and is not so big that it pretends to rescue a device
    /// that generates slower than real time - a bigger window only postpones the gap.
    /// </remarks>
    public static readonly TimeSpan GenerateAhead = TimeSpan.FromSeconds(30.0);

    /// <summary>
    /// The fraction of the generate-ahead window the lead has to fall to before pulling resumes.
    /// The gap between the two marks is what makes generation run in short bursts with idle time
    /// between them rather than trickling constantly.
    /// </summary>
    public const double GenerateAheadResumeFactor = 0.5;

    /// <summary>
    /// The least clearance left between the play head and music that has been HELD BACK, whatever
    /// the pre-roll is. The pre-roll itself is the clearance normally - the stream will not resume
    /// until that much is written ahead of the head anyway - but a consumer may set a pre-roll of
    /// nothing, and a margin of nothing would leave the head to catch up with the music while the
    /// decision was being taken.
    /// </summary>
    public static readonly TimeSpan MinimumHoldMargin = TimeSpan.FromMilliseconds(250.0);

    /// <summary>
    /// The time constant of the real-time factor's moving average, measured in seconds of time
    /// SPENT GENERATING. A model's rate falls as a piece grows, so the figure has to follow the
    /// recent past rather than average the whole session.
    /// </summary>
    public static readonly TimeSpan RealTimeFactorWindow = TimeSpan.FromSeconds(10.0);

    /// <summary>
    /// How much generating has to have happened before the real-time factor is reported at all, or
    /// allowed to change the delivery mode. Below this there is not enough evidence to act on.
    /// </summary>
    public static readonly TimeSpan MinimumMeasuredGenerating = TimeSpan.FromSeconds(2.0);

    /// <summary>
    /// The real-time factor below which the engine gives up on a continuous stream and delivers a
    /// whole segment at a time.
    /// </summary>
    public const double FallbackEngagesBelow = 0.8;

    /// <summary>
    /// The real-time factor above which the engine goes back to a continuous stream. It is the
    /// reciprocal of <see cref="FallbackEngagesBelow"/>, so the two marks are the same distance
    /// from real time and the mode cannot flap around 1.
    /// </summary>
    public const double FallbackLeavesAbove = 1.25;

    /// <summary>
    /// How many bars of the music already played are handed to a generator as the tail of a
    /// continuation. Enough for a phrase, short enough to leave a model's context for new music.
    /// </summary>
    /// <remarks>
    /// MEASURED AND CONFIRMED on both roads: four bars costs about 50 ms more at a seam than one
    /// bar, and eight bars costs another 50 ms while doubling what the model has to read before
    /// every pass. A phrase is four bars.
    /// </remarks>
    public const int ContinuationTailBars = 4;

    /// <summary>
    /// HOW MUCH OF THE MACHINE A MODEL TAKES BY DEFAULT: one inference thread for every this many
    /// processors the machine reports.
    /// </summary>
    /// <remarks>
    /// A SHARE, NOT A NUMBER, because the right answer is different on a workstation and on a
    /// small board: a quarter of a twenty-four-thread laptop is six and a quarter of a four-core
    /// device is one, and both are the conservative answer for that machine.
    /// </remarks>
    public const int InferenceThreadShare = 4;

    /// <summary>
    /// The most threads the share ever asks for, however large the machine is.
    /// </summary>
    /// <remarks>
    /// MEASURED: on a sixteen-core laptop both models climb steeply to four threads and then
    /// flatten - four threads reach the same rate as eight, inside the run-to-run spread - so
    /// taking more than four buys nothing and costs the application the cores it needs.
    /// </remarks>
    public const int MaximumDefaultInferenceThreads = 4;

    /// <summary>
    /// The conservative default number of inference threads on THIS machine: a share of the
    /// processors it reports, at least one and never more than
    /// <see cref="MaximumDefaultInferenceThreads"/>.
    /// </summary>
    public static readonly int DefaultInferenceThreadCount = ConservativeThreadCount();

    private static int ConservativeThreadCount()
    {
        var share = Environment.ProcessorCount / InferenceThreadShare;

        if (share < 1)
        {
            return 1;
        }

        return share > MaximumDefaultInferenceThreads ? MaximumDefaultInferenceThreads : share;
    }
}
