using System;
using System.Collections.Generic;
using System.Text;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// Turning text into music WHILE IT IS STILL BEING WRITTEN, without ever changing anything that
/// has already been heard.
/// </summary>
/// <remarks>
/// <para>
/// THE STREAMING UNIT IS ONE COMPLETE SLICE - for a single-voice tune, one complete bar. Nothing
/// is playable before the <c>K:</c> line closes the header, because that is what fixes the metre,
/// the unit note length, the tempo and the key; after that, every time a slice separator arrives
/// the SHIPPED whole-tune path is run over ALL the text so far and only the NEW TAIL of events is
/// let out. Writing a second, incremental ABC parser was the alternative, and this way the reader
/// and converter that went through a full test suite carry the ties, the repeats, the numbered
/// endings and the inline field changes instead.
/// </para>
/// <para>
/// IT RESTS ON PREFIX STABILITY: later text never rewrites earlier events. THE ONE KNOWN
/// EXCEPTION IS A TIE. The converter holds a tied note until the note it joins to arrives and, if
/// the text ends first, plays it SHORT on its own - so a prefix ending <c>A2-|</c> yields a short
/// note that the next slice turns into one long note.
/// </para>
/// <para>
/// SO THE SETTLED TICK IS THE EARLIEST LAST NOTE. Two things a later slice can do, and one number
/// that covers both: it can give a voice another bar, which begins AT THAT VOICE'S OWN END, and it
/// can LENGTHEN that voice's last note, which is the only note a tie can still be holding open. A
/// voice's last note starts at or before its end, so the settled tick is one tick before the
/// EARLIEST, over the voices, of the start of the voice's last note - and a note that may still
/// grow is never let out until it cannot. It also covers a tie the text cannot show: a repeat with
/// a first ending and no second one ends its second pass part-way through the section, holding
/// whatever that bar left open.
/// </para>
/// <para>
/// A VOICE THAT STOPS BEING WRITTEN therefore holds the settled tick where it is until the pass
/// ends - the model gives a slice's bars to the voices in order, so a part that drops out has its
/// next bar placed where its own music ended, which may be far behind everything else. It costs
/// latency and never correctness, and on this model a whole pass is a second or two of work.
/// </para>
/// <para>
/// THE LAST WORD IS THE WHOLE PASS. When the model stops, the text is converted once more - the
/// slice it was part-way through included, because nothing more is coming - and everything left is
/// let out at once, with a final settled tick rounded up to a BAR LINE so that the segment after
/// it starts against one.
/// </para>
/// <para>
/// A CONTINUATION IS PROMPTED WITH THE MUSIC IT FOLLOWS, so the prompt's own events must not be
/// played again: they are counted at the start and skipped, and the segment's tick 0 is the end of
/// the prompt's music. A voice that had stopped being written lands its next bar BEFORE that
/// point; those events are moved to the seam rather than dropped, because the music they belong to
/// has not been heard.
/// </para>
/// </remarks>
internal sealed class MuPTSlicePath
{
    private readonly StringBuilder raw;
    private readonly int ticksPerQuarterNote;
    private readonly List<MidiEvent> released = new List<MidiEvent>();

    private int convertedThrough = -1;
    private int promptEventCount;
    private long originTick;
    private long settledThroughTick = GeneratedMusicEvent.NothingSettled;
    private bool finished;

    /// <summary>Starts a pass over one prompt.</summary>
    /// <param name="promptText">
    /// The prompt the model was given, in the model's own form. Its music is part of the piece
    /// unless <paramref name="promptIsAlreadyHeard"/> says otherwise.
    /// </param>
    /// <param name="ticksPerQuarterNote">The resolution the segment's ticks are at.</param>
    /// <param name="promptIsAlreadyHeard">
    /// True when the prompt is the tail of music that has already been played - a continuation -
    /// so that none of it is played again.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="promptText"/> is null.</exception>
    /// <exception cref="MusicGenerationException">The prompt could not be read.</exception>
    public MuPTSlicePath(string promptText, int ticksPerQuarterNote, bool promptIsAlreadyHeard)
    {
        if (promptText == null)
        {
            throw new ArgumentNullException(nameof(promptText));
        }

        raw = new StringBuilder(promptText);
        this.ticksPerQuarterNote = ticksPerQuarterNote;

        if (!promptIsAlreadyHeard)
        {
            return;
        }

        var prefix = MuPTSmtAbc.CompleteSlicePrefix(promptText);
        var tune = prefix == null ? null : MuPTTune.Read(prefix, ticksPerQuarterNote);

        if (tune == null)
        {
            return;
        }

        // WHERE THE NEW MUSIC BEGINS is where the music it follows ends, and everything the prompt
        // already accounted for is not this segment's to play.
        promptEventCount = tune.Events.Count;
        originTick = tune.EndTick;
        convertedThrough = promptText.LastIndexOf(MuPTSmtAbc.SliceSeparator, StringComparison.Ordinal);
    }

