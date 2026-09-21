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
/// THE OPENING A VOICE COUNT BECOMES - the one way a number of parts can mean anything to a model
/// that reads notation.
/// </summary>
/// <remarks>
/// THE FENCE IS THE AUDITION'S OWN DUETS. Both rated duet prompts opened with a chord in two
/// parts, and this writer has to produce those bars exactly, or a request for two voices is not
/// asking for the music that was rated.
/// </remarks>
public class MuPTOpeningTests
{
    [Fact]
    public void the_first_bar_of_the_rated_C_duet_comes_out_exactly_as_it_was_prompted()
    {
        //Act
        var opening = MuPTOpening.For(MusicMeter.CommonTime, MusicNoteLength.EighthNote, "C",
            MusicMode.Major);

        //Assert - both parts of the first slice, character for character
        opening.Should().StartWith("\"C\" c2 e2 g2 e2 | C,4 G,4 | <|> <|> ");
    }

    [Fact]
    public void the_first_bar_of_the_rated_A_minor_waltz_duet_comes_out_exactly_as_it_was_prompted()
    {
        //Act
        var opening = MuPTOpening.For(new MusicMeter(3, 4), MusicNoteLength.EighthNote, "A",
            MusicMode.Minor);

        //Assert
        opening.Should().StartWith("\"Am\" e2 a2 c'2 | A,2 [CE]2 [CE]2 | <|> <|> ");
    }

    [Fact]
    public void the_second_chord_of_a_major_key_is_its_fifth_degree()
    {
        //Act
        var opening = MuPTOpening.For(MusicMeter.CommonTime, MusicNoteLength.EighthNote, "C",
            MusicMode.Major);

        //Assert - the bass of the second bar is the audition's own, G3 then D4
        opening.Should().Contain("\"G\" ").And.Contain("| G,4 D4 |");
    }

    [Fact]
    public void the_second_chord_of_a_minor_key_is_its_fourth_degree()
    {
        //Act
        var opening = MuPTOpening.For(new MusicMeter(3, 4), MusicNoteLength.EighthNote, "A",
            MusicMode.Minor);

        //Assert - the audition's own second bar in the lower part
        opening.Should().Contain("\"Dm\" ").And.Contain("| D,2 [FA]2 [FA]2 |");
    }

    [Fact]
    public void an_opening_is_two_slices_of_two_parts()
    {
        //Act
        var opening = MuPTOpening.For(MusicMeter.CommonTime, MusicNoteLength.EighthNote, "C",
            MusicMode.Major);

        //Assert - one bar line ends each part, and each slice is closed by the separator
        Count(opening, "<|>").Should().Be(4);
        Count(opening, " | ").Should().Be(4);
    }

    [Fact]
    public void a_compound_metre_is_written_in_groups_of_three()
    {
        //Act - a jig: six eighth notes felt in two dotted quarters
        var opening = MuPTOpening.For(new MusicMeter(6, 8), MusicNoteLength.EighthNote, "D",
            MusicMode.Major);

        //Assert - two groups of three, the first rising and the second falling, over a bass and
        //a chord
        opening.Should().StartWith("\"D\" dfa afd | D,3 [FA]3 | <|> <|> ");
    }

    [Fact]
    public void a_key_with_a_signature_writes_no_accidentals_of_its_own()
    {
        //Act - G minor carries two flats, and B flat is spelled by the signature
        var opening = MuPTOpening.For(MusicMeter.CommonTime, MusicNoteLength.EighthNote, "G",
            MusicMode.Minor);

        //Assert - the chord symbol carries the accidental; the notes do not
        opening.Should().StartWith("\"Gm\" g2 b2 d'2 b2 | G,4 D4 | ");
        opening.Should().NotContain("^").And.NotContain("_").And.NotContain("=");
    }

    [Fact]
    public void a_flat_key_names_its_chords_with_flats()
    {
        //Act
        var opening = MuPTOpening.For(MusicMeter.CommonTime, MusicNoteLength.EighthNote, "Bb",
            MusicMode.Major);

        //Assert - B flat, then its fifth degree F
        opening.Should().StartWith("\"Bb\" ").And.Contain("\"F\" ");
    }

    [Fact]
    public void a_mode_names_its_own_chords()
    {
        //Act - D mixolydian: a major tonic triad, so the second chord is the fifth degree
        var opening = MuPTOpening.For(MusicMeter.CommonTime, MusicNoteLength.EighthNote, "D",
            MusicMode.Mixolydian);

        //Assert - the fifth degree of D mixolydian is A, and its third is C natural: A minor
        opening.Should().StartWith("\"D\" ").And.Contain("\"Am\" ");
    }

    [Fact]
    public void a_unit_note_length_of_a_quarter_writes_shorter_numbers()
    {
        //Act - four quarter notes to the bar means each chord tone is one unit
        var opening = MuPTOpening.For(MusicMeter.CommonTime, MusicNoteLength.QuarterNote, "C",
            MusicMode.Major);

        //Assert
        opening.Should().StartWith("\"C\" c e g e | C,2 G,2 | ");
    }

