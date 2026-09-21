using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Abc;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>
/// Turns a piece of music - MIDI or ABC - into the ordered, tick-resolved, honestly-settled form a
/// replay generator releases.
/// </summary>
/// <remarks>
/// <para>
/// A NOTE TRAVELS AS ONE EVENT. The note-offs a MIDI file carries are dropped, because a
/// <see cref="NoteOnEvent"/> carries its own length and the timeline schedules the note-off from
/// it. Anything else - programs, controllers, pitch wheel, tempo, metre, key, text, sysex - is
/// carried across untouched apart from its tick.
/// </para>
/// <para>
/// THE SETTLED TICK IS HONEST. Events that share a tick are released together, and only the last
/// of them says that tick has settled: claiming otherwise would tell a caller that a chord was
/// finished when half of it was still to come.
/// </para>
/// </remarks>
internal static class ReplayPieceBuilder
{
    /// <summary>Prepares a piece read from the bytes of a MIDI file.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="ticksPerQuarterNote">The resolution to produce ticks at.</param>
    /// <param name="what">What the music is, for an error message.</param>
    /// <returns>The prepared piece.</returns>
    /// <exception cref="MusicGenerationException">The bytes are not a MIDI file this can read.</exception>
    public static ReplayPiece FromMidiBytes(byte[] bytes, int ticksPerQuarterNote, string what)
    {
        MidiFile file;

        try
        {
            using var stream = new MemoryStream(bytes, false);
            file = new MidiFile(stream, MidiReadMode.Tolerant);
        }
        catch (Exception exception) when (exception is not MusicGenerationException)
        {
            throw new MusicGenerationException(
                $"{what} could not be read as a MIDI file: {exception.Message}", exception);
        }

        return FromMidiEvents(file.Events, ticksPerQuarterNote, what);
    }

    /// <summary>Prepares a piece from a MIDI event collection.</summary>
    /// <param name="source">The collection to replay.</param>
    /// <param name="ticksPerQuarterNote">The resolution to produce ticks at.</param>
    /// <param name="what">What the music is, for an error message.</param>
    /// <returns>The prepared piece.</returns>
    /// <exception cref="MusicGenerationException">
    /// The collection uses a timing this cannot read, or holds no music.
    /// </exception>
    public static ReplayPiece FromMidiEvents(MidiEventCollection source, int ticksPerQuarterNote,
        string what)
    {
        var sourceTicks = source.DeltaTicksPerQuarterNote;

        if (sourceTicks < 1)
        {
            throw new MusicGenerationException(
                $"{what} is timed in SMPTE frames rather than in ticks per quarter note, which a " +
                "replay generator does not read. Convert it to a metrical MIDI file first.");
        }

        var entries = new List<Entry>();
        var tempoEvents = new List<TempoEvent>();
        var meterChanges = new List<MeterChange>();
        var order = 0;
        var musicEndTick = 0L;

        for (var track = 0; track < source.Tracks; track++)
        {
            foreach (var sourceEvent in source.GetTrackEvents(track))
            {
                if (sourceEvent == null || MidiEvent.IsNoteOff(sourceEvent) ||
                    MidiEvent.IsEndTrack(sourceEvent))
                {
                    continue;
                }

                var tick = Scale(sourceEvent.AbsoluteTime, sourceTicks, ticksPerQuarterNote);
                var copy = Rescale(sourceEvent, tick, sourceTicks, ticksPerQuarterNote);

                var endTick = tick;
                if (copy is NoteOnEvent note && note.OffEvent != null)
                {
                    endTick = tick + note.NoteLength;
                }

                if (endTick > musicEndTick)
                {
                    musicEndTick = endTick;
                }

                if (copy is TempoEvent tempo)
                {
                    tempoEvents.Add(tempo);
                }

                if (copy is TimeSignatureEvent timeSignature)
                {
                    var meter = MeterOf(timeSignature);
                    if (meter.HasValue)
                    {
                        meterChanges.Add(new MeterChange(tick, meter.Value));
                    }
                }

                entries.Add(new Entry(tick, RankOf(copy), order++, copy));
            }
        }

        if (entries.Count == 0)
        {
            throw new MusicGenerationException(
                $"{what} holds no music: there is nothing for a replay generator to play.");
        }

        entries.Sort(CompareEntries);
        meterChanges.Sort(static (left, right) => left.Tick.CompareTo(right.Tick));
        tempoEvents.Sort(static (left, right) => left.AbsoluteTime.CompareTo(right.AbsoluteTime));

        var totalTicks = BarGrid.RoundUpToBar(meterChanges, ticksPerQuarterNote, musicEndTick);
        var items = BuildItems(entries, totalTicks);
        var tempoMap = BuildTempoMap(tempoEvents, ticksPerQuarterNote);

        return new ReplayPiece(ticksPerQuarterNote, totalTicks, items, tempoMap);
    }