    /// <summary>Everything the model has written, prompt included, in the model's own form.</summary>
    public string RawText => raw.ToString();

    /// <summary>Where the segment's tick 0 sits in the tune's own ticks.</summary>
    public long OriginTick => originTick;

    /// <summary>The tick the pass has settled through, or -1 when nothing has settled yet.</summary>
    public long SettledThroughTick => settledThroughTick;

    /// <summary>How many events the pass has let out.</summary>
    public int ReleasedEventCount => released.Count;

    /// <summary>How many times the whole text has been converted.</summary>
    public int ConversionCount { get; private set; }

    /// <summary>
    /// How many events that had ALREADY BEEN LET OUT a later conversion changed - which is the
    /// prefix stability this path rests on, counted rather than assumed. It is zero, and a test
    /// says so over every audition file and every hand-written fixture.
    /// </summary>
    public int PrefixChangeCount { get; private set; }

    /// <summary>Takes in the next piece of text the model wrote and lets out whatever has settled.</summary>
    /// <param name="moreText">The text since the last call. Empty is allowed and does nothing.</param>
    /// <returns>What to yield, which is usually nothing.</returns>
    /// <exception cref="MusicGenerationException">The text could not be read as a tune.</exception>
    public IReadOnlyList<GeneratedMusicEvent> Accept(string moreText)
    {
        if (string.IsNullOrEmpty(moreText))
        {
            return Array.Empty<GeneratedMusicEvent>();
        }

        raw.Append(moreText);

        // A SLICE CAN ONLY HAVE COMPLETED WHERE ITS LAST CHARACTER ARRIVED, so text without one
        // costs nothing at all - which matters, because this is called for every token.
        if (moreText.IndexOf('>') < 0)
        {
            return Array.Empty<GeneratedMusicEvent>();
        }

        var text = raw.ToString();
        var boundary = text.LastIndexOf(MuPTSmtAbc.SliceSeparator, StringComparison.Ordinal);

        if (boundary <= convertedThrough)
        {
            return Array.Empty<GeneratedMusicEvent>();
        }

        convertedThrough = boundary;

        return Convert(text.Substring(0, boundary + MuPTSmtAbc.SliceSeparator.Length), false);
    }

    /// <summary>
    /// Ends the pass: the slice the model was part-way through is included, everything left is let
    /// out, and the final settled tick is the bar line the music ends at.
    /// </summary>
    /// <returns>What to yield last, which may be nothing at all.</returns>
    /// <exception cref="MusicGenerationException">The text could not be read as a tune.</exception>
    public IReadOnlyList<GeneratedMusicEvent> Finish()
    {
        if (finished)
        {
            return Array.Empty<GeneratedMusicEvent>();
        }

        finished = true;

        return Convert(raw.ToString(), true);
    }

    private IReadOnlyList<GeneratedMusicEvent> Convert(string modelText, bool isTheEnd)
    {
        var tune = MuPTTune.Read(modelText, ticksPerQuarterNote);

        ConversionCount++;

        if (tune == null)
        {
            return Array.Empty<GeneratedMusicEvent>();
        }

        CheckWhatWasAlreadyLetOut(tune);

        var settled = SettledTickOf(tune, isTheEnd);
        var taken = new List<GeneratedMusicEvent>();

        for (var index = promptEventCount + released.Count; index < tune.Events.Count; index++)
        {
            var midiEvent = tune.Events[index];
            var tick = SegmentTick(midiEvent.AbsoluteTime);

            if (!isTheEnd && tick > settled)
            {
                break;
            }

            var placed = Placed(midiEvent, tick);

            released.Add(placed);
            taken.Add(GeneratedMusicEvent.FromMidiEvent(placed, GeneratedMusicEvent.NothingSettled));
        }

        if (settled <= settledThroughTick && taken.Count == 0)
        {
            return Array.Empty<GeneratedMusicEvent>();
        }

        settledThroughTick = settled;

        if (taken.Count == 0)
        {
            // A SETTLED STRETCH WITH NOTHING IN IT IS STILL WORTH SAYING: a bar of rest has to
            // move the settled tick on, or silence would look like a generator that had stalled.
            return new[] { GeneratedMusicEvent.SettledThrough(settled) };
        }

        if (settled >= 0L)
        {
            // ONLY THE LAST ITEM SAYS THE TICK HAS SETTLED. An earlier one saying so would let the
            // caller play it before the rest of the group had arrived.
            taken[taken.Count - 1] = GeneratedMusicEvent.FromMidiEvent(
                taken[taken.Count - 1].Event, settled);
        }

        return taken;
    }

