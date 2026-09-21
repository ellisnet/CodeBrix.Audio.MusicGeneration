using System;
using System.Collections.Generic;
using CodeBrix.Audio.Abc;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// One run of the SHIPPED whole-tune path - regroup, read, convert - over a piece of the model's
/// text, flattened into the order a timeline is written in.
/// </summary>
/// <remarks>
/// <para>
/// IT IS THE WHOLE TUNE EVERY TIME, and that is the point: the reader and the converter that went
/// through a full test suite do the work, so ties, repeats, numbered endings and inline field
/// changes need no second implementation carrying cross-bar state by hand. What the incremental
/// path adds is knowing which events are NEW and how far the text has settled.
/// </para>
/// <para>
/// A NOTE TRAVELS AS ONE EVENT, exactly as the replay generators do it: the note-off the converter
/// writes beside each note is dropped, because a note-on carries its own length and the timeline
/// schedules the note-off from it. What is kept is the tempo, the metre, the key, a program change
/// and the notes; a title and a voice name are for a printed page and are not music.
/// </para>
/// <para>
/// WHERE EACH VOICE HAS GOT TO is here too, one end tick per voice, because it is what the settled
/// tick is made of: the next slice gives a voice its next bar AT ITS OWN END, so nothing beyond
/// the earliest of those ends can be promised.
/// </para>
/// </remarks>
internal sealed class MuPTTune
{
    private readonly List<MidiEvent> events;
    private readonly List<long> voiceEndTicks;
    private readonly List<long> voiceLastNoteTicks;
    private readonly List<MeterChange> meterChanges;

    private MuPTTune(List<MidiEvent> events, List<long> voiceEndTicks,
        List<long> voiceLastNoteTicks, List<MeterChange> meterChanges, long endTick,
        bool aVoiceIsHoldingATie)
    {
        this.events = events;
        this.voiceEndTicks = voiceEndTicks;
        this.voiceLastNoteTicks = voiceLastNoteTicks;
        this.meterChanges = meterChanges;
        EndTick = endTick;
        AVoiceIsHoldingATie = aVoiceIsHoldingATie;
    }

    /// <summary>The music, in tick order, ready to be placed on a timeline.</summary>
    public IReadOnlyList<MidiEvent> Events => events;

    /// <summary>How far each voice's music reaches, one entry per voice.</summary>
    public IReadOnlyList<long> VoiceEndTicks => voiceEndTicks;

    /// <summary>
    /// Where each voice's music STOPS BEING FINAL, one entry per voice: the start of the earliest
    /// note that is still sounding when the voice's last note begins.
    /// </summary>
    /// <remarks>
    /// It is the one event in a voice that more text can still change - a tie left open is played
    /// short and a later slice makes it long - together with anything sounding over it. In plain
    /// music it is simply the last note's own tick.
    /// </remarks>
    public IReadOnlyList<long> VoiceUnsettledFromTicks => voiceLastNoteTicks;

    /// <summary>Every metre in force, in tick order, so bar lines can be found.</summary>
    public IReadOnlyList<MeterChange> MeterChanges => meterChanges;

    /// <summary>Where the music ends: the furthest any voice reaches.</summary>
    public long EndTick { get; }

    /// <summary>Whether a voice is left holding a tie whose partner has not been written yet.</summary>
    public bool AVoiceIsHoldingATie { get; }

    /// <summary>Reads a piece of the model's text as a tune.</summary>
    /// <param name="modelText">The model's text: the prompt and whatever it has written.</param>
    /// <param name="ticksPerQuarterNote">The resolution to produce ticks at.</param>
    /// <returns>The tune, or null when the text holds no tune yet.</returns>
    /// <exception cref="MusicGenerationException">
    /// The text could not be read or converted at all.
    /// </exception>
    public static MuPTTune Read(string modelText, int ticksPerQuarterNote)
    {
        if (!MuPTSmtAbc.HeaderIsClosed(modelText))
        {
            // NOTHING IS PLAYABLE BEFORE THE K: LINE CLOSES THE HEADER: it is what fixes the
            // metre, the unit note length, the tempo and the key, and everything before it means
            // something else without them.
            return null;
        }

        var standard = MuPTSmtAbc.ToStandardAbc(modelText, null);
        AbcTuneBook book;

        try
        {
            book = AbcReader.Parse(standard);
        }
        catch (Exception exception) when (exception is not MusicGenerationException)
        {
            throw new MusicGenerationException(
                "The text the MuPT model wrote could not be read as ABC notation: " +
                exception.Message, exception);
        }

        if (book.Tunes.Count == 0)
        {
            return null;
        }

        MidiEventCollection converted;

        try
        {
            var options = new AbcToMidiOptions { TicksPerQuarterNote = ticksPerQuarterNote };
            converted = AbcToMidi.Convert(book.Tunes[0], options);
        }
        catch (Exception exception) when (exception is not MusicGenerationException)
        {
            throw new MusicGenerationException(
                "The music the MuPT model wrote could not be turned into MIDI: " +
                exception.Message, exception);
        }

        return Flatten(converted, MuPTSmtAbc.AVoiceIsHoldingATie(standard));
    }

