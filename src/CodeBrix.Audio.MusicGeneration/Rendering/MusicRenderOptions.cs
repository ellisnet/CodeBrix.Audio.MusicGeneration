using System;

namespace CodeBrix.Audio.MusicGeneration.Rendering;

/// <summary>
/// What a render is told beyond the music itself: how long the file should be, how it should end,
/// and how the audio should be stored.
/// </summary>
/// <remarks>
/// <para>
/// WHAT TO PLAY IS NOT HERE. The generator, the instrument library, the rendition and the request
/// are the session's own <see cref="MusicGenerationOptions"/> - the same ones
/// <see cref="MusicSession.Play"/> uses - so rendering a piece and playing it are the same music
/// asked for the same way. This type is only about the FILE.
/// </para>
/// <code>
/// var render = new MusicRenderOptions { TargetLength = TimeSpan.FromMinutes(6.0) };
///
/// var result = await music.RenderToFileAsync("theme.wav", render, cancellationToken);
/// </code>
/// <para>
/// AN OPTIONS OBJECT WITH NOTHING SET is a valid render: one pass of the generator, ending at its
/// own ending, written in the file format's own default sample format.
/// </para>
/// </remarks>
public sealed class MusicRenderOptions
{
    /// <summary>
    /// How long the notes sounding at the end of a piece are allowed to ring on for, unless a
    /// caller says otherwise: five seconds. A render stops as soon as nothing is sounding, so this
    /// is a limit rather than a length.
    /// </summary>
    public static readonly TimeSpan DefaultRingOut = TimeSpan.FromSeconds(5.0);

    private TimeSpan? targetLength;
    private TimeSpan fadeLength = MusicFade.DefaultLength;
    private TimeSpan ringOut = DefaultRingOut;
    private int? bitsPerSample;

    /// <summary>
    /// How long the file should be, or null - the default - for however long the music turns out
    /// to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT IS REACHED BY GENERATING, not by cutting a longer piece down: the generator is asked for
    /// more music, from where the last of it left off, until the music passes the target. A
    /// generator that ends its piece early is continued at the next bar line, carrying the tempo,
    /// the metre, the key and each part's instrument across the seam.
    /// </para>
    /// <para>
    /// IT IS A RENDER OPTION AND NOT PART OF THE REQUEST. A generator that declares it honours a
    /// target length is given it as a hint as well - and a target the CALLER put in
    /// <see cref="Generation.MusicIntent.TargetLength"/> is never touched.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is set and is not positive.</exception>
    public TimeSpan? TargetLength
    {
        get => targetLength;
        set
        {
            if (value.HasValue && value.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A target length is a positive amount of music, or null for however long the " +
                    "music turns out to be.");
            }

            targetLength = value;
        }
    }

    /// <summary>
    /// How the file ends, or null - the default - for the ending that fits: a
    /// <see cref="RenderEnding.Fade"/> when <see cref="TargetLength"/> is set, and a
    /// <see cref="RenderEnding.NaturalStop"/> when it is not.
    /// </summary>
    /// <remarks>
    /// <see cref="RenderEnding.Fade"/> and <see cref="RenderEnding.HardCut"/> both need a target
    /// length, and asking for either without one is refused before anything is generated.
    /// </remarks>
    public RenderEnding? Ending { get; set; }

    /// <summary>
    /// How long the fade at the end is. It is <see cref="MusicFade.DefaultLength"/> unless it is
    /// set, and it is used only by <see cref="RenderEnding.Fade"/>.
    /// </summary>
    /// <remarks>
    /// A FADE LONGER THAN THE FILE IS CLAMPED to the whole file rather than refused - the whole
    /// file then fades - and the render says so in
    /// <see cref="MusicRenderResult.Diagnostics"/> and in
    /// <see cref="MusicRenderResult.FadeLength"/>, which reports the length actually applied.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan FadeLength
    {
        get => fadeLength;
        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A fade is a non-negative length of time.");
            }

            fadeLength = value;
        }
    }

    /// <summary>
    /// The shape the fade follows. It is <see cref="MusicFade.DefaultCurve"/> unless it is set.
    /// </summary>
    public MusicFadeCurve FadeCurve { get; set; } = MusicFade.DefaultCurve;

    /// <summary>
    /// How long the notes still sounding at the end of the music are allowed to ring on for. It is
    /// <see cref="DefaultRingOut"/> unless it is set, and the render stops as soon as nothing is
    /// sounding, so a file is only this much longer than its music when something really is still
    /// ringing.
    /// </summary>
    /// <remarks>
    /// It applies to <see cref="RenderEnding.NaturalStop"/>, and to the case where the music runs
    /// out before a target length is reached. An exact-length ending never rings on: the file is
    /// the length that was asked for.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan RingOut
    {
        get => ringOut;
        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A ring-out is a non-negative length of time.");
            }

            ringOut = value;
        }
    }

    /// <summary>
    /// How the samples are stored in the file, or null - the default - for the format's own
    /// default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>.wav</c> IS 32-BIT FLOAT unless this says otherwise, which is what an offline render
    /// produces and what nothing quantises on the way to disk; 16 gets the 16-bit PCM that every
    /// other program expects of a <c>.wav</c>. An <c>.aiff</c> is 16-bit PCM by default and will
    /// not take float at all.
    /// </para>
    /// <para>
    /// A FORMAT THAT STORES COMPRESSED AUDIO IGNORES IT. An Opus file has no bit depth, so a
    /// render "to whatever extension the user picked" keeps working when the extension turns out
    /// to be <c>.opus</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is set and is not 8, 16, 24 or 32.</exception>
    public int? BitsPerSample
    {
        get => bitsPerSample;
        set
        {
            if (value.HasValue && value.Value != 8 && value.Value != 16 && value.Value != 24 &&
                value.Value != 32)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Audio is stored as 8, 16, 24 or 32 bits a sample, or null for the file " +
                    "format's own default. 32 means IEEE float.");
            }

            bitsPerSample = value;
        }
    }

    /// <summary>Copies the options, so a render cannot be changed underneath itself.</summary>
    /// <returns>The copy.</returns>
    public MusicRenderOptions Clone() =>
        new MusicRenderOptions
        {
            targetLength = targetLength,
            fadeLength = fadeLength,
            ringOut = ringOut,
            bitsPerSample = bitsPerSample,
            Ending = Ending,
            FadeCurve = FadeCurve
        };

    /// <summary>
    /// The ending this render really uses: what <see cref="Ending"/> says, or the one that fits
    /// when it says nothing.
    /// </summary>
    /// <returns>The ending.</returns>
    internal RenderEnding EndingOrDefault() =>
        Ending ?? (targetLength.HasValue ? RenderEnding.Fade : RenderEnding.NaturalStop);
}
