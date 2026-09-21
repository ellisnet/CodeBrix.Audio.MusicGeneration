namespace CodeBrix.Audio.MusicGeneration.Rendering;

/// <summary>
/// The shape a fade-out follows, from full level to silence.
/// </summary>
/// <remarks>
/// They differ most in the middle. Half way through a fade, the eased-decibel curve is already
/// thirty decibels down, the equal-power curve is three decibels down and the straight line is six
/// - which is why a straight line sounds as though it hangs and then drops.
/// </remarks>
public enum MusicFadeCurve
{
    /// <summary>
    /// The level falls evenly IN DECIBELS, eased at both ends so that it neither lurches at the
    /// start nor lands abruptly at the end. It is the default, and it is the one that sounds like
    /// a fade rather than like a volume knob being turned.
    /// </summary>
    EasedDecibels = 0,

    /// <summary>
    /// The AMPLITUDE falls in a straight line. It is what a naive fade does, it is here because a
    /// consumer may want exactly that, and it is the shape the eased-decibel default exists to
    /// improve on.
    /// </summary>
    StraightLine = 1,

    /// <summary>
    /// The POWER falls in a straight line - a quarter-cycle of a cosine. It is the shape used for
    /// crossfades, where the two halves have to sum to constant power, and it holds its level
    /// longer than either of the others.
    /// </summary>
    EqualPower = 2
}
