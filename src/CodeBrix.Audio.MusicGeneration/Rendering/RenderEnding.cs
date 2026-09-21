namespace CodeBrix.Audio.MusicGeneration.Rendering;

/// <summary>
/// How a rendered file ends.
/// </summary>
/// <remarks>
/// NEITHER MODEL PLANS A LENGTH - they write until they stop - but musical time is known as the
/// music is generated, so a render generates until the music passes the target and then ends the
/// file one of these three ways.
/// </remarks>
public enum RenderEnding
{
    /// <summary>
    /// The file is EXACTLY the target length and the last few seconds of it fade to silence. How
    /// long the fade is and what shape it follows are
    /// <see cref="MusicRenderOptions.FadeLength"/> and <see cref="MusicRenderOptions.FadeCurve"/>;
    /// with nothing said they are <see cref="MusicFade.DefaultLength"/> and
    /// <see cref="MusicFade.DefaultCurve"/>. It needs a target length.
    /// </summary>
    Fade = 0,

    /// <summary>
    /// The file is EXACTLY the target length and stops at that sample, mid-note if that is where
    /// the music is. It is for a consumer applying a fade, a crossfade or an edit of their own. It
    /// needs a target length.
    /// </summary>
    HardCut = 1,

    /// <summary>
    /// The generator's OWN ending, plus the ring-out of the notes still sounding at it. The length
    /// is whatever it turns out to be, which is why it is the only ending that makes sense with no
    /// target length - and it is what a render with nothing said at all does.
    /// </summary>
    /// <remarks>
    /// IT IS ONE PASS OF THE GENERATOR. A generator that would carry on for ever - the embedded
    /// replay loops, and a model continues - is asked once and rendered to the end of what it
    /// writes, so a render with this ending always finishes. A target length is not used.
    /// </remarks>
    NaturalStop = 2
}
