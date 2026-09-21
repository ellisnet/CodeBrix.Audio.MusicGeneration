using System;

namespace CodeBrix.Audio.MusicGeneration.Rendering;

/// <summary>
/// THE ONE PLACE THE FADE LIVES: how long a fade lasts unless a caller says otherwise, what shape
/// it follows, and the function that turns a position within a fade into a gain.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFAULTS BELOW WERE SETTLED BY EAR. The three curves were heard one after another on the
/// same ending and could not be told apart, so the default is the one that measures best and the
/// other two stay selectable. They are two lines, deliberately together and deliberately in a file
/// of their own, so that changing the fade every render in this library performs is changing
/// <see cref="DefaultLength"/> and <see cref="DefaultCurve"/> and nothing else. Both are consumer
/// options on every render
/// (<see cref="MusicRenderOptions.FadeLength"/>, <see cref="MusicRenderOptions.FadeCurve"/>), so
/// the default settles what a caller who says nothing gets - not what is possible.
/// </para>
/// <para>
/// THE CURVES, as formulas. <c>p</c> runs from 0 at the first frame of the fade to 1 at the last
/// frame of the file, and every curve gives exactly 1 at <c>p = 0</c> and exactly 0 at
/// <c>p = 1</c>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="MusicFadeCurve.StraightLine"/>: <c>gain = 1 - p</c>.
/// </description></item>
/// <item><description>
/// <see cref="MusicFadeCurve.EqualPower"/>: <c>gain = cos(p * PI / 2)</c>.
/// </description></item>
/// <item><description>
/// <see cref="MusicFadeCurve.EasedDecibels"/>: <c>e = p * p * (3 - 2p)</c> (the easing),
/// <c>floor = 10 ^ (FloorDecibels / 20)</c>, <c>gain = (floor ^ e - floor) / (1 - floor)</c>. The
/// level therefore falls evenly in decibels - it is <c>FloorDecibels * e</c> dB down at <c>p</c> -
/// with the easing flattening the slope at both ends, and the subtraction of the floor bringing it
/// to true silence at the last frame instead of stopping at the floor.
/// </description></item>
/// </list>
/// </remarks>
public static class MusicFade
{
    /// <summary>
    /// HOW LONG A FADE LASTS unless the caller says otherwise: six seconds, settled by ear.
    /// </summary>
    public static readonly TimeSpan DefaultLength = TimeSpan.FromSeconds(6.0);

    /// <summary>
    /// THE SHAPE A FADE FOLLOWS unless the caller says otherwise: the level falling evenly in
    /// decibels, eased at both ends. Settled by ear, and this is the one line to change it.
    /// </summary>
    public const MusicFadeCurve DefaultCurve = MusicFadeCurve.EasedDecibels;

    /// <summary>
    /// How far down <see cref="MusicFadeCurve.EasedDecibels"/> travels before the last of it is
    /// taken off to reach true silence: sixty decibels, which is below anything a listener can
    /// still hear under the music that came before it.
    /// </summary>
    public const double FloorDecibels = -60.0;

    private static readonly double Floor = Math.Pow(10.0, FloorDecibels / 20.0);

    /// <summary>The gain a fade is at, part of the way through it.</summary>
    /// <param name="curve">The shape the fade follows.</param>
    /// <param name="progress">
    /// How far through the fade it is: 0 at the first frame of the fade and 1 at the last frame of
    /// the file. Values outside that range are clamped into it.
    /// </param>
    /// <returns>The gain to multiply the audio by, from 1 down to 0.</returns>
    /// <remarks>
    /// IT IS EXACT AT BOTH ENDS: 1 at the start of the fade and 0 at the end of it, whatever the
    /// curve, so a faded file ends in silence rather than in a step down to nothing.
    /// </remarks>
    public static float GainAt(MusicFadeCurve curve, double progress)
    {
        if (!(progress > 0.0))
        {
            // Also catches NaN, which a caller can only reach with a zero-length fade.
            return 1.0F;
        }

        if (progress >= 1.0)
        {
            return 0.0F;
        }

        switch (curve)
        {
            case MusicFadeCurve.StraightLine:
                return (float)(1.0 - progress);

            case MusicFadeCurve.EqualPower:
                return (float)Math.Cos(progress * Math.PI / 2.0);

            default:
                var eased = progress * progress * (3.0 - (2.0 * progress));

                return (float)((Math.Pow(Floor, eased) - Floor) / (1.0 - Floor));
        }
    }

    /// <summary>How far down a fade is, in decibels, part of the way through it.</summary>
    /// <param name="curve">The shape the fade follows.</param>
    /// <param name="progress">How far through the fade it is, from 0 to 1.</param>
    /// <returns>
    /// The level in decibels relative to the music, which is 0 at the start of the fade and
    /// <see cref="double.NegativeInfinity"/> at the end of it.
    /// </returns>
    /// <remarks>
    /// It is <see cref="GainAt"/> expressed the way a listener hears it, and it is what makes
    /// "falls evenly in decibels" something a test can assert rather than something a comment
    /// claims.
    /// </remarks>
    public static double DecibelsAt(MusicFadeCurve curve, double progress)
    {
        var gain = GainAt(curve, progress);

        return gain <= 0.0F ? double.NegativeInfinity : 20.0 * Math.Log10(gain);
    }
}
