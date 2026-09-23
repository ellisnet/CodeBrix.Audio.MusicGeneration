using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE FENCE: turning the model's text into music WHILE IT IS BEING WRITTEN produces exactly what
/// turning the finished text into music produces - event for event, tick for tick - and nothing is
/// ever let out at or before a tick already announced as settled.
/// </summary>
/// <remarks>
/// <para>
/// IT IS HELD TO THE REAL THING: all thirty-two audition tunes, fed in a character at a time, in
/// threes and in sevens, because a model's tokens do not arrive on any boundary that matters.
/// </para>
/// <para>
/// AND TO WHAT THE REAL THING DOES NOT COVER. No audition tune has a tie across a bar line, so the
/// fence would pass with the tie rule missing: the hand-written fixtures below all carry one, and
/// each of them also proves that the NAIVE settled tick - the end of the last complete slice,
/// ties ignored - would have let out an event the finished text changes.
/// </para>
/// </remarks>
public class MuPTSlicePathTests
{
    private const int Resolution = 480;

    private const string Header = "X:1<n>L:1/8<n>M:4/4<n>K:C<n>";

    // A TIE ACROSS A BAR LINE: B2- ends the bar and its partner opens the next one, so the two
    // become one half note. A prefix ending at the bar line has a quarter note instead.
    private const string CrossBarTie = "CDEF GAB2- | <|> <|> B2 cdef ga | <|> <|> GFED C4 | <|>";

    // A TIE INTO A REPEAT: the same join, inside a section that is played twice.
    private const string TieIntoARepeat =
        "|:CDEF GAB2- | <|> <|> B2 cdef ga :| <|> <|> GFED C4 | <|>";

    // FIRST AND SECOND ENDINGS, with the tie carrying into both of them - and the endings written
    // the way the model writes them, detached from their bar line.
    private const string TieIntoNumberedEndings =
        "|:CDEF GAB2- | <|> <|> 1 B2 cdef ga :| <|> <|> 2 B2 GFED C2 | <|>";

    // INLINE FIELD CHANGES, each of them in force for the bars after it and none of them for the
    // bars before, with a tie across the bar line after the key change. Inline tempo changes
    // also stay at their own ticks when the header has no tempo.
    private const string TieAfterInlineFieldChanges =
        "CDEF GABc | <|> <|> [Q:1/4=90] [K:D] defg ab c2- | <|> <|> c2 defg ab | <|> <|> " +
        "[M:3/4] [L:1/4] [Q:1/4=144] d e f | <|> <|> g a b | <|>";

    // TWO VOICES AND ONE TIE: the upper part holds the note across the bar line, the lower part
    // does not, and nothing of either may be let out while the tie is open.
    private const string TieInOneVoiceOfTwo =
        "CDEF GAB2- | C,4 E,4 | <|> <|> B2 cdef ga | C,4 G,4 | <|> <|> GFED C4 | C,8 | <|>";