    private long SettledTickOf(MuPTTune tune, bool isTheEnd)
    {
        long settled;

        if (isTheEnd)
        {
            // THE PASS ENDS ON A BAR LINE, so the segment after it starts against one.
            settled = BarGrid.RoundUpToBar(SegmentMeterChanges(tune), ticksPerQuarterNote,
                SegmentTick(tune.EndTick));
        }
        else
        {
            settled = EarliestUnsettled(tune) - 1L;
        }

        return settled < settledThroughTick ? settledThroughTick : settled;
    }

    // WHAT A LATER SLICE CAN STILL CHANGE, IN ONE NUMBER. It can give a voice another bar, which
    // starts at that voice's END; and it can LENGTHEN that voice's last note, when the note is
    // holding a tie the converter has not seen the other half of. A voice's last note starts at or
    // before its end, so holding that note back covers both - and it covers a tie left open by the
    // REPEATS as well, which the text alone cannot show: a tune whose repeat has a first ending
    // and no second one ends its second pass in the middle of the section.
    private long EarliestUnsettled(MuPTTune tune)
    {
        if (tune.VoiceUnsettledFromTicks.Count == 0)
        {
            return 0L;
        }

        var earliest = long.MaxValue;

        for (var i = 0; i < tune.VoiceUnsettledFromTicks.Count; i++)
        {
            var at = SegmentTick(tune.VoiceUnsettledFromTicks[i]);

            if (at < earliest)
            {
                earliest = at;
            }
        }

        return earliest;
    }

    private IReadOnlyList<MeterChange> SegmentMeterChanges(MuPTTune tune)
    {
        var changes = new List<MeterChange>(tune.MeterChanges.Count);

        for (var i = 0; i < tune.MeterChanges.Count; i++)
        {
            changes.Add(new MeterChange(SegmentTick(tune.MeterChanges[i].Tick),
                tune.MeterChanges[i].Meter));
        }

        return changes;
    }

    private long SegmentTick(long tuneTick) => tuneTick <= originTick ? 0L : tuneTick - originTick;

    // PREFIX STABILITY, COUNTED RATHER THAN ASSUMED. Nothing can be taken back once it has been
    // let out, so a change here is not something to repair - it is something to know about, and a
    // test asserts it never happens.
    private void CheckWhatWasAlreadyLetOut(MuPTTune tune)
    {
        for (var i = 0; i < released.Count; i++)
        {
            var index = promptEventCount + i;

            if (index >= tune.Events.Count ||
                !IsTheSame(released[i], tune.Events[index], SegmentTick(tune.Events[index].AbsoluteTime)))
            {
                PrefixChangeCount++;
            }
        }
    }

    private static bool IsTheSame(MidiEvent released, MidiEvent now, long tick)
    {
        if (released.AbsoluteTime != tick || released.GetType() != now.GetType())
        {
            return false;
        }

        if (released is NoteOnEvent was && now is NoteOnEvent current)
        {
            return was.NoteNumber == current.NoteNumber && was.Channel == current.Channel &&
                   LengthOf(was) == LengthOf(current);
        }

        return true;
    }

    private static int LengthOf(NoteOnEvent note) => note.OffEvent == null ? 0 : note.NoteLength;

    private static MidiEvent Placed(MidiEvent midiEvent, long tick)
    {
        if (midiEvent is NoteOnEvent note)
        {
            return new NoteOnEvent(tick, note.Channel, note.NoteNumber, note.Velocity,
                LengthOf(note));
        }

        var copy = midiEvent.Clone();
        copy.AbsoluteTime = tick;

        return copy;
    }
}
