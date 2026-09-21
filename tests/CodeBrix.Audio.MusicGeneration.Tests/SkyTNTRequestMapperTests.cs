using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A request turned into the model runner's own options, field by field - no model needed.
/// </summary>
public class SkyTNTRequestMapperTests
{
    private const string GeneratorName = "SkyTNT";

    [Fact]
    public void the_music_sampling_defaults_are_what_a_bare_request_sends()
    {
        //Arrange
        var request = new MusicRequest();

        //Act
        var mapped = Map(request);

        //Assert - the request's own music defaults, not the model runner's text ones
        mapped.Temperature.Should().Be(MusicGenerationControls.DefaultTemperature);
        mapped.TopK.Should().Be(MusicGenerationControls.DefaultTopK);
        mapped.TopP.Should().Be(MusicGenerationControls.DefaultTopP);
    }

    [Fact]
    public void the_sampling_controls_are_carried_across()
    {
        //Arrange
        var request = new MusicRequest();
        request.Controls.Temperature = 1.1;
        request.Controls.TopK = 12;
        request.Controls.TopP = 0.75;

        //Act
        var mapped = Map(request);

        //Assert
        mapped.Temperature.Should().Be(1.1);
        mapped.TopK.Should().Be(12);
        mapped.TopP.Should().Be(0.75);
    }

    [Fact]
    public void top_k_of_nothing_keeps_every_candidate_rather_than_becoming_greedy()
    {
        //Arrange - zero means "no top-k" in a request, and one means greedy to this model
        var request = new MusicRequest();
        request.Controls.TopK = 0;

        //Act
        var mapped = Map(request);

        //Assert
        mapped.TopK.Should().BeGreaterThan(1);
    }

    [Fact]
    public void a_repetition_penalty_is_refused_by_name()
    {
        //Arrange
        var request = new MusicRequest();
        request.Controls.RepetitionPenalty = 1.1;

        //Act
        Action act = () => Map(request);

        //Assert
        var refusal = act.Should().Throw<MusicRequestNotHonouredException>().Which;
        refusal.GeneratorName.Should().Be(GeneratorName);
        refusal.UnhonouredFeatures.Should().Be(MusicRequestFeatures.SamplingControls);
        refusal.Message.Should().Contain("repetition penalty");
    }

    [Fact]
    public void the_event_cap_comes_from_the_request_when_it_names_one()
    {
        //Arrange
        var request = new MusicRequest();
        request.Controls.MaximumEvents = 250;

        //Act and Assert
        Map(request).MaximumEvents.Should().Be(250);
    }

    [Fact]
    public void the_event_cap_comes_from_the_generator_when_the_request_names_none() =>
        Map(new MusicRequest()).MaximumEvents
            .Should().Be(SkyTNTGeneratorOptions.DefaultMaximumEventsPerPass);

    [Fact]
    public void the_seed_is_carried_across()
    {
        //Arrange
        var request = new MusicRequest { Seed = 20260920 };

        //Act and Assert
        Map(request).Seed.Should().Be(20260920L);
    }

    [Fact]
    public void the_instrument_hints_become_the_instruments_in_order()
    {
        //Arrange - the embedded piece's own recipe
        var request = new MusicRequest();
        request.InstrumentHints.Add(GeneralMidiProgram.AcousticGrandPiano);
        request.InstrumentHints.Add(GeneralMidiProgram.Violin);
        request.InstrumentHints.Add(GeneralMidiProgram.Flute);

        //Act
        var mapped = Map(request);

        //Assert
        mapped.Instruments.Should().Equal(0, 40, 73);
    }

    [Fact]
    public void no_instrument_hints_leaves_the_model_to_choose()
    {
        //Arrange and Act
        var mapped = Map(new MusicRequest());

        //Assert - naming instruments also stops the model choosing, so naming none says nothing
        mapped.Instruments.Should().BeNull();
    }

    [Fact]
    public void the_drum_kit_comes_from_the_generator_options()
    {
        //Arrange
        var options = new SkyTNTGeneratorOptions { DrumKit = 0 };

        //Act
        var mapped = SkyTNTRequestMapper.ToGenerationOptions(new MusicRequest(), options, null,
            GeneratorName);

        //Assert
        mapped.DrumKit.Should().Be(0);
    }

    [Fact]
    public void the_tempo_is_carried_across()
    {
        //Arrange
        var request = new MusicRequest { Intent = new MusicIntent { BeatsPerMinute = 140.0 } };

        //Act and Assert
        Map(request).BeatsPerMinute.Should().Be(140);
    }