    [Fact]
    public void every_audition_tune_converts_the_same_piece_by_piece_as_it_does_in_one_go()
    {
        //Arrange
        var files = MuPTAuditionFiles.All();
        var wrong = new List<string>();

        //Act
        foreach (var file in files)
        {
            foreach (var chunk in new[] { 1, 3, 7 })
            {
                var incremental = Describe(Incremental(file.RawText, chunk));
                var oneShot = Describe(OneShot(file.RawText));

                if (!SameAs(incremental, oneShot))
                {
                    wrong.Add(file.Name + " in pieces of " + chunk);
                }
            }
        }

        //Assert
        files.Should().HaveCount(32);
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void no_audition_tune_ever_lets_out_an_event_at_or_before_a_tick_already_settled()
    {
        //Arrange
        var wrong = new List<string>();

        //Act
        foreach (var file in MuPTAuditionFiles.All())
        {
            var settled = GeneratedMusicEvent.NothingSettled;

            foreach (var item in Incremental(file.RawText, 3))
            {
                if (item.HasEvent && item.Event.AbsoluteTime <= settled)
                {
                    wrong.Add(file.Name + " at tick " + item.Event.AbsoluteTime);
                }

                if (item.HasSettled)
                {
                    settled = item.SettledThroughTick;
                }
            }
        }

        //Assert
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void nothing_already_let_out_is_ever_changed_by_a_later_slice()
    {
        //Arrange
        var changed = new List<string>();

        //Act
        foreach (var file in MuPTAuditionFiles.All())
        {
            var path = Feed(file.RawText, 3);

            if (path.PrefixChangeCount != 0)
            {
                changed.Add(file.Name);
            }
        }

        //Assert
        changed.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CrossBarTie)]
    [InlineData(TieIntoARepeat)]
    [InlineData(TieIntoNumberedEndings)]
    [InlineData(TieAfterInlineFieldChanges)]
    [InlineData(TieInOneVoiceOfTwo)]
    public void a_tie_fixture_converts_the_same_piece_by_piece_as_it_does_in_one_go(string body)
    {
        //Arrange
        var text = Header + body;

        //Act
        var incremental = Describe(Incremental(text, 1));

        //Assert
        incremental.Should().Equal(Describe(OneShot(text)));
        Feed(text, 1).PrefixChangeCount.Should().Be(0);
    }

    [Theory]
    [InlineData(CrossBarTie)]
    [InlineData(TieIntoARepeat)]
    [InlineData(TieIntoNumberedEndings)]
    [InlineData(TieAfterInlineFieldChanges)]
    [InlineData(TieInOneVoiceOfTwo)]
    public void the_naive_settled_tick_would_have_let_out_a_note_the_rest_of_the_text_changes(
        string body)
    {
        //Arrange - the text through the slice the tie is left open in
        var text = Header + body;
        var prefix = PrefixThroughTheOpenTie(text);

        //Act
        var wrong = WhatTheNaiveRuleWouldHaveLetOut(prefix, text);

        //Assert - the fixture bites: without the tie rule, a note of the wrong length is heard
        wrong.Should().NotBeNull();
        TestContext.Current.TestOutputHelper.WriteLine(wrong);
    }

    [Fact]
    public void the_settled_tick_does_not_move_past_a_note_a_tie_could_still_lengthen()
    {
        //Arrange - the tied note begins at tick 1440, in a bar that ends at 1920
        var text = Header + CrossBarTie;

        //Act - one slice at a time, watching what the path says it has settled
        var afterTheOpenTie = Feed(Slices(text, 1), 1).SettledThroughTick;
        var afterItsPartner = Feed(Slices(text, 3), 1).SettledThroughTick;

        //Assert - the first bar settles only what is in front of the tied note, and of the note
        //before it, which ends exactly where the tied one begins; the second bar closes the tie,
        //and the settled tick moves past it.
        afterTheOpenTie.Should().Be(1199L);
        afterItsPartner.Should().BeGreaterThan(1920L);
    }

    [Fact]
    public void a_complete_slice_settles_the_music_in_front_of_what_a_tie_could_still_join()
    {
        //Arrange - two bars of eight eighth notes, so a bar is 1,920 ticks and a note is 240
        var text = Header + "CDEF GABc | <|> <|> cBAG FEDC | <|>";

        //Act
        var path = Feed(text, 1);

        //Assert - everything but the last two notes: the last one is what a tie could lengthen,
        //and the one before it ends exactly where that one begins.
        path.SettledThroughTick.Should().Be((2L * 1920L) - (2L * 240L) - 1L);
    }

    [Fact]
    public void the_pass_ends_on_a_bar_line_even_when_the_model_stopped_part_way_through_one()
    {
        //Arrange - the model stopped after half a bar, with no slice separator to end it
        var text = Header + "CDEF GABc | <|> <|> cBAG";

        //Act
        var path = new MuPTSlicePath(string.Empty, Resolution, false);
        var produced = new List<GeneratedMusicEvent>(path.Accept(text));

        produced.AddRange(path.Finish());

        //Assert - the half bar is played, and the pass settles at the bar line after it
        Notes(produced).Should().HaveCount(12);
        path.SettledThroughTick.Should().Be(2L * 1920L);
    }

    [Fact]
    public void a_tune_that_never_closed_its_header_yields_nothing_at_all()
    {
        //Act
        var produced = Incremental("X:1<n>L:1/8<n>M:4/4", 1);

        //Assert
        produced.Should().BeEmpty();
    }

    [Fact]
    public void a_repeat_that_was_opened_and_never_closed_is_music_rather_than_an_error()
    {
        //Act
        var produced = Incremental(Header + "|:CDEF GABc | <|> <|> cBAG FEDC | <|>", 1);

        //Assert
        Notes(produced).Should().HaveCount(16);
    }

    [Fact]
    public void a_continuation_plays_none_of_the_prompt_again_and_starts_at_its_own_tick_zero()
    {
        //Arrange - two bars are the tail, and the model writes a third
        var tail = Header + "CDEF GABc | <|> <|> cBAG FEDC | <|>";
        var path = new MuPTSlicePath(tail, Resolution, true);

        //Act
        var produced = new List<GeneratedMusicEvent>(path.Accept(" GABc cBAG | <|>"));

        produced.AddRange(path.Finish());

        //Assert - eight new notes, the first of them at the segment's own tick 0
        var notes = Notes(produced);

        notes.Should().HaveCount(8);
        notes[0].AbsoluteTime.Should().Be(0L);
        path.OriginTick.Should().Be(2L * 1920L);
    }

    [Fact]
    public void a_continuation_that_is_prompted_with_nothing_playable_starts_from_tick_zero()
    {
        //Arrange
        var path = new MuPTSlicePath("X:1<n>L:1/8<n>M:4/4<n>K:C<n>", Resolution, true);

        //Act
        var produced = new List<GeneratedMusicEvent>(path.Accept("CDEF GABc | <|>"));

        produced.AddRange(path.Finish());

        //Assert
        path.OriginTick.Should().Be(0L);
        Notes(produced).Should().HaveCount(8);
    }

    // --- the rig ------------------------------------------------------------------------------

    private static MuPTSlicePath Feed(string modelText, int chunk)
    {
        var path = new MuPTSlicePath(string.Empty, Resolution, false);

        for (var at = 0; at < modelText.Length; at += chunk)
        {
            path.Accept(modelText.Substring(at, Math.Min(chunk, modelText.Length - at)));
        }

        return path;
    }

    private static IReadOnlyList<GeneratedMusicEvent> Incremental(string modelText, int chunk)
    {
        var path = new MuPTSlicePath(string.Empty, Resolution, false);
        var produced = new List<GeneratedMusicEvent>();

        for (var at = 0; at < modelText.Length; at += chunk)
        {
            produced.AddRange(path.Accept(modelText.Substring(at,
                Math.Min(chunk, modelText.Length - at))));
        }

        produced.AddRange(path.Finish());

        return produced;
    }

    private static IReadOnlyList<MidiEvent> OneShot(string modelText)
    {
        var tune = MuPTTune.Read(modelText, Resolution);

        return tune == null ? Array.Empty<MidiEvent>() : tune.Events;
    }

    private static IReadOnlyList<NoteOnEvent> Notes(IReadOnlyList<GeneratedMusicEvent> produced)
    {
        var notes = new List<NoteOnEvent>();

        foreach (var item in produced)
        {
            if (item.HasEvent && item.Event is NoteOnEvent note)
            {
                notes.Add(note);
            }
        }

        return notes;
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<GeneratedMusicEvent> produced)
    {
        var described = new List<string>();

        foreach (var item in produced)
        {
            if (item.HasEvent)
            {
                described.Add(Describe(item.Event));
            }
        }

        return described;
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<MidiEvent> events)
    {
        var described = new List<string>();

        for (var i = 0; i < events.Count; i++)
        {
            described.Add(Describe(events[i]));
        }

        return described;
    }

    private static string Describe(MidiEvent midiEvent) =>
        midiEvent is NoteOnEvent note
            ? "note " + note.NoteNumber + " channel " + note.Channel + " at " + note.AbsoluteTime +
              " for " + (note.OffEvent == null ? 0 : note.NoteLength)
            : midiEvent.GetType().Name + " at " + midiEvent.AbsoluteTime + " " + midiEvent;

    private static bool SameAs(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    // The text through the FIRST slice that leaves a tie open, which is where the naive rule and
    // the tie-aware one part company.
    private static string PrefixThroughTheOpenTie(string modelText)
    {
        for (var slices = 1; slices < 16; slices++)
        {
            var prefix = Slices(modelText, slices);

            if (prefix == null)
            {
                return null;
            }

            if (MuPTSmtAbc.AVoiceIsHoldingATie(MuPTSmtAbc.ToStandardAbc(prefix, null)))
            {
                return prefix;
            }
        }

        return null;
    }

    // The text through the first so many complete slices.
    private static string Slices(string modelText, int count)
    {
        var at = 0;

        for (var taken = 0; taken < count; taken++)
        {
            var next = modelText.IndexOf(MuPTSmtAbc.SliceSeparator, at, StringComparison.Ordinal);

            if (next < 0)
            {
                return null;
            }

            at = next + MuPTSmtAbc.SliceSeparator.Length;
        }

        return modelText.Substring(0, at);
    }

    // THE NAIVE RULE, written out: settle through the end of the last complete slice and ignore
    // ties. This finds the note it would have let out that the finished text does not agree with.
    private static string WhatTheNaiveRuleWouldHaveLetOut(string prefix, string whole)
    {
        var early = MuPTTune.Read(prefix, Resolution);
        var final = MuPTTune.Read(whole, Resolution);
        var naive = EarliestVoiceEnd(early) - 1L;

        foreach (var midiEvent in early.Events)
        {
            if (midiEvent is not NoteOnEvent note || note.AbsoluteTime > naive)
            {
                continue;
            }

            var inTheEnd = Matching(final, note);

            if (inTheEnd < 0)
            {
                return "the naive settled tick " + naive + " would have let out note " +
                       note.NoteNumber + " at tick " + note.AbsoluteTime + " " + note.NoteLength +
                       " ticks long; the finished text makes it " + LengthOf(final, note) +
                       " ticks long";
            }
        }

        return null;
    }

    private static long EarliestVoiceEnd(MuPTTune tune)
    {
        var earliest = long.MaxValue;

        for (var i = 0; i < tune.VoiceEndTicks.Count; i++)
        {
            if (tune.VoiceEndTicks[i] < earliest)
            {
                earliest = tune.VoiceEndTicks[i];
            }
        }

        return earliest == long.MaxValue ? 0L : earliest;
    }

    private static int Matching(MuPTTune tune, NoteOnEvent note)
    {
        for (var i = 0; i < tune.Events.Count; i++)
        {
            if (tune.Events[i] is NoteOnEvent other && other.AbsoluteTime == note.AbsoluteTime &&
                other.NoteNumber == note.NoteNumber && other.Channel == note.Channel &&
                other.NoteLength == note.NoteLength)
            {
                return i;
            }
        }

        return -1;
    }

    private static int LengthOf(MuPTTune tune, NoteOnEvent note)
    {
        for (var i = 0; i < tune.Events.Count; i++)
        {
            if (tune.Events[i] is NoteOnEvent other && other.AbsoluteTime == note.AbsoluteTime &&
                other.NoteNumber == note.NoteNumber && other.Channel == note.Channel)
            {
                return other.NoteLength;
            }
        }

        return -1;
    }
}
