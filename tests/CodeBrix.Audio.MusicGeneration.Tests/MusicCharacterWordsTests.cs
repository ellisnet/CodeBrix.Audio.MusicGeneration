using System;
using System.Collections.Generic;
using CodeBrix.Audio.MusicGeneration.Generation;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE CHARACTER-WORD TABLE: which words mean something a model can be asked for, what each of
/// them means, and what happens to a word that means nothing.
/// </summary>
public class MusicCharacterWordsTests
{
    [Theory]
    [InlineData("slow", 60.0)]
    [InlineData("calm", 66.0)]
    [InlineData("gentle", 72.0)]
    [InlineData("moderate", 100.0)]
    [InlineData("lively", 132.0)]
    [InlineData("driving", 138.0)]
    [InlineData("fast", 168.0)]
    public void a_tempo_word_names_a_tempo_and_no_mode(string word, double beatsPerMinute)
    {
        //Act
        var character = MusicCharacterWords.Find(word);

        //Assert
        character.BeatsPerMinute.Should().Be(beatsPerMinute);
        character.Mode.Should().BeNull();
    }

    [Theory]
    [InlineData("sad", 72.0, MusicMode.Minor)]
    [InlineData("dark", 76.0, MusicMode.Minor)]
    [InlineData("bright", 132.0, MusicMode.Major)]
    [InlineData("triumphant", 112.0, MusicMode.Major)]
    public void a_word_may_name_both_a_tempo_and_a_mode(string word, double beatsPerMinute,
        MusicMode mode)
    {
        //Act
        var character = MusicCharacterWords.Find(word);

        //Assert
        character.BeatsPerMinute.Should().Be(beatsPerMinute);
        character.Mode.Should().Be(mode);
    }

    [Theory]
    [InlineData("major", MusicMode.Major)]
    [InlineData("minor", MusicMode.Minor)]
    [InlineData("dorian", MusicMode.Dorian)]
    [InlineData("mixolydian", MusicMode.Mixolydian)]
    [InlineData("locrian", MusicMode.Locrian)]
    public void a_mode_names_itself_and_no_tempo(string word, MusicMode mode)
    {
        //Act
        var character = MusicCharacterWords.Find(word);

        //Assert
        character.Mode.Should().Be(mode);
        character.BeatsPerMinute.Should().BeNull();
    }

    [Fact]
    public void a_word_is_matched_without_regard_to_case_or_space() =>
        MusicCharacterWords.Find("  GeNtLe ").BeatsPerMinute.Should().Be(72.0);

    [Fact]
    public void a_word_that_names_nothing_musical_is_not_known()
    {
        //Assert
        MusicCharacterWords.Find("menacing").Should().BeNull();
        MusicCharacterWords.IsKnown("menacing").Should().BeFalse();
        MusicCharacterWords.IsKnown("gentle").Should().BeTrue();
    }

    [Fact]
    public void every_known_word_can_be_looked_up_and_names_at_least_one_thing()
    {
        //Arrange
        var empty = new List<string>();

        //Act
        foreach (var word in MusicCharacterWords.Known)
        {
            var character = MusicCharacterWords.Find(word);

            if (character == null ||
                (!character.BeatsPerMinute.HasValue && !character.Mode.HasValue))
            {
                empty.Add(word);
            }
        }

        //Assert
        empty.Should().BeEmpty();
        MusicCharacterWords.Known.Should().BeInAscendingOrder();
    }

    [Fact]
    public void the_first_word_that_names_a_thing_settles_it()
    {
        //Act - both name a tempo, and only the second names a mode
        var character = MusicCharacterWords.Resolve(new[] { "gentle", "sad" }, "AnyGenerator");

        //Assert
        character.BeatsPerMinute.Should().Be(72.0);
        character.Mode.Should().Be(MusicMode.Minor);
        character.Words.Should().Equal("gentle", "sad");
    }

    [Fact]
    public void an_unknown_word_is_refused_by_name()
    {
        //Act
        Action act = () =>
            MusicCharacterWords.Resolve(new[] { "gentle", "menacing" }, "MyGenerator");

        //Assert
        var thrown = act.Should().Throw<MusicRequestNotHonouredException>().Which;
        thrown.Message.Should().Contain("MyGenerator").And.Contain("'menacing'")
            .And.Contain("MusicCharacterWords.Known");
        thrown.UnhonouredFeatures.Should().Be(MusicRequestFeatures.CharacterWords);
        thrown.GeneratorName.Should().Be("MyGenerator");
    }

    [Fact]
    public void no_words_at_all_mean_nothing_to_resolve() =>
        MusicCharacterWords.Resolve(null, "MyGenerator").Should().BeNull();

    [Fact]
    public void a_request_with_no_words_is_handed_back_unchanged()
    {
        //Arrange
        var request = new MusicRequest { Intent = new MusicIntent { BeatsPerMinute = 90.0 } };

        //Act
        var applied = MusicCharacterWords.ApplyTo(request, "MyGenerator");

        //Assert - the same instance, so nothing is copied for nothing
        applied.Should().BeSameAs(request);
    }

    [Fact]
    public void the_words_are_written_into_a_copy_of_the_intent()
    {
        //Arrange
        var request = new MusicRequest { Intent = new MusicIntent() };
        request.Intent.CharacterWords.Add("melancholy");

        //Act
        var applied = MusicCharacterWords.ApplyTo(request, "MyGenerator");

        //Assert
        applied.Should().NotBeSameAs(request);
        applied.Intent.BeatsPerMinute.Should().Be(76.0);
        applied.Intent.Mode.Should().Be(MusicMode.Minor);
        applied.Intent.CharacterWords.Should().BeEmpty();

        //... and the caller's own request is untouched
        request.Intent.BeatsPerMinute.Should().BeNull();
        request.Intent.CharacterWords.Should().Equal("melancholy");
    }

    [Fact]
    public void what_the_request_says_outright_wins_over_what_a_word_would_say()
    {
        //Arrange
        var request = new MusicRequest
        {
            Intent = new MusicIntent { BeatsPerMinute = 90.0, Mode = MusicMode.Dorian }
        };

        request.Intent.CharacterWords.Add("fast");
        request.Intent.CharacterWords.Add("minor");

        //Act
        var applied = MusicCharacterWords.ApplyTo(request, "MyGenerator");

        //Assert
        applied.Intent.BeatsPerMinute.Should().Be(90.0);
        applied.Intent.Mode.Should().Be(MusicMode.Dorian);
    }

    [Fact]
    public void a_character_describes_itself_for_a_log() =>
        MusicCharacterWords.Find("sad").ToString().Should().Be("sad: 72 bpm, Minor");
}
