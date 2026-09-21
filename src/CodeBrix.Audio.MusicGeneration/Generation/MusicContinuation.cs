using System.Collections.Generic;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// What the music has been doing, handed to a generator so that the next segment carries on from
/// it rather than starting from nothing.
/// </summary>
/// <remarks>
/// <para>
/// A continuation is the difference between a join a listener notices and one they do not. The
/// tail lets a generator see the music it is following - the biggest lever there is - and the
/// carried state stops a new segment resetting the tempo, the metre, the key or the instruments
/// that were already chosen.
/// </para>
/// <para>
/// THE TICKS INSIDE THE TAIL are the tail's own, measured from the start of the tail. The segment
/// a generator writes in reply starts at tick 0 in its turn; placing segments one after another on
/// a timeline is the caller's job.
/// </para>
/// </remarks>
public sealed class MusicContinuation
{
    private readonly Dictionary<int, GeneralMidiProgram> channelPrograms =
        new Dictionary<int, GeneralMidiProgram>();

    /// <summary>
    /// The tail of the music so far, as MIDI - the last few bars, not the whole piece. Null when
    /// the tail is only available as text, or not at all.
    /// </summary>
    public MidiEventCollection Tail { get; set; }

    /// <summary>
    /// How long the tail is, in the tail's own ticks: the segment the generator writes begins
    /// exactly at this tick of the tail. Zero means the tail's length is not stated, and a
    /// generator then has to take it as ending at its last event.
    /// </summary>
    /// <remarks>
    /// IT IS NOT THE TICK OF THE TAIL'S LAST EVENT, and that is why it is here. The music the tail
    /// describes usually stops part-way through its last bar, and the next segment starts at the
    /// BAR LINE after it - so a generator that read the tail's last event as its end would place
    /// every continuation a beat or two early. A generator needs the two numbers separately.
    /// </remarks>
    public long TailTicks { get; set; }

    /// <summary>
    /// The tail of the music so far in the generator's own notation, for a generator that reads
    /// text rather than events. Null when there is none.
    /// </summary>
    public string ModelNativeTail { get; set; }

    /// <summary>The tempo in force at the end of the tail, in quarter notes per minute.</summary>
    public double? BeatsPerMinute { get; set; }

    /// <summary>The metre in force at the end of the tail.</summary>
    public MusicMeter? Meter { get; set; }

    /// <summary>The tonic in force at the end of the tail, written as in <see cref="MusicIntent.Key"/>.</summary>
    public string Key { get; set; }

    /// <summary>The mode in force at the end of the tail.</summary>
    public MusicMode? Mode { get; set; }

    /// <summary>
    /// The General MIDI program each channel was left on, keyed by channel number 1 to 16 - so a
    /// new segment does not silently re-voice a part.
    /// </summary>
    public IDictionary<int, GeneralMidiProgram> ChannelPrograms => channelPrograms;

    /// <summary>Copies the continuation, sharing no collection with the original.</summary>
    /// <returns>A copy. The MIDI tail itself is shared, because it is not modified.</returns>
    public MusicContinuation Clone()
    {
        var copy = new MusicContinuation
        {
            Tail = Tail,
            TailTicks = TailTicks,
            ModelNativeTail = ModelNativeTail,
            BeatsPerMinute = BeatsPerMinute,
            Meter = Meter,
            Key = Key,
            Mode = Mode
        };

        foreach (var pair in channelPrograms)
        {
            copy.channelPrograms[pair.Key] = pair.Value;
        }

        return copy;
    }
}