    [Fact]
    public void a_tempo_this_model_cannot_write_is_refused_by_name()
    {
        //Arrange
        var request = new MusicRequest { Intent = new MusicIntent { BeatsPerMinute = 500.0 } };

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<MusicRequestNotHonouredException>().Which
            .UnhonouredFeatures.Should().Be(MusicRequestFeatures.Tempo);
    }

    [Fact]
    public void the_metre_is_carried_across()
    {
        //Arrange
        var request = new MusicRequest
        {
            Intent = new MusicIntent { Meter = new MusicMeter(6, 8) }
        };

        //Act
        var mapped = Map(request);

        //Assert
        mapped.TimeSignatureNumerator.Should().Be(6);
        mapped.TimeSignatureDenominator.Should().Be(8);
    }

    [Fact]
    public void a_metre_this_model_cannot_write_is_refused_by_name()
    {
        //Arrange - thirty-second notes are not one of the four lower numbers it writes
        var request = new MusicRequest
        {
            Intent = new MusicIntent { Meter = new MusicMeter(4, 32) }
        };

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<MusicRequestNotHonouredException>().Which
            .UnhonouredFeatures.Should().Be(MusicRequestFeatures.Meter);
    }

    [Fact]
    public void a_major_key_becomes_its_signature()
    {
        //Arrange
        var request = new MusicRequest
        {
            Intent = new MusicIntent { Key = "D", Mode = MusicMode.Major }
        };

        //Act
        var mapped = Map(request);

        //Assert
        mapped.KeySignatureSharpsOrFlats.Should().Be(2);
        mapped.KeySignatureIsMinor.Should().BeFalse();
    }

    [Fact]
    public void a_minor_key_becomes_its_signature()
    {
        //Arrange
        var request = new MusicRequest
        {
            Intent = new MusicIntent { Key = "A", Mode = MusicMode.Minor }
        };

        //Act
        var mapped = Map(request);

        //Assert
        mapped.KeySignatureSharpsOrFlats.Should().Be(0);
        mapped.KeySignatureIsMinor.Should().BeTrue();
    }

    [Fact]
    public void a_modal_key_is_read_as_the_minor_or_major_it_is_nearest()
    {
        //Arrange
        var request = new MusicRequest
        {
            Intent = new MusicIntent { Key = "D", Mode = MusicMode.Dorian }
        };

        //Act
        var mapped = Map(request);

        //Assert - a key signature has only two shapes, so a modal request gets the nearer one
        mapped.KeySignatureIsMinor.Should().BeTrue();
        mapped.KeySignatureSharpsOrFlats.Should().Be(-1);
    }

    [Fact]
    public void a_key_this_model_has_no_signature_for_is_refused_by_name()
    {
        //Arrange
        var request = new MusicRequest
        {
            Intent = new MusicIntent { Key = "H", Mode = MusicMode.Major }
        };

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<MusicRequestNotHonouredException>().Which
            .UnhonouredFeatures.Should().Be(MusicRequestFeatures.Key);
    }

    [Fact]
    public void a_prompt_replaces_every_description_of_a_piece()
    {
        //Arrange - what the engine hands over at a seam: the base request plus a continuation
        var request = new MusicRequest { Intent = new MusicIntent { BeatsPerMinute = 140.0 } };
        request.InstrumentHints.Add(GeneralMidiProgram.Violin);

        var prompt = new CodeBrix.Ollama.ModelRunner.MidiScore(480,
            new[] { CodeBrix.Ollama.ModelRunner.MidiEvent.Note(0L, 0, 0, 60, 100, 480L) });

        //Act
        var mapped = SkyTNTRequestMapper.ToGenerationOptions(request,
            new SkyTNTGeneratorOptions(), prompt, GeneratorName);

        //Assert - the model refuses both at once, and the description is in the music anyway
        mapped.Prompt.Should().BeSameAs(prompt);
        mapped.Instruments.Should().BeNull();
        mapped.BeatsPerMinute.Should().BeNull();
        mapped.DrumKit.Should().BeNull();
    }

    [Fact]
    public void a_described_piece_gets_its_own_instrument_and_tempo_events()
    {
        //Arrange - naming instruments also stops the model writing an instrument change of its
        //own, so the ones the description sets up are the only ones there will ever be
        var request = new MusicRequest { Intent = new MusicIntent { BeatsPerMinute = 140.0 } };
        request.InstrumentHints.Add(GeneralMidiProgram.Violin);

        //Act
        var mapped = Map(request);

        //Assert
        mapped.IncludePromptEvents.Should().BeTrue();
    }

