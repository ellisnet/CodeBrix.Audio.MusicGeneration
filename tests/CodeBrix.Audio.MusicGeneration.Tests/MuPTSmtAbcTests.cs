using System.Collections.Generic;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The model's own text form, and standard ABC notation: the publisher's post-processing, ported.
/// </summary>
/// <remarks>
/// THE PORT IS FENCED AGAINST THE REAL THING. Every one of the thirty-two audition tunes was
/// turned into ABC by the scratch generator that ran the listening sessions, and both the text it
/// was given and the text it produced are in the test assets - so the port has to produce the
/// second from the first, character for character, thirty-two times over.
/// </remarks>
public class MuPTSmtAbcTests
{
    [Fact]
    public void ToStandardAbc_produces_what_the_listening_session_heard_for_every_audition_tune()
    {
        //Arrange
        var files = MuPTAuditionFiles.All();
        var wrong = new List<string>();

        //Act
        foreach (var file in files)
        {
            if (MuPTSmtAbc.ToStandardAbc(file.RawText, file.Title) != file.StandardAbc)
            {
                wrong.Add(file.Name);
            }
        }

        //Assert
        files.Should().HaveCount(32);
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void ToStandardAbc_turns_one_voice_into_a_tune_with_no_voice_fields()
    {
        //Act
        var abc = MuPTSmtAbc.ToStandardAbc(
            "X:1<n>L:1/8<n>M:4/4<n>K:C<n>CDEF GABc | <|> <|> cBAG FEDC | <|>", null);

        //Assert
        abc.Should().Be("X:1\nL:1/8\nM:4/4\nK:C\nCDEF GABc | cBAG FEDC |\n");
    }

    [Fact]
    public void ToStandardAbc_gives_each_part_of_a_merged_slice_a_voice_of_its_own()
    {
        //Act
        var abc = MuPTSmtAbc.ToStandardAbc(
            "X:1<n>L:1/8<n>M:4/4<n>K:C<n>CDEF GABc | C,4 G,4 | <|> <|> cBAG FEDC | C,4 E,4 | <|>",
            null);

        //Assert
        abc.Should().Be(
            "X:1\nL:1/8\nM:4/4\nK:C\nV:1\nCDEF GABc | cBAG FEDC |\nV:2\nC,4 G,4 | C,4 E,4 |\n");
    }

    [Fact]
    public void ToStandardAbc_gives_a_part_that_stops_being_written_the_bars_there_are()
    {
        //Act - the second slice carries one part where the first carried two
        var abc = MuPTSmtAbc.ToStandardAbc(
            "X:1<n>L:1/8<n>M:4/4<n>K:C<n>CDEF GABc | C,4 G,4 | <|> <|> cBAG FEDC | <|>", null);

        //Assert
        abc.Should().Be("X:1\nL:1/8\nM:4/4\nK:C\nV:1\nCDEF GABc | cBAG FEDC |\nV:2\nC,4 G,4 |\n");
    }

    [Fact]
    public void ToStandardAbc_attaches_a_numbered_ending_to_the_bar_line_the_model_detached_it_from()
    {
        //Act - the ending opens the next slice, which is where the model writes it
        var abc = MuPTSmtAbc.ToStandardAbc(
            "X:1<n>L:1/8<n>M:4/4<n>K:C<n>|:CDEF GABc | <|> <|> 1 cBAG FEDC :| <|> <|> " +
            "2 cdef gabc' | <|>", null);

        //Assert
        abc.Should().Contain("|1 cBAG").And.Contain(":|2 cdef");
    }

    [Fact]
    public void ToStandardAbc_reads_the_models_newline_and_drops_its_sequence_markers() =>
        MuPTSmtAbc.ToStandardAbc("<bos>X:1<n>L:1/8<n>M:4/4<n>K:C<n>CDEF GABc | <|><eos>", null)
            .Should().Be("X:1\nL:1/8\nM:4/4\nK:C\nCDEF GABc |\n");

    [Fact]
    public void ToStandardAbc_leaves_text_that_never_closed_its_header_alone() =>
        MuPTSmtAbc.ToStandardAbc("X:1<n>L:1/8<n>M:4/4", null).Should().Be("X:1\nL:1/8\nM:4/4\n");

    [Fact]
    public void ToStandardAbc_writes_the_title_where_a_player_looks_for_it() =>
        MuPTSmtAbc.ToStandardAbc("X:1<n>K:C<n>CDEF | <|>", "A Tune")
            .Should().StartWith("X:1\nT:A Tune\nK:C\n");

    [Fact]
    public void HeaderIsClosed_is_false_until_the_key_line_has_been_written_and_ended()
    {
        //Assert
        MuPTSmtAbc.HeaderIsClosed("X:1<n>L:1/8<n>M:4/4<n>K:C").Should().BeFalse();
        MuPTSmtAbc.HeaderIsClosed("X:1<n>L:1/8<n>M:4/4<n>K:C<n>").Should().BeTrue();
    }

    [Fact]
    public void CompleteSlicePrefix_keeps_every_finished_slice_and_none_of_the_one_being_written()
    {
        //Assert
        MuPTSmtAbc.CompleteSlicePrefix("K:C<n>abc | <|> de").Should().Be("K:C<n>abc | <|>");
        MuPTSmtAbc.CompleteSlicePrefix("K:C<n>abc").Should().BeNull();
    }

    [Fact]
    public void LastSlices_counts_the_slices_that_hold_music_rather_than_the_separators()
    {
        //Arrange - the model writes the separator twice over between one bar and the next
        const string Text = "X:1<n>K:C<n>one | <|> <|> two | <|> <|> three | <|> <|> four |";

        //Act
        var tail = MuPTSmtAbc.LastSlices(Text, 2);

        //Assert
        tail.Should().Be(" three | <|> <|> four |");
    }

    [Fact]
    public void LastSlices_hands_back_the_whole_body_when_there_is_less_than_that_to_keep() =>
        MuPTSmtAbc.LastSlices("X:1<n>K:C<n>one | <|> <|> two |", 8)
            .Should().Be("one | <|> <|> two |");

    [Fact]
    public void AVoiceIsHoldingATie_sees_a_tie_that_has_no_partner_yet()
    {
        //Assert
        MuPTSmtAbc.AVoiceIsHoldingATie("X:1\nK:C\nCDEF GAB c2-|").Should().BeTrue();
        MuPTSmtAbc.AVoiceIsHoldingATie("X:1\nK:C\nCDEF GAB c2|").Should().BeFalse();
        MuPTSmtAbc.AVoiceIsHoldingATie("X:1\nK:C\nCDEF GA [ce]-").Should().BeTrue();
        MuPTSmtAbc.AVoiceIsHoldingATie("X:1\nK:C\nCDEF GA [ce]").Should().BeFalse();
    }

    [Fact]
    public void AVoiceIsHoldingATie_looks_at_every_voice_and_not_just_the_last()
    {
        //Assert
        MuPTSmtAbc.AVoiceIsHoldingATie("X:1\nK:C\nV:1\nc2-|\nV:2\nC,4|").Should().BeTrue();
        MuPTSmtAbc.AVoiceIsHoldingATie("X:1\nK:C\nV:1\nc2|\nV:2\nC,4|").Should().BeFalse();
    }

    [Fact]
    public void AVoiceIsHoldingATie_is_not_fooled_by_a_chord_symbol_that_ends_the_line() =>
        MuPTSmtAbc.AVoiceIsHoldingATie("X:1\nK:C\nc2 \"Cm-7\"").Should().BeFalse();
}
