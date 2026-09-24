namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// What happens to the tempo when a FRESH piece joins the music: whether it plays at the tempo
/// it was written at, or at the tempo the music has been keeping - the SESSION TEMPO.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT IS HERE: every fresh piece is a new piece, and a model picks a new tempo for each one, so
/// music made of fresh segments lurches from one pulse to another at every seam - 121, then 192,
/// then 128 beats per minute. A primed segment already carries the tempo across; this does the
/// same for fresh ones.
/// </para>
/// <para>
/// THE SESSION TEMPO is <see cref="MusicGenerationOptions.SessionBeatsPerMinute"/> when it is set,
/// otherwise the request's <see cref="Generation.MusicIntent.BeatsPerMinute"/> when that is set,
/// and otherwise the opening tempo of the first piece. <see cref="MusicDiagnostics.SessionTempo"/>
/// reports it.
/// </para>
/// <para>
/// ONLY FRESH PIECES ARE AFFECTED (and the first piece, when a session tempo is given up front).
/// Primed segments carry the tempo already, and a follow-up prompt is a new request the
/// application asked for, so both keep what they were written with.
/// </para>
/// </remarks>
public enum SessionTempoPolicy
{
    /// <summary>
    /// Every piece plays at the tempo it was written at. The default, and what the engine has
    /// always done.
    /// </summary>
    Adopt = 0,

    /// <summary>
    /// Every fresh piece plays at the session tempo: its own tempo events are dropped - later tempo
    /// changes inside it included, so a carried piece has ONE tempo - and the session tempo is
    /// stated where it starts. Its notes keep their ticks, so it is heard a little faster or slower
    /// than the model wrote it, never rearranged.
    /// </summary>
    Carry = 1,

    /// <summary>
    /// A fresh piece whose opening tempo is within <see cref="MusicGenerationOptions.TempoBand"/>
    /// of the session tempo plays at its own tempo - a little natural variation - and one outside
    /// the band is carried, exactly as <see cref="Carry"/> carries it. The session tempo itself
    /// never drifts: every piece is measured against the same one.
    /// </summary>
    CarryOutsideBand = 2
}
