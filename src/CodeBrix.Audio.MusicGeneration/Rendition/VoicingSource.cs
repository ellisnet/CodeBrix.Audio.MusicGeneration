namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>Where a part's instrument came from. It is the answer to "why is this part this sound".</summary>
public enum VoicingSource
{
    /// <summary>The named rendition's own list of voices gave this part its instrument.</summary>
    Rendition,

    /// <summary>The music itself asked for it, with a program change on that channel.</summary>
    Music,

    /// <summary>The request's instrument hints asked for it.</summary>
    InstrumentHint,

    /// <summary>Nothing asked for anything, so the taste table filled the gap with a distinct voice.</summary>
    TasteTable,

    /// <summary>It is the percussion part, which is the instrument library's kit.</summary>
    Percussion
}
