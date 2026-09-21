using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The check that makes "never silently ignored" true: what a request relies on, what a generator
/// honours, and the refusal that names the difference.
/// </summary>
public class MusicGeneratorCapabilitiesTests
{
    [Fact]
    public void FeaturesUsedBy_a_bare_request_is_none()
        => MusicGeneratorCapabilities.FeaturesUsedBy(new MusicRequest())
            .Should().Be(MusicRequestFeatures.None);

    [Fact]
    public void FeaturesUsedBy_ignores_the_tick_resolution_and_the_pacing_setting()
    {
        //Arrange
        var request = new MusicRequest { TicksPerQuarterNote = 960, PaceInRealTime = false };

        //Act
        var used = MusicGeneratorCapabilities.FeaturesUsedBy(request);

        //Assert
        used.Should().Be(MusicRequestFeatures.None);
    }

    [Theory]
    [MemberData(nameof(EveryRequestPart))]
    public void FeaturesUsedBy_sees_each_part_that_is_set(string label,
        MusicRequestFeatures expected)
    {
        //Arrange
        var request = RequestUsing(label);

        //Act
        var used = MusicGeneratorCapabilities.FeaturesUsedBy(request);

        //Assert
        used.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(EveryRequestPart))]
    public void EnsureHonoured_refuses_each_part_the_generator_does_not_honour_by_name(string label,
        MusicRequestFeatures expected)
    {
        //Arrange
        var generator = new StubMusicGenerator("Picky");
        var request = RequestUsing(label);
        var partName = MusicGeneratorCapabilities.NamesOf(expected)[0];

        //Act
        Action act = () => MusicGeneratorCapabilities.EnsureHonoured(generator, request);

        //Assert
        var thrown = act.Should().Throw<MusicRequestNotHonouredException>().Which;
        thrown.Message.Should().Contain(partName);
        thrown.Message.Should().Contain("Picky");
        thrown.GeneratorName.Should().Be("Picky");
        thrown.UnhonouredFeatures.Should().Be(expected);
    }

    [Fact]
    public void EnsureHonoured_takes_a_bare_request_even_when_the_generator_honours_nothing()
    {
        //Arrange
        var generator = new StubMusicGenerator("Picky");
        Action act = () => MusicGeneratorCapabilities.EnsureHonoured(generator, new MusicRequest());

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureHonoured_takes_a_part_the_generator_declares()
    {
        //Arrange
        var generator = new StubMusicGenerator("Seeded", MusicRequestFeatures.Seed);
        var request = new MusicRequest { Seed = 29 };
        Action act = () => MusicGeneratorCapabilities.EnsureHonoured(generator, request);

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureHonoured_names_every_part_it_refuses_at_once()
    {
        //Arrange
        var generator = new StubMusicGenerator("Picky", MusicRequestFeatures.Seed);
        var request = new MusicRequest { Seed = 29, Text = "something calm", Primer = new MidiEventCollection(1, 480) };

        //Act
        Action act = () => MusicGeneratorCapabilities.EnsureHonoured(generator, request);

        //Assert
        var thrown = act.Should().Throw<MusicRequestNotHonouredException>().Which;
        thrown.Message.Should().Be(
            "The music generator 'Picky' does not honour these parts of the request: free text, " +
            "a primer. Leave them unset, or use a generator that honours them.");
    }

    [Fact]
    public void UnhonouredFeatures_is_none_when_the_generator_honours_everything_asked_for()
    {
        //Arrange
        var generator = new StubMusicGenerator("Everything",
            MusicRequestFeatures.Seed | MusicRequestFeatures.Tempo);
        var request = new MusicRequest { Seed = 1, Intent = new MusicIntent { BeatsPerMinute = 92.0 } };

        //Act
        var unhonoured = MusicGeneratorCapabilities.UnhonouredFeatures(generator, request);

        //Assert
        unhonoured.Should().Be(MusicRequestFeatures.None);
    }

    [Fact]
    public void NamesOf_none_names_nothing()
        => MusicGeneratorCapabilities.NamesOf(MusicRequestFeatures.None).Should().BeEmpty();

    [Fact]
    public void NamesOf_names_the_parts_in_the_order_they_are_declared()
        => MusicGeneratorCapabilities
            .NamesOf(MusicRequestFeatures.Continuation | MusicRequestFeatures.FreeText)
            .Should().Equal("free text", "a continuation");

    [Fact]
    public void NamesOf_names_a_drum_kit_between_a_target_length_and_the_instrument_hints()
        => MusicGeneratorCapabilities
            .NamesOf(MusicRequestFeatures.TargetLength | MusicRequestFeatures.DrumKit |
                     MusicRequestFeatures.InstrumentHints)
            .Should().Equal("target length", "a drum kit", "instrument hints");

    [Fact]
    public void FeaturesUsedBy_rejects_a_null_request()
    {
        //Arrange
        Action act = () => MusicGeneratorCapabilities.FeaturesUsedBy(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UnhonouredFeatures_rejects_a_null_generator()
    {
        //Arrange
        Action act = () => MusicGeneratorCapabilities.UnhonouredFeatures(null, new MusicRequest());

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Every part of a request, one at a time, with the flag it stands for.</summary>
    /// <returns>A label for the part, and the feature it sets.</returns>
    public static TheoryData<string, MusicRequestFeatures> EveryRequestPart() =>
        new TheoryData<string, MusicRequestFeatures>
        {
            { "text", MusicRequestFeatures.FreeText },
            { "model-native text", MusicRequestFeatures.ModelNativeText },
            { "primer", MusicRequestFeatures.Primer },
            { "key", MusicRequestFeatures.Key },
            { "mode", MusicRequestFeatures.Key },
            { "meter", MusicRequestFeatures.Meter },
            { "unit note length", MusicRequestFeatures.UnitNoteLength },
            { "tempo", MusicRequestFeatures.Tempo },
            { "character words", MusicRequestFeatures.CharacterWords },
            { "voice count", MusicRequestFeatures.VoiceCount },
            { "target length", MusicRequestFeatures.TargetLength },
            { "drum kit", MusicRequestFeatures.DrumKit },
            { "no drum kit", MusicRequestFeatures.DrumKit },
            { "instrument hints", MusicRequestFeatures.InstrumentHints },
            { "seed", MusicRequestFeatures.Seed },
            { "temperature", MusicRequestFeatures.SamplingControls },
            { "top-k", MusicRequestFeatures.SamplingControls },
            { "top-p", MusicRequestFeatures.SamplingControls },
            { "repetition penalty", MusicRequestFeatures.SamplingControls },
            { "maximum events", MusicRequestFeatures.MaximumEvents },
            { "thread count", MusicRequestFeatures.InferenceThreadCount },
            { "continuation", MusicRequestFeatures.Continuation }
        };

    private static MusicRequest RequestUsing(string label)
    {
        var request = new MusicRequest();

        switch (label)
        {
            case "text":
                request.Text = "something calm for a forest at dusk";
                break;
            case "model-native text":
                request.ModelNativeText = "X:1\nK:C\nCDEF|";
                break;
            case "primer":
                request.Primer = new MidiEventCollection(1, 480);
                break;
            case "key":
                request.Intent = new MusicIntent { Key = "A" };
                break;
            case "mode":
                request.Intent = new MusicIntent { Mode = MusicMode.Mixolydian };
                break;
            case "meter":
                request.Intent = new MusicIntent { Meter = new MusicMeter(3, 4) };
                break;
            case "unit note length":
                request.Intent = new MusicIntent { UnitNoteLength = MusicNoteLength.EighthNote };
                break;
            case "tempo":
                request.Intent = new MusicIntent { BeatsPerMinute = 92.0 };
                break;
            case "character words":
                request.Intent = new MusicIntent();
                request.Intent.CharacterWords.Add("sparse");
                break;
            case "voice count":
                request.Intent = new MusicIntent { VoiceCount = 2 };
                break;
            case "target length":
                request.Intent = new MusicIntent { TargetLength = TimeSpan.FromMinutes(6.0) };
                break;
            case "drum kit":
                request.Intent = new MusicIntent { DrumKit = 24 };
                break;
            case "no drum kit":
                request.Intent = new MusicIntent { DrumKit = MusicIntent.NoDrumKit };
                break;
            case "instrument hints":
                request.InstrumentHints.Add(GeneralMidiProgram.Celesta);
                break;
            case "seed":
                request.Seed = 29;
                break;
            case "temperature":
                request.Controls.Temperature = 1.1;
                break;
            case "top-k":
                request.Controls.TopK = 20;
                break;
            case "top-p":
                request.Controls.TopP = 0.5;
                break;
            case "repetition penalty":
                request.Controls.RepetitionPenalty = 1.1;
                break;
            case "maximum events":
                request.Controls.MaximumEvents = 512;
                break;
            case "thread count":
                request.InferenceThreadCount = 2;
                break;
            case "continuation":
                request.Continuation = new MusicContinuation();
                break;
            default:
                throw new ArgumentException($"There is no request part called '{label}'.", nameof(label));
        }

        return request;
    }
}
