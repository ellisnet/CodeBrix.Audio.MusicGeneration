namespace CodeBrix.Audio.MusicGeneration.Rendering;

/// <summary>
/// What a render is doing at the moment it reported its progress.
/// </summary>
/// <remarks>
/// GENERATION IS THE TIME COST, not rendering. With a model behind the music a render spends
/// almost all of its time in <see cref="Generating"/>; with the embedded replay it is never there
/// for long, because a replay writes its music as fast as it is asked for it.
/// </remarks>
public enum MusicRenderStage
{
    /// <summary>
    /// Waiting for the generator to write more music. Nothing is being rendered, because there is
    /// nothing yet to render.
    /// </summary>
    Generating = 0,

    /// <summary>Turning music into audio and writing it to the file.</summary>
    Rendering = 1,

    /// <summary>
    /// Finishing the file: the last of the audio has been written and the format is being closed
    /// off, which for a WAV or an AIFF means going back to patch the header.
    /// </summary>
    Writing = 2,

    /// <summary>The file is written. It is the last thing a render reports.</summary>
    Finished = 3
}
