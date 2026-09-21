using System.Collections.Generic;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// What the model is given: the header, the opening, and the tail of its own text that a
/// continuation carries over.
/// </summary>
/// <remarks>
/// THE EIGHT AUDITION PROMPTS ARE THE FENCE. Every piece of music this library's taste rests on
/// was written from one of them, so the seam an intent becomes a prompt through has to be able to
/// produce them exactly - character for character - or the music that comes back is not the music
/// that was rated.
/// </remarks>
public class MuPTPromptTests
{
    [Fact]
    public void the_seam_builds_the_eight_audition_prompts_exactly_as_they_were_sent()
    {
        //Arrange
        var wrong = new List<string>();

        //Act
        foreach (var piece in AuditionPrompts())
        {
            if (piece.Header.ToPrompt() != piece.Prompt)
            {
                wrong.Add(piece.Name + ": " + piece.Header.ToPrompt());
            }
        }

        //Assert
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void a_header_is_the_model_cards_own_field_order_joined_with_its_own_newline() =>
        new MuPTHeader
        {
            UnitNoteLength = new MusicNoteLength(1, 8),
            TempoBeat = new MusicNoteLength(1, 4),
            TempoPerMinute = 120.0,
            Meter = new MusicMeter(3, 4),
            Key = "Am"
        }.ToPrompt().Should().Be("X:1<n>L:1/8<n>Q:1/4=120<n>M:3/4<n>K:Am<n>");

    [Fact]
    public void an_intent_becomes_the_header_it_asks_for()
    {
        //Arrange
        var intent = new MusicIntent
        {
            Key = "G",
            Mode = MusicMode.Minor,
            Meter = new MusicMeter(4, 4),
            UnitNoteLength = new MusicNoteLength(1, 8),
            BeatsPerMinute = 100.0
        };

        //Act
        var header = MuPTHeader.For(intent, null);

        //Assert
        header.ToPrompt().Should().Be("X:1<n>L:1/8<n>Q:1/4=100<n>M:4/4<n>K:Gm<n>");
    }

    [Fact]
    public void a_tempo_is_written_in_the_beat_the_metre_is_felt_in()
    {
        //Arrange - a jig: six eighth notes to the bar, felt in two dotted quarters
        var intent = new MusicIntent
        {
            Meter = new MusicMeter(6, 8),
            BeatsPerMinute = 165.0
        };

        //Act
        var header = MuPTHeader.For(intent, null);

        //Assert - 165 quarter notes a minute is 110 dotted quarters a minute
        header.ToPrompt().Should().Be("X:1<n>L:1/8<n>Q:3/8=110<n>M:6/8<n>K:C<n>");
    }

    [Theory]
    [InlineData(MusicMode.Major, "D")]
    [InlineData(MusicMode.Ionian, "D")]
    [InlineData(MusicMode.Minor, "Dm")]
    [InlineData(MusicMode.Aeolian, "Dm")]
    [InlineData(MusicMode.Dorian, "Ddor")]
    [InlineData(MusicMode.Mixolydian, "Dmix")]
    [InlineData(MusicMode.Phrygian, "Dphr")]
    [InlineData(MusicMode.Lydian, "Dlyd")]
    [InlineData(MusicMode.Locrian, "Dloc")]
    public void a_mode_is_written_the_way_abc_writes_it(MusicMode mode, string expected) =>
        MuPTHeader.KeyOf("D", mode).Should().Be(expected);

    [Fact]
    public void a_header_with_no_tempo_asked_for_writes_no_tempo_field() =>
        MuPTHeader.For(new MusicIntent { Key = "C" }, null).ToPrompt()
            .Should().Be("X:1<n>L:1/8<n>M:4/4<n>K:C<n>");

    [Fact]
    public void model_native_text_is_the_prompt_itself_and_nothing_is_added_to_it()
    {
        //Arrange
        var request = new MusicRequest
        {
            ModelNativeText = "X:1\nL:1/8\nQ:1/4=108\nM:3/4\nK:Am\n\"Am\" e2 a2 c'2 | <|> <|> "
        };

        //Act
        var prompt = MuPTPrompt.For(request, new MuPTGeneratorOptions(), null, null);

        //Assert - the caller's notation, with the model's own spelling of a line break
        prompt.Text.Should().Be(
            "X:1<n>L:1/8<n>Q:1/4=108<n>M:3/4<n>K:Am<n>\"Am\" e2 a2 c'2 | <|> <|> ");
        prompt.IsAlreadyHeard.Should().BeFalse();
    }

    [Fact]
    public void a_continuation_is_prompted_with_the_original_header_and_the_models_own_tail()
    {
        //Arrange - what the last pass wrote, and the header it started from
        const string Header = "X:1<n>L:1/8<n>Q:1/4=120<n>M:4/4<n>K:C<n>";
        const string Written = Header +
            "one | <|> <|> two | <|> <|> three | <|> <|> four | <|> <|> five | <|>";

        var request = new MusicRequest { Continuation = new MusicContinuation() };
        var options = new MuPTGeneratorOptions { MaximumPromptBars = 2 };

        //Act
        var prompt = MuPTPrompt.For(request, options, Header, Written);

        //Assert - the header, then the last two bars of its own text, and none of the rest
        prompt.Text.Should().Be(Header + " four | <|> <|> five | <|>");
        prompt.IsAlreadyHeard.Should().BeTrue();
        prompt.HeaderText.Should().Be(Header);
    }

    [Fact]
    public void a_continuation_wins_over_the_opening_the_piece_started_from()
    {
        //Arrange - the engine builds every later segment from the request the piece began with,
        //so the opening is still on it, and playing that again would write the piece twice
        const string Header = "X:1<n>L:1/8<n>Q:1/4=108<n>M:3/4<n>K:Am<n>";
        var request = new MusicRequest
        {
            ModelNativeText = Header + "\"Am\" e2 a2 c'2 | <|> <|> ",
            Continuation = new MusicContinuation()
        };

        //Act
        var prompt = MuPTPrompt.For(request, new MuPTGeneratorOptions(), Header,
            Header + "one | <|> <|> two | <|>");

        //Assert - the model's own last bars, not the opening
        prompt.Text.Should().Be(Header + "one | <|> <|> two | <|>");
        prompt.IsAlreadyHeard.Should().BeTrue();
    }

    [Fact]
    public void a_continuation_with_no_text_left_keeps_the_header_the_piece_began_with()
    {
        //Arrange - a generator that was released has no text of its own any more
        const string Header = "X:1<n>L:1/8<n>Q:1/4=108<n>M:3/4<n>K:Am<n>";
        var request = new MusicRequest
        {
            ModelNativeText = Header + "\"Am\" e2 a2 c'2 | <|> <|> ",
            Continuation = new MusicContinuation()
        };

        //Act
        var prompt = MuPTPrompt.For(request, new MuPTGeneratorOptions(), null, null);

        //Assert - the key, the metre and the tempo still come from the opening's own header
        prompt.Text.Should().Be(Header);
        prompt.IsAlreadyHeard.Should().BeFalse();
    }

    [Fact]
    public void a_continuation_with_nothing_remembered_starts_again_from_what_it_carries()
    {
        //Arrange - the generator was released, so its own text is gone
        var request = new MusicRequest
        {
            Continuation = new MusicContinuation
            {
                Key = "A",
                Mode = MusicMode.Minor,
                Meter = new MusicMeter(3, 4),
                BeatsPerMinute = 108.0
            }
        };

        //Act
        var prompt = MuPTPrompt.For(request, new MuPTGeneratorOptions(), null, null);

        //Assert - the key, the metre and the tempo carry over even though the text did not
        prompt.Text.Should().Be("X:1<n>L:1/8<n>Q:1/4=108<n>M:3/4<n>K:Am<n>");
        prompt.IsAlreadyHeard.Should().BeFalse();
    }

    [Fact]
    public void a_tail_a_caller_wrote_itself_wins_over_the_one_the_generator_remembers()
    {
        //Arrange
        var request = new MusicRequest
        {
            Continuation = new MusicContinuation
            {
                ModelNativeTail = "X:1\nL:1/8\nM:4/4\nK:C\nCDEF GABc | <|> <|> "
            }
        };

        //Act
        var prompt = MuPTPrompt.For(request, new MuPTGeneratorOptions(), "X:1<n>K:G<n>",
            "X:1<n>K:G<n>GABc | <|>");

        //Assert - a whole tune of the caller's own is the prompt, header and all
        prompt.Text.Should().Be("X:1<n>L:1/8<n>M:4/4<n>K:C<n>CDEF GABc | <|> <|> ");
        prompt.IsAlreadyHeard.Should().BeTrue();
    }

    [Fact]
    public void model_native_text_wins_over_an_intent_that_asks_for_two_voices()
    {
        //Arrange - the escape hatch is the escape hatch: a caller who writes the notation itself
        //is not asking for an opening to be written for it
        var request = new MusicRequest
        {
            ModelNativeText = "X:1\nL:1/8\nM:4/4\nK:C\nCDEF GABc | ",
            Intent = new MusicIntent { VoiceCount = 2, Key = "A", Mode = MusicMode.Minor }
        };

        //Act
        var prompt = MuPTPrompt.For(request, new MuPTGeneratorOptions(), null, null);

        //Assert
        prompt.Text.Should().Be("X:1<n>L:1/8<n>M:4/4<n>K:C<n>CDEF GABc | ");
    }

    [Fact]
    public void a_character_word_fills_in_the_tempo_and_the_mode_the_header_is_written_with()
    {
        //Arrange
        var request = new MusicRequest { Intent = new MusicIntent { Key = "D" } };
        request.Intent.CharacterWords.Add("mournful");

        //Act - the words are read at the generator's edge, and the header sees an ordinary intent
        var applied = MusicCharacterWords.ApplyTo(request, "MuPT");

        //Assert - "mournful" is 60 quarter notes a minute, in the minor
        MuPTHeader.For(applied.Intent, null).ToPrompt().Should()
            .Be("X:1<n>L:1/8<n>Q:1/4=60<n>M:4/4<n>K:Dm<n>");
    }

    [Fact]
    public void the_eight_audition_prompts_are_the_openings_the_duets_came_from()
    {
        //Arrange
        var prompts = AuditionPrompts();

        //Assert - two of the eight carry a two-part opening, which is what makes a duet
        prompts.Should().HaveCount(8);
        prompts[6].Prompt.Should().Contain("<|>");
        prompts[7].Prompt.Should().Contain("<|>");
    }

    // --- the eight prompts, exactly as the listening sessions sent them -------------------------

    private static IReadOnlyList<AuditionPrompt> AuditionPrompts() =>
        new[]
        {
            Prompt("reel-Gmin", 1, 8, 200.0, 4, 4, "Gmin", "|:\"Gm\" BGdB"),
            Prompt("jig-D", 3, 8, 110.0, 6, 8, "D", "|:\"D\" fed cBA"),
            Prompt("waltz-Am", 1, 4, 120.0, 3, 4, "Am", "|:\"Am\" A2 c2 e2"),
            Prompt("air-Dmix", 1, 4, 70.0, 4, 4, "Dmix", "|:\"D\" A3 B A2 FD"),
            Prompt("hornpipe-G", 1, 4, 150.0, 4, 4, "G", "|:\"G\" G>A B>c d>B G>B"),
            Prompt("open-C", 1, 4, 100.0, 4, 4, "C", ""),
            Prompt("duet-C", 1, 4, 96.0, 4, 4, "C",
                "\"C\" c2 e2 g2 e2 | C,4 G,4 | <|> <|> \"G\" d2 g2 b2 g2 | G,4 D4 | <|> <|> "),
            Prompt("duet-Am-waltz", 1, 4, 108.0, 3, 4, "Am",
                "\"Am\" e2 a2 c'2 | A,2 [CE]2 [CE]2 | <|> <|> \"Dm\" d2 f2 a2 | " +
                "D,2 [FA]2 [FA]2 | <|> <|> ")
        };

    private static AuditionPrompt Prompt(string name, int beatNumerator, int beatDenominator,
        double perMinute, int beatsPerBar, int beatNoteValue, string key, string opening)
    {
        var header = new MuPTHeader
        {
            UnitNoteLength = new MusicNoteLength(1, 8),
            TempoBeat = new MusicNoteLength(beatNumerator, beatDenominator),
            TempoPerMinute = perMinute,
            Meter = new MusicMeter(beatsPerBar, beatNoteValue),
            Key = key,
            Opening = opening
        };

        var lines = new List<string>
        {
            "X:1",
            "L:1/8",
            "Q:" + beatNumerator + "/" + beatDenominator + "=" + (int)perMinute,
            "M:" + beatsPerBar + "/" + beatNoteValue,
            "K:" + key
        };

        return new AuditionPrompt(name, header, string.Join("<n>", lines) + "<n>" + opening);
    }

    private sealed class AuditionPrompt
    {
        public AuditionPrompt(string name, MuPTHeader header, string prompt)
        {
            Name = name;
            Header = header;
            Prompt = prompt;
        }

        public string Name { get; }

        public MuPTHeader Header { get; }

        public string Prompt { get; }
    }
}