    private static MuPTTune Flatten(MidiEventCollection converted, bool aVoiceIsHoldingATie)
    {
        var entries = new List<Entry>();
        var voiceEndTicks = new List<long>();
        var voiceLastNoteTicks = new List<long>();
        var meterChanges = new List<MeterChange>();
        var order = 0;
        var endTick = 0L;

        var noteStarts = new List<long>();
        var noteEnds = new List<long>();

        for (var track = 0; track < converted.Tracks; track++)
        {
            // TRACK 0 IS THE CONDUCTOR and every track after it is one voice, in the tune's own
            // voice order - which is how the converter writes a tune.
            var voiceEnd = 0L;

            foreach (var midiEvent in converted.GetTrackEvents(track))
            {
                if (midiEvent == null || !IsMusic(midiEvent))
                {
                    continue;
                }

                var at = midiEvent.AbsoluteTime;

                if (midiEvent is NoteOnEvent note && note.OffEvent != null)
                {
                    var noteEnd = at + note.NoteLength;

                    if (noteEnd > voiceEnd)
                    {
                        voiceEnd = noteEnd;
                    }

                    noteStarts.Add(at);
                    noteEnds.Add(noteEnd);
                }

                if (midiEvent is TimeSignatureEvent timeSignature)
                {
                    var meter = MeterOf(timeSignature);

                    if (meter.HasValue)
                    {
                        meterChanges.Add(new MeterChange(at, meter.Value));
                    }
                }

                entries.Add(new Entry(at, RankOf(midiEvent), order++, midiEvent));
            }

            if (track > 0)
            {
                voiceEndTicks.Add(voiceEnd);
                voiceLastNoteTicks.Add(UnsettledFrom(noteStarts, noteEnds));

                if (voiceEnd > endTick)
                {
                    endTick = voiceEnd;
                }
            }

            noteStarts.Clear();
            noteEnds.Clear();
        }

        entries.Sort(CompareEntries);
        meterChanges.Sort(static (left, right) => left.Tick.CompareTo(right.Tick));

        var events = new List<MidiEvent>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            events.Add(entries[i].Event);
        }

        return new MuPTTune(events, voiceEndTicks, voiceLastNoteTicks, meterChanges, endTick,
            aVoiceIsHoldingATie);
    }

    // WHERE A VOICE STOPS BEING FINAL. The note a tie can still lengthen is the LAST one its voice
    // emitted - a held note is flushed at the end of the walk and nothing but a DECORATION can be
    // written after it - and a decoration written there starts at the held note's own END. So the
    // answer is the earliest note that reaches the last note's tick, which takes the note BEFORE
    // it as well: a note that ends exactly where the next begins cannot be told apart from a note
    // the last one was written over, and one note of extra latency is the cheapest way to be sure.
    private static long UnsettledFrom(List<long> starts, List<long> ends)
    {
        if (starts.Count == 0)
        {
            return 0L;
        }

        var latest = 0L;

        for (var i = 0; i < starts.Count; i++)
        {
            if (starts[i] > latest)
            {
                latest = starts[i];
            }
        }

        var from = latest;

        for (var i = 0; i < starts.Count; i++)
        {
            if (ends[i] >= latest && starts[i] < from)
            {
                from = starts[i];
            }
        }

        return from;
    }

    // AN ALLOW-LIST, not a list of things to drop: the converter writes a known set, and anything
    // it might add later is not music until somebody says it is.
    private static bool IsMusic(MidiEvent midiEvent)
    {
        if (MidiEvent.IsNoteOff(midiEvent))
        {
            return false;
        }

        return midiEvent is NoteOnEvent || midiEvent is TempoEvent ||
               midiEvent is TimeSignatureEvent || midiEvent is KeySignatureEvent ||
               midiEvent is PatchChangeEvent;
    }

    private static MusicMeter? MeterOf(TimeSignatureEvent timeSignature)
    {
        if (timeSignature.Numerator < 1 || timeSignature.Denominator < 0 ||
            timeSignature.Denominator > 10)
        {
            return null;
        }

        return new MusicMeter(timeSignature.Numerator, 1 << timeSignature.Denominator);
    }

    // The same order the replay generators put a piece in: what describes a bar goes before what
    // sounds in it, and two events at one tick keep the order the tune wrote them in.
    private static int RankOf(MidiEvent midiEvent)
    {
        if (midiEvent.CommandCode == MidiCommandCode.MetaEvent)
        {
            return 0;
        }

        return midiEvent.CommandCode == MidiCommandCode.NoteOn ? 2 : 1;
    }

    private static int CompareEntries(Entry left, Entry right)
    {
        var byTick = left.Tick.CompareTo(right.Tick);

        if (byTick != 0)
        {
            return byTick;
        }

        var byRank = left.Rank.CompareTo(right.Rank);

        return byRank != 0 ? byRank : left.Order.CompareTo(right.Order);
    }

    private readonly struct Entry
    {
        public Entry(long tick, int rank, int order, MidiEvent midiEvent)
        {
            Tick = tick;
            Rank = rank;
            Order = order;
            Event = midiEvent;
        }

        public long Tick { get; }

        public int Rank { get; }

        public int Order { get; }

        public MidiEvent Event { get; }
    }
}
