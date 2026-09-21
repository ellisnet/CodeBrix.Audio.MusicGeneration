using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// The one place that moves a MIDI event to another tick, because moving one has a sharp edge that
/// has to be got right everywhere: A NOTE IS REBUILT, NEVER MOVED.
/// </summary>
/// <remarks>
/// Setting <c>AbsoluteTime</c> on a <see cref="NoteOnEvent"/> leaves the note-off it carries where
/// it was, which silently changes the note's length - so a note is built again at its new tick with
/// the length it was written with, and everything else is cloned and re-timed.
/// </remarks>
internal static class MusicEventPlacement
{
    /// <summary>Builds the same event at another tick.</summary>
    /// <param name="source">The event to place.</param>
    /// <param name="tick">The tick to place it at.</param>
    /// <returns>A new event at that tick. The one passed in is never changed.</returns>
    public static MidiEvent At(MidiEvent source, long tick)
    {
        if (source is NoteOnEvent note)
        {
            var length = note.OffEvent == null ? 0 : note.NoteLength;

            return new NoteOnEvent(tick, note.Channel, note.NoteNumber, note.Velocity, length);
        }

        var copy = source.Clone();
        copy.AbsoluteTime = tick;

        return copy;
    }
}
