using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>One thing a replay generator releases, and when in the music it belongs.</summary>
internal readonly struct ReplayItem
{
    /// <summary>Creates an item.</summary>
    /// <param name="tick">Where in the segment it belongs.</param>
    /// <param name="midiEvent">The event, or null for a settled tick on its own.</param>
    /// <param name="settledThroughTick">The settled tick it carries.</param>
    public ReplayItem(long tick, MidiEvent midiEvent, long settledThroughTick)
    {
        Tick = tick;
        Event = midiEvent;
        SettledThroughTick = settledThroughTick;
    }

    /// <summary>Where in the segment the item belongs, in ticks from its start.</summary>
    public long Tick { get; }

    /// <summary>The event, or null when the item carries a settled tick on its own.</summary>
    public MidiEvent Event { get; }

    /// <summary>The tick through which output has settled once this item has been released.</summary>
    public long SettledThroughTick { get; }

    /// <summary>
    /// Builds what the caller sees, cloning the event so that one pass can never be changed by
    /// what a caller does with the pass before it.
    /// </summary>
    /// <returns>The item to yield.</returns>
    public GeneratedMusicEvent ToGeneratedEvent() =>
        Event == null
            ? GeneratedMusicEvent.SettledThrough(SettledThroughTick)
            : GeneratedMusicEvent.FromMidiEvent(Event.Clone(), SettledThroughTick);
}
