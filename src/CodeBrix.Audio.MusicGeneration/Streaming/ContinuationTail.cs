using System.Collections.Generic;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// Builds the tail of the music so far: the last few bars, as MIDI, for a generator to see what it
/// is carrying on from.
/// </summary>
/// <remarks>
/// <para>
/// OVERLAPPING THE PROMPT IS THE BIGGEST SEAM LEVER THERE IS. A model asked to continue WITH the
/// previous bars in view keeps the key, the metre, the idiom and the material; one asked to start
/// again with only a description of the state does not. The highest-rated piece of the whole
/// audition was a continuation generated this way.
/// </para>
/// <para>
/// THE TAIL'S TICKS ARE THE TAIL'S OWN, counted from its first bar, exactly as
/// <see cref="Generation.MusicContinuation.Tail"/> documents. A NOTE IS ONE EVENT CARRYING ITS
/// LENGTH, the way everything in this library travels, and a note is REBUILT at its new tick rather
/// than moved - moving a note-on leaves its note-off behind and silently changes the length.
/// </para>
/// </remarks>
internal static class ContinuationTail
{
    /// <summary>Builds the tail that ends at a tick.</summary>
    /// <param name="played">Everything committed so far, in tick order.</param>
    /// <param name="endTick">The tick the tail ends at, which is where the next segment starts.</param>
    /// <param name="tailTicks">How many ticks of music to take.</param>
    /// <param name="ticksPerQuarterNote">The resolution the ticks are counted at.</param>
    /// <returns>The tail, or null when there is no music in it.</returns>
    public static MidiEventCollection Build(IReadOnlyList<MidiEvent> played, long endTick,
        long tailTicks, int ticksPerQuarterNote)
    {
        if (played == null || played.Count == 0 || tailTicks < 1L)
        {
            return null;
        }

        var fromTick = endTick - tailTicks;

        if (fromTick < 0L)
        {
            fromTick = 0L;
        }

        var tail = new MidiEventCollection(1, ticksPerQuarterNote);
        var track = tail.AddTrack();

        for (var i = 0; i < played.Count; i++)
        {
            var midiEvent = played[i];

            if (midiEvent == null || midiEvent.AbsoluteTime < fromTick ||
                midiEvent.AbsoluteTime >= endTick || IsCarriedHorizon(midiEvent))
            {
                continue;
            }

            track.Add(Rebase(midiEvent, midiEvent.AbsoluteTime - fromTick));
        }

        return track.Count == 0 ? null : tail;
    }

    /// <summary>The tick of the last real music there is, or -1 when there is none.</summary>
    /// <param name="played">Everything committed so far, in tick order.</param>
    /// <returns>The tick, ignoring the carried horizon, which is not music.</returns>
    /// <remarks>
    /// THE TAIL IS MUSIC, NOT SILENCE. When the engine has HELD MUSIC BACK, the bars immediately
    /// before the next segment are the REST it inserted; a tail taken from there would describe a
    /// silence to a generator and ask it to carry on from nothing.
    /// </remarks>
    public static long LastMusicTick(IReadOnlyList<MidiEvent> played)
    {
        if (played == null)
        {
            return -1L;
        }

        for (var i = played.Count - 1; i >= 0; i--)
        {
            var midiEvent = played[i];

            if (midiEvent != null && !IsCarriedHorizon(midiEvent))
            {
                return midiEvent.AbsoluteTime;
            }
        }

        return -1L;
    }

    private static bool IsCarriedHorizon(MidiEvent midiEvent) =>
        midiEvent is TextEvent text && text.MetaEventType == MetaEventType.TextEvent &&
        text.Text.Length == 0;

    // A NOTE IS REBUILT RATHER THAN MOVED - MusicEventPlacement says why.
    private static MidiEvent Rebase(MidiEvent source, long tick) =>
        MusicEventPlacement.At(source, tick);
}