    [Fact]
    public void a_prompt_never_re_emits_its_own_events()
    {
        //Arrange
        var prompt = new CodeBrix.Ollama.ModelRunner.MidiScore(480,
            new[] { CodeBrix.Ollama.ModelRunner.MidiEvent.Note(0L, 0, 0, 60, 100, 480L) });

        //Act
        var mapped = SkyTNTRequestMapper.ToGenerationOptions(new MusicRequest(),
            new SkyTNTGeneratorOptions(), prompt, GeneratorName);

        //Assert
        mapped.IncludePromptEvents.Should().BeFalse();
    }

    [Fact]
    public void the_continuation_program_for_channel_ten_is_the_drum_kit_in_force()
    {
        //Arrange
        var request = new MusicRequest { Continuation = new MusicContinuation() };
        request.Continuation.ChannelPrograms[10] = (GeneralMidiProgram)32;

        //Act
        var kit = SkyTNTRequestMapper.DrumKitOf(request, new SkyTNTGeneratorOptions { DrumKit = 0 });

        //Assert - what the music is really using wins over what the generator was built with
        kit.Should().Be(32);
    }

    [Fact]
    public void the_request_s_own_drum_kit_wins_over_everything_else()
    {
        //Arrange - the music has been using one kit and the request asks for another
        var request = new MusicRequest
        {
            Intent = new MusicIntent { DrumKit = 24 },
            Continuation = new MusicContinuation()
        };

        request.Continuation.ChannelPrograms[10] = (GeneralMidiProgram)32;

        //Act
        var kit = SkyTNTRequestMapper.DrumKitOf(request, new SkyTNTGeneratorOptions { DrumKit = 0 });

        //Assert
        kit.Should().Be(24);
    }

    [Fact]
    public void a_request_that_asks_for_no_percussion_gets_none()
    {
        //Arrange
        var request = new MusicRequest
        {
            Intent = new MusicIntent { DrumKit = MusicIntent.NoDrumKit }
        };

        //Act - even though the generator was built with a kit
        var kit = SkyTNTRequestMapper.DrumKitOf(request, new SkyTNTGeneratorOptions { DrumKit = 0 });

        //Assert
        kit.Should().BeNull();
    }

    [Fact]
    public void the_drum_kit_a_request_asks_for_reaches_the_model_s_own_options()
    {
        //Arrange
        var request = new MusicRequest { Intent = new MusicIntent { DrumKit = 24 } };
        request.InstrumentHints.Add(GeneralMidiProgram.SynthBass1);

        //Act
        var mapped = Map(request);

        //Assert
        mapped.DrumKit.Should().Be(24);
        mapped.Instruments.Should().Equal(38);
    }

    [Fact]
    public void a_request_that_says_nothing_takes_the_generator_s_own_kit()
    {
        //Act
        var kit = SkyTNTRequestMapper.DrumKitOf(new MusicRequest(),
            new SkyTNTGeneratorOptions { DrumKit = 8 });

        //Assert
        kit.Should().Be(8);
    }

    [Fact]
    public void controller_events_are_off_unless_the_generator_was_built_to_allow_them()
    {
        //Act
        var plain = Map(new MusicRequest());
        var allowed = SkyTNTRequestMapper.ToGenerationOptions(new MusicRequest(),
            new SkyTNTGeneratorOptions { AllowControlChange = true }, null, GeneratorName);

        //Assert - a pass spent on controller events at tick 0 is a pass with no music in it
        plain.AllowControlChange.Should().BeFalse();
        allowed.AllowControlChange.Should().BeTrue();
    }

    [Fact]
    public void a_character_word_becomes_a_tempo_and_a_key_signature()
    {
        //Arrange
        var request = new MusicRequest { Intent = new MusicIntent { Key = "D" } };
        request.Intent.CharacterWords.Add("dark");

        //Act - the words are read at the generator's edge, and the mapper sees an ordinary intent
        var mapped = Map(MusicCharacterWords.ApplyTo(request, GeneratorName));

        //Assert - "dark" is 76 quarter notes a minute, in the minor: D minor is one flat
        mapped.BeatsPerMinute.Should().Be(76);
        mapped.KeySignatureSharpsOrFlats.Should().Be(-1);
        mapped.KeySignatureIsMinor.Should().BeTrue();
    }

    [Fact]
    public void SignatureOf_reads_a_tonic_written_with_its_mode_beside_it()
    {
        //Act
        var signature = SkyTNTRequestMapper.SignatureOf("Bb major", MusicMode.Major, out var isMinor);

        //Assert
        signature.Should().Be(-2);
        isMinor.Should().BeFalse();
    }

    private static CodeBrix.Ollama.ModelRunner.MidiGenerationOptions Map(MusicRequest request) =>
        SkyTNTRequestMapper.ToGenerationOptions(request, new SkyTNTGeneratorOptions(), null,
            GeneratorName);
}
