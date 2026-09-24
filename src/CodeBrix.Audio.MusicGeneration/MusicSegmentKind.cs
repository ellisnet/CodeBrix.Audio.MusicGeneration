namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// What kind of segment a generation is: how it was asked for, and so what a listener should
/// expect to hear where it joins the music before it.
/// </summary>
public enum MusicSegmentKind
{
    /// <summary>The first piece of the music, asked for with the application's own request.</summary>
    FirstPiece = 0,

    /// <summary>
    /// A segment asked for at the end of a pass WITH THE MUSIC SO FAR IN VIEW, so the generator
    /// carries on from where it was. See <see cref="SegmentPriming.Primed"/>.
    /// </summary>
    Primed = 1,

    /// <summary>
    /// A segment asked for at the end of a pass as a FRESH PIECE, with nothing of the music so far
    /// in view. See <see cref="SegmentPriming.Fresh"/>.
    /// </summary>
    Fresh = 2,

    /// <summary>A follow-up prompt the application asked for while the music played.</summary>
    FollowUp = 3
}
