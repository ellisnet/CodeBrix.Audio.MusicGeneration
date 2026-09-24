namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// How each segment the engine asks for at the end of a pass STARTS: with the music so far in view
/// (primed), or as a fresh piece. It only matters with <see cref="EndOfPiecePolicy.KeepGenerating"/>,
/// because that is the only time the engine asks the same generator for another segment.
/// </summary>
/// <remarks>
/// <para>
/// PRIMING IS THE BIGGEST SEAM LEVER THERE IS, and it is also what makes some models repeat
/// themselves. A generator that is shown the last bars of its own music carries on in the same key,
/// metre and idiom - and a model that is strongly led by what it is shown will write the same
/// material again, however the seed changes. A FRESH segment is a new piece on a new derived seed,
/// with the same character, joined at a bar line with the instruments and the tempo carried
/// across: real variety, at the cost of a join the listener can hear.
/// </para>
/// <para>
/// <see cref="Alternate"/> takes turns, which keeps some continuity while still moving the music
/// on. <see cref="MusicGenerationOptions.SeamCrossfade"/> softens the join at a fresh seam.
/// </para>
/// <para>
/// A GENERATOR THAT DOES NOT HONOUR <see cref="Generation.MusicRequestFeatures.Continuation"/>
/// always starts its segments fresh, whatever this says, because it has no way to be shown the
/// music so far.
/// </para>
/// </remarks>
public enum SegmentPriming
{
    /// <summary>
    /// Every segment is primed with the last bars of the music so far, so the generator carries on
    /// from where it was. It is the default, and it is what the engine has always done.
    /// </summary>
    Primed = 0,

    /// <summary>
    /// Every segment is a fresh piece: the application's own request again, on a new derived seed
    /// when it has a seed, with nothing of the music so far in view.
    /// </summary>
    Fresh = 1,

    /// <summary>
    /// The segments take turns, starting with a primed one: the second segment of the music is
    /// primed, the third fresh, the fourth primed, and so on. A follow-up prompt starts the count
    /// again, so its own first continuation is primed.
    /// </summary>
    Alternate = 2
}
