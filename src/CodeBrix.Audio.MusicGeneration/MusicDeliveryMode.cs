namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// How music is reaching the timeline: as a continuous stream, or a whole segment at a time. The
/// engine chooses between them from its own measurements and reports which one it is in.
/// </summary>
public enum MusicDeliveryMode
{
    /// <summary>
    /// A continuous stream: music is committed to the timeline a short window ahead of the play
    /// head, and the head follows it without a break. This is what happens while the generator
    /// keeps up with real time.
    /// </summary>
    Streaming = 0,

    /// <summary>
    /// A whole segment at a time: nothing of a segment is heard until the whole of it has been
    /// generated, then all of it plays, and then there is a REST while the next one is generated.
    /// </summary>
    /// <remarks>
    /// It is what the engine does when the measured generation rate is below real time. A stream
    /// that ran out would stall wherever the buffer happened to empty - usually mid-phrase, which
    /// sounds broken - whereas a phrase followed by a pause sounds intentional.
    /// </remarks>
    SegmentAtATime = 1
}
