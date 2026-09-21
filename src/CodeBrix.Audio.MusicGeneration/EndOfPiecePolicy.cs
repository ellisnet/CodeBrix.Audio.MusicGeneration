namespace CodeBrix.Audio.MusicGeneration;

/// <summary>What happens when a generator reaches the end of the music it was writing.</summary>
/// <remarks>
/// The default is <see cref="KeepGenerating"/>, because the thing an application usually wants from
/// generated music is music that does not stop. A piece that has to end is asked for by name.
/// </remarks>
public enum EndOfPiecePolicy
{
    /// <summary>
    /// The music carries on: the same generator is asked for the next segment, with the music so
    /// far in view, and that segment is placed at the next bar line. For the embedded replay - which
    /// has one piece and no imagination - that is a loop.
    /// </summary>
    KeepGenerating = 0,

    /// <summary>
    /// The music ends. The timeline is completed when the generator's pass is over, and playback
    /// drains to the last event and ends the way a file does.
    /// </summary>
    Stop = 1
}