    /// <summary>Prepares a piece written in ABC notation.</summary>
    /// <param name="abcText">The ABC text. The first tune in it is the one that is played.</param>
    /// <param name="ticksPerQuarterNote">The resolution to produce ticks at.</param>
    /// <param name="what">What the music is, for an error message.</param>
    /// <returns>The prepared piece.</returns>
    /// <exception cref="MusicGenerationException">The text holds no tune this can play.</exception>
    public static ReplayPiece FromAbcText(string abcText, int ticksPerQuarterNote, string what)
    {
        AbcTuneBook book;

        try
        {
            book = AbcReader.Parse(abcText);
        }
        catch (Exception exception) when (exception is not MusicGenerationException)
        {
            throw new MusicGenerationException(
                $"{what} could not be read as ABC notation: {exception.Message}", exception);
        }

        if (book.Tunes.Count == 0)
        {
            throw new MusicGenerationException(
                $"{what} holds no ABC tune. An ABC tune needs at least an X: reference number, a K: " +
                "key line, and some music under it.");
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
                $"{what} could not be turned into music: {exception.Message}", exception);
        }

        return FromMidiEvents(converted, ticksPerQuarterNote, what);
    }

    private static ReplayItem[] BuildItems(List<Entry> entries, long totalTicks)
    {
        var items = new List<ReplayItem>(entries.Count + 1);
        var settledSoFar = GeneratedMusicEvent.NothingSettled;

        for (var i = 0; i < entries.Count; i++)
        {
            var tick = entries[i].Tick;
            var lastOfTick = i == entries.Count - 1 || entries[i + 1].Tick > tick;
            var settled = lastOfTick ? tick : settledSoFar;

            items.Add(new ReplayItem(tick, entries[i].Event, settled));

            if (lastOfTick)
            {
                settledSoFar = tick;
            }
        }

        if (totalTicks > settledSoFar)
        {
            // The pass runs to a whole bar, and the silence at the end of it is settled music too -
            // so the pass ends by saying so, with nothing to play.
            items.Add(new ReplayItem(totalTicks, null, totalTicks));
        }

        return items.ToArray();
    }

    private static MidiTempoMap BuildTempoMap(List<TempoEvent> tempoEvents, int ticksPerQuarterNote)
    {
        var changes = new List<MidiTempoChange>();
        var beatsPerMinute = MidiTempoMap.DefaultBeatsPerMinute;
        var seconds = 0.0;
        var previousTick = 0L;

        foreach (var tempo in tempoEvents)
        {
            var beats = (double)(tempo.AbsoluteTime - previousTick) / ticksPerQuarterNote;
            seconds += beats * 60.0 / beatsPerMinute;
            previousTick = tempo.AbsoluteTime;
            beatsPerMinute = tempo.Tempo;

            changes.Add(new MidiTempoChange(TimeSpan.FromSeconds(seconds),
                (double)tempo.AbsoluteTime / ticksPerQuarterNote, beatsPerMinute));
        }

        return new MidiTempoMap(changes);
    }

    private static MusicMeter? MeterOf(TimeSignatureEvent timeSignature)
    {
        // A MIDI time signature writes its denominator as a power of two, so 4/4 is numerator 4 and
        // denominator 2. Anything outside a sane range is left alone rather than guessed at.
        if (timeSignature.Numerator < 1 || timeSignature.Denominator < 0 ||
            timeSignature.Denominator > 10)
        {
            return null;
        }

        return new MusicMeter(timeSignature.Numerator, 1 << timeSignature.Denominator);
    }

    private static MidiEvent Rescale(MidiEvent sourceEvent, long tick, int sourceTicks,
        int targetTicks)
    {
        if (sourceEvent is NoteOnEvent note)
        {
            var length = note.OffEvent == null ? 0L : note.NoteLength;
            var scaledLength = length == 0L
                ? 0L
                : Scale(sourceEvent.AbsoluteTime + length, sourceTicks, targetTicks) - tick;

            if (length > 0L && scaledLength < 1L)
            {
                // A note that is real in the source stays real here, however coarse the resolution.
                scaledLength = 1L;
            }

            if (scaledLength > int.MaxValue)
            {
                scaledLength = int.MaxValue;
            }

            return new NoteOnEvent(tick, note.Channel, note.NoteNumber, note.Velocity,
                (int)scaledLength);
        }

        var copy = sourceEvent.Clone();
        copy.AbsoluteTime = tick;

        return copy;
    }

    private static long Scale(long tick, int sourceTicks, int targetTicks) =>
        sourceTicks == targetTicks
            ? tick
            : (long)Math.Round((double)tick * targetTicks / sourceTicks, MidpointRounding.AwayFromZero);

    private static int RankOf(MidiEvent midiEvent)
    {
        if (midiEvent.CommandCode == MidiCommandCode.MetaEvent)
        {
            // Tempo, metre, key and the rest describe the bar they open, so they go first.
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
