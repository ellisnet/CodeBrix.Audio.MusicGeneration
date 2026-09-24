using System;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// One event of the outgoing piece's last moments, with the time it falls at - and, for a note,
/// the time its note-off falls at - measured from the start of the fade.
/// </summary>
internal readonly struct TimedTailEvent
{
    /// <summary>Times one event.</summary>
    /// <param name="midiEvent">The event.</param>
    /// <param name="at">When it falls, from the start of the fade.</param>
    /// <param name="offAt">When its note-off falls, for a note; otherwise the same as <paramref name="at"/>.</param>
    public TimedTailEvent(MidiEvent midiEvent, TimeSpan at, TimeSpan offAt)
    {
        Event = midiEvent;
        At = at;
        OffAt = offAt;
    }

    /// <summary>The event.</summary>
    public MidiEvent Event { get; }

    /// <summary>When it falls, from the start of the fade.</summary>
    public TimeSpan At { get; }

    /// <summary>When its note-off falls, for a note.</summary>
    public TimeSpan OffAt { get; }
}