    [Theory]
    [InlineData(2, 4, "\"C\" c e g e | C,2 G,2 | ")]
    [InlineData(5, 4, "\"C\" c5 g5 | C,5 G,5 | ")]
    public void an_unusual_metre_still_fills_its_bar(int beats, int beatNote, string expected)
    {
        //Act
        var opening = MuPTOpening.For(new MusicMeter(beats, beatNote),
            MusicNoteLength.EighthNote, "C", MusicMode.Major);

        //Assert
        opening.Should().StartWith(expected);
    }

    [Fact]
    public void one_voice_wants_no_opening_at_all()
    {
        //Arrange
        var intent = new MusicIntent { VoiceCount = 1 };

        //Act
        var header = MuPTHeader.For(intent, null);

        //Assert - a header alone is what one line of melody is asked for with
        header.Opening.Should().BeEmpty();
    }

    [Fact]
    public void two_voices_put_the_opening_into_the_header()
    {
        //Arrange
        var intent = new MusicIntent
        {
            Key = "A",
            Mode = MusicMode.Minor,
            Meter = new MusicMeter(3, 4),
            BeatsPerMinute = 108.0,
            VoiceCount = 2
        };

        //Act
        var prompt = MuPTHeader.For(intent, null).ToPrompt();

        //Assert - the header the audition sent, and an opening of two parts under it
        prompt.Should().StartWith("X:1<n>L:1/8<n>Q:1/4=108<n>M:3/4<n>K:Am<n>");
        prompt.Should().Contain("\"Am\" e2 a2 c'2 | A,2 [CE]2 [CE]2 | <|> <|> ");
    }

    [Fact]
    public void the_opening_really_converts_to_two_parts_of_the_right_length()
    {
        //Arrange - the prompt an intent for two voices produces, fed through the very path that
        //turns the model's own text into music
        var intent = new MusicIntent
        {
            Key = "A",
            Mode = MusicMode.Minor,
            Meter = new MusicMeter(3, 4),
            BeatsPerMinute = 108.0,
            VoiceCount = 2
        };

        var path = new MuPTSlicePath(string.Empty, 480, false);
        var produced = new List<GeneratedMusicEvent>();

        //Act
        produced.AddRange(path.Accept(MuPTHeader.For(intent, null).ToPrompt()));
        produced.AddRange(path.Finish());

        //Assert - two parts sound, and everything written falls inside the two 3/4 bars the
        //opening covers: a bar is three quarter notes, so two bars end at tick 2880
        var channels = new SortedSet<int>();
        var notes = 0;

        foreach (var item in produced)
        {
            if (item.HasEvent && item.Event is NoteOnEvent note)
            {
                channels.Add(note.Channel);
                notes++;
                (note.AbsoluteTime + note.NoteLength).Should().BeLessThanOrEqualTo(2880L);
            }
        }

        channels.Should().HaveCount(2);
        notes.Should().Be(16,
            "each bar is three melody notes over a bass note and two chords of two notes");
    }

    [Fact]
    public void a_key_with_no_signature_at_all_is_opened_in_C()
    {
        //Arrange - "none" writes no key signature, so nothing spells an accidental
        var intent = new MusicIntent { Key = "F#", Mode = MusicMode.None, VoiceCount = 2 };

        //Act
        var header = MuPTHeader.For(intent, null);

        //Assert
        header.Key.Should().Be("none");
        header.Opening.Should().StartWith("\"C\" ");
    }

    [Fact]
    public void more_voices_than_can_be_written_are_refused_by_name()
    {
        //Arrange
        var intent = new MusicIntent { VoiceCount = 3 };

        //Act
        Action act = () => MuPTOpening.EnsureWritable(intent, "MuPT");

        //Assert
        var thrown = act.Should().Throw<MusicRequestNotHonouredException>().Which;
        thrown.Message.Should().Contain("MuPT").And.Contain("ModelNativeText");
        thrown.UnhonouredFeatures.Should().Be(MusicRequestFeatures.VoiceCount);
    }

    [Fact]
    public void a_bar_that_does_not_divide_into_whole_unit_notes_is_refused_by_name()
    {
        //Arrange - a dotted unit note length does not divide a bar of four quarter notes
        var intent = new MusicIntent
        {
            VoiceCount = 2,
            Meter = MusicMeter.CommonTime,
            UnitNoteLength = new MusicNoteLength(3, 8)
        };

        //Act
        Action act = () => MuPTOpening.EnsureWritable(intent, "MuPT");

        //Assert
        act.Should().Throw<MusicRequestNotHonouredException>().Which
            .Message.Should().Contain("whole unit notes");
    }

    [Fact]
    public void one_voice_and_no_voice_count_are_both_writable()
    {
        //Act
        Action one = () => MuPTOpening.EnsureWritable(new MusicIntent { VoiceCount = 1 }, "MuPT");
        Action none = () => MuPTOpening.EnsureWritable(new MusicIntent(), "MuPT");
        Action nothing = () => MuPTOpening.EnsureWritable(null, "MuPT");

        //Assert
        one.Should().NotThrow();
        none.Should().NotThrow();
        nothing.Should().NotThrow();
    }

    private static int Count(string text, string what)
    {
        var found = 0;
        var at = text.IndexOf(what, StringComparison.Ordinal);

        while (at >= 0)
        {
            found++;
            at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal);
        }

        return found;
    }
}
