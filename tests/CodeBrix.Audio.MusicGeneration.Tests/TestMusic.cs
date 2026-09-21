using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Small, exactly known pieces of music, so a test about pacing or about ticks is not also a test
/// about whatever a model once wrote.
/// </summary>
public static class TestMusic
{
    /// <summary>A tune in ABC notation: one voice, four bars of 4/4 at 120 beats per minute.</summary>
    public const string SimpleAbc = "X:1\nT:Test Tune\nM:4/4\nL:1/4\nQ:1/4=120\nK:C\nCDEF|GABc|cBAG|FEDC|\n";

    /// <summary>Text that is not ABC notation at all.</summary>
    public const string NotAbc = "this is not a tune, it is a sentence";

    /// <summary>
    /// One bar of 4/4 at 120 beats per minute: four quarter notes, so the bar is exactly two
    /// seconds long and each note falls half a second after the one before it.
    /// </summary>
    /// <param name="ticksPerQuarterNote">The resolution to write it at.</param>
    /// <returns>The collection.</returns>
    public static MidiEventCollection OneBarOfQuarterNotes(int ticksPerQuarterNote)
    {
        var collection = new MidiEventCollection(1, ticksPerQuarterNote);
        collection.AddTrack();

        collection.AddEvent(new TempoEvent(500000, 0L), 0);
        collection.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);

        for (var beat = 0; beat < 4; beat++)
        {
            var noteOn = new NoteOnEvent(beat * (long)ticksPerQuarterNote, 1, 60 + beat, 100,
                ticksPerQuarterNote);
            collection.AddEvent(noteOn, 0);
            collection.AddEvent(noteOn.OffEvent, 0);
        }

        return collection;
    }

    /// <summary>
    /// Bars of 4/4 at 120 beats per minute, four quarter notes to the bar, so every bar is exactly
    /// two seconds long. The tempo and the metre are written once, at the start.
    /// </summary>
    /// <param name="barCount">How many bars.</param>
    /// <param name="ticksPerQuarterNote">The resolution to write it at.</param>
    /// <returns>The collection.</returns>
    public static MidiEventCollection Bars(int barCount, int ticksPerQuarterNote)
    {
        var collection = new MidiEventCollection(1, ticksPerQuarterNote);
        collection.AddTrack();

        collection.AddEvent(new TempoEvent(500000, 0L), 0);
        collection.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);

        for (var beat = 0; beat < barCount * 4; beat++)
        {
            var noteOn = new NoteOnEvent(beat * (long)ticksPerQuarterNote, 1, 60 + (beat % 12), 100,
                ticksPerQuarterNote);
            collection.AddEvent(noteOn, 0);
            collection.AddEvent(noteOn.OffEvent, 0);
        }

        return collection;
    }

    /// <summary>
    /// One bar of 4/4 with EVERYTHING a piece sets up at its start: a tempo, a metre, a key, a
    /// program change and two controllers. A second pass of it would say all of that again, which
    /// is exactly what a seam must not do.
    /// </summary>
    /// <param name="ticksPerQuarterNote">The resolution to write it at.</param>
    /// <returns>The collection.</returns>
    public static MidiEventCollection OneBarWithAFullSetup(int ticksPerQuarterNote)
    {
        var collection = new MidiEventCollection(1, ticksPerQuarterNote);
        collection.AddTrack();

        collection.AddEvent(new TempoEvent(500000, 0L), 0);
        collection.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);
        collection.AddEvent(new KeySignatureEvent(0, 0, 0L), 0);
        collection.AddEvent(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.Celesta), 0);
        collection.AddEvent(new ControlChangeEvent(0L, 1, MidiController.MainVolume, 90), 0);
        collection.AddEvent(new ControlChangeEvent(0L, 1, MidiController.Pan, 40), 0);

        for (var beat = 0; beat < 4; beat++)
        {
            var noteOn = new NoteOnEvent(beat * (long)ticksPerQuarterNote, 1, 60 + beat, 100,
                ticksPerQuarterNote);
            collection.AddEvent(noteOn, 0);
            collection.AddEvent(noteOn.OffEvent, 0);
        }

        return collection;
    }

    /// <summary>
    /// One long note, held for several bars, over a bar of 4/4 at 120 beats per minute. It is what
    /// proves that a note sounding at a seam keeps its length instead of being cut off there.
    /// </summary>
    /// <param name="bars">How many bars the note is held for.</param>
    /// <param name="ticksPerQuarterNote">The resolution to write it at.</param>
    /// <returns>The collection.</returns>
    public static MidiEventCollection OneLongNote(int bars, int ticksPerQuarterNote)
    {
        var collection = new MidiEventCollection(1, ticksPerQuarterNote);
        collection.AddTrack();

        collection.AddEvent(new TempoEvent(500000, 0L), 0);
        collection.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);

        var noteOn = new NoteOnEvent(0L, 1, 60, 100, bars * 4 * ticksPerQuarterNote);
        collection.AddEvent(noteOn, 0);
        collection.AddEvent(noteOn.OffEvent, 0);

        return collection;
    }

    /// <summary>A chord: three notes at one tick, which is what tests the settled tick honestly.</summary>
    /// <param name="ticksPerQuarterNote">The resolution to write it at.</param>
    /// <returns>The collection.</returns>
    public static MidiEventCollection OneChord(int ticksPerQuarterNote)
    {
        var collection = new MidiEventCollection(1, ticksPerQuarterNote);
        collection.AddTrack();

        collection.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);

        foreach (var note in new[] { 60, 64, 67 })
        {
            var noteOn = new NoteOnEvent(ticksPerQuarterNote, 1, note, 100, ticksPerQuarterNote);
            collection.AddEvent(noteOn, 0);
            collection.AddEvent(noteOn.OffEvent, 0);
        }

        return collection;
    }
}
