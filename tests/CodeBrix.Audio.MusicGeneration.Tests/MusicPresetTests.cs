using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE PRESETS: the eight prompts the MuPT listening session was run on, and the three accepted
/// electronica starting points for the other model.
/// </summary>
/// <remarks>
/// NOTHING HERE LOADS A MODEL. A generator is built over a path that holds nothing, which is
/// enough to ask it what it honours - building one loads nothing at all - and the capability check
/// is the same one a session makes before a note is generated. The class reads the two
/// process-wide registries, so it runs in the non-parallel collection with everything else that
/// touches them.
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MusicPresetTests
{
    private static readonly string NoModelHere =
        Path.Combine(Path.GetTempPath(), "codebrix-musicgen-no-model-here");

    [Fact]
    public void the_eight_MuPT_presets_are_the_prompts_of_the_listening_session() =>
        Names(MuPTPresets.All).Should().Equal("ReelInGMinor", "JigInD", "WaltzInAMinor",
            "AirInDMixolydian", "HornpipeInG", "OpenInC", "DuetInC", "WaltzDuetInAMinor");

    [Fact]
    public void the_three_SkyTNT_presets_are_the_electronica_ones() =>
        Names(SkyTNTPresets.All).Should().Equal("FourOnTheFloor", "ClubArrangement",
            "AmbientElectronica");

    [Fact]
    public void every_MuPT_preset_is_carried_as_the_model_s_own_text()
    {
        //Arrange
        var wrong = new List<string>();

        //Act
        foreach (var preset in MuPTPresets.All)
        {
            var request = preset.CreateRequest();

            if (string.IsNullOrWhiteSpace(request.ModelNativeText) ||
                !request.ModelNativeText.StartsWith("X:1\nL:1/8\nQ:", StringComparison.Ordinal))
            {
                wrong.Add(preset.Name);
            }
        }

        //Assert - every one of them is a header in the model card's own order, and an opening
        wrong.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ReelInGMinor", "Q:1/8=200", "M:4/4", "K:Gmin")]
    [InlineData("JigInD", "Q:3/8=110", "M:6/8", "K:D")]
    [InlineData("WaltzInAMinor", "Q:1/4=120", "M:3/4", "K:Am")]
    [InlineData("AirInDMixolydian", "Q:1/4=70", "M:4/4", "K:Dmix")]
    [InlineData("HornpipeInG", "Q:1/4=150", "M:4/4", "K:G")]
    [InlineData("OpenInC", "Q:1/4=100", "M:4/4", "K:C")]
    [InlineData("DuetInC", "Q:1/4=96", "M:4/4", "K:C")]
    [InlineData("WaltzDuetInAMinor", "Q:1/4=108", "M:3/4", "K:Am")]
    public void each_MuPT_preset_carries_the_header_its_music_was_rated_from(string name,
        string tempo, string meter, string key)
    {
        //Act
        var text = MuPTPresets.Find(name).CreateRequest().ModelNativeText;

        //Assert
        text.Should().Contain(tempo).And.Contain(meter).And.Contain(key);
    }

    [Fact]
    public void only_the_two_duet_presets_open_in_the_merged_form()
    {
        //Arrange
        var merged = new List<string>();

        //Act
        foreach (var preset in MuPTPresets.All)
        {
            if (preset.CreateRequest().ModelNativeText.Contains("<|>"))
            {
                merged.Add(preset.Name);
            }
        }

        //Assert - a single line comes back as melody only, so a duet needs two parts in its
        //opening; these are the two whose music was rated highest
        merged.Should().Equal("DuetInC", "WaltzDuetInAMinor");
    }

    [Theory]
    [InlineData("WaltzInAMinor", BuiltInRenditions.MelodyOverPad)]
    [InlineData("AirInDMixolydian", BuiltInRenditions.VibesAndStrings)]
    [InlineData("DuetInC", BuiltInRenditions.HarpAndCello)]
    [InlineData("WaltzDuetInAMinor", BuiltInRenditions.AmbientDuet)]
    public void a_preset_whose_music_was_rated_through_a_voicing_suggests_it(string name,
        string rendition)
    {
        //Act
        var suggested = MuPTPresets.Find(name).SuggestedRendition;

        //Assert
        suggested.Should().Be(rendition);
        MusicRenditionRegistry.IsRegistered(suggested).Should().BeTrue();
    }

    [Fact]
    public void a_preset_no_voicing_was_rated_for_suggests_none()
    {
        //Assert - the automatic rendition voices it
        MuPTPresets.ReelInGMinor.SuggestedRendition.Should().BeNull();
        MuPTPresets.OpenInC.SuggestedRendition.Should().BeNull();
        SkyTNTPresets.ClubArrangement.SuggestedRendition.Should().BeNull();
    }

    [Fact]
    public void every_accepted_MuPT_and_SkyTNT_preset_is_not_provisional()
    {
        //Arrange
        var provisional = new List<string>();

        //Act
        foreach (var preset in MuPTPresets.All)
        {
            if (preset.IsProvisional)
            {
                provisional.Add(preset.Name);
            }
        }

        foreach (var preset in SkyTNTPresets.All)
        {
            if (preset.IsProvisional)
            {
                provisional.Add(preset.Name);
            }
        }

        //Assert - both families have been accepted in listening sessions
        provisional.Should().BeEmpty();
    }

    [Fact]
    public void the_electronica_presets_ask_for_the_kit_per_request()
    {
        //Act
        var floor = SkyTNTPresets.FourOnTheFloor.CreateRequest();
        var club = SkyTNTPresets.ClubArrangement.CreateRequest();
        var ambient = SkyTNTPresets.AmbientElectronica.CreateRequest();

        //Assert
        floor.Intent.DrumKit.Should().Be(SkyTNTPresets.ElectronicDrumKit);
        club.Intent.DrumKit.Should().Be(SkyTNTPresets.ElectronicDrumKit);
        ambient.Intent.DrumKit.Should().Be(MusicIntent.NoDrumKit);
    }

    [Fact]
    public void the_electronica_presets_name_the_instruments_and_the_tempo()
    {
        //Act
        var floor = SkyTNTPresets.FourOnTheFloor.CreateRequest();
        var club = SkyTNTPresets.ClubArrangement.CreateRequest();
        var ambient = SkyTNTPresets.AmbientElectronica.CreateRequest();

        //Assert
        floor.InstrumentHints.Should().Equal(GeneralMidiProgram.SynthBass1);
        floor.Intent.BeatsPerMinute.Should().Be(126.0);
        floor.Intent.Meter.Should().Be(MusicMeter.CommonTime);

        club.InstrumentHints.Should().Equal(GeneralMidiProgram.SynthBass1,
            GeneralMidiProgram.Lead2Sawtooth, GeneralMidiProgram.Pad2Warm);

        ambient.InstrumentHints.Should().Equal(GeneralMidiProgram.Pad1NewAge,
            GeneralMidiProgram.Pad2Warm, GeneralMidiProgram.Celesta);
        ambient.Intent.BeatsPerMinute.Should().Be(84.0);
    }

    [Fact]
    public void every_MuPT_preset_builds_a_request_its_own_generator_accepts()
    {
        //Arrange
        using var generator = new MuPTMusicGenerator("MuPT", Path.Combine(NoModelHere, "x.gguf"));
        var refused = new List<string>();

        //Act
        foreach (var preset in MuPTPresets.All)
        {
            var unhonoured = MusicGeneratorCapabilities.UnhonouredFeatures(generator,
                preset.CreateRequest());

            if (unhonoured != MusicRequestFeatures.None)
            {
                refused.Add(preset.Name + ": " + unhonoured);
            }
        }

        //Assert
        refused.Should().BeEmpty();
    }

    [Fact]
    public void every_SkyTNT_preset_builds_a_request_its_own_generator_accepts()
    {
        //Arrange
        using var generator = new SkyTNTMusicGenerator("SkyTNT", NoModelHere);
        var refused = new List<string>();

        //Act
        foreach (var preset in SkyTNTPresets.All)
        {
            var unhonoured = MusicGeneratorCapabilities.UnhonouredFeatures(generator,
                preset.CreateRequest());

            if (unhonoured != MusicRequestFeatures.None)
            {
                refused.Add(preset.Name + ": " + unhonoured);
            }
        }

        //Assert
        refused.Should().BeEmpty();
    }

    [Fact]
    public void the_embedded_replay_refuses_every_preset_by_name()
    {
        //Arrange - a replay plays a piece that is already written, so it can honour none of this
        var replay = MusicGeneratorRegistry.Resolve(EmbeddedReplay.Midi);
        var quietlyAccepted = new List<string>();

        //Act
        foreach (var preset in Every())
        {
            Action act = () =>
                MusicGeneratorCapabilities.EnsureHonoured(replay, preset.CreateRequest());

            var thrown = act.Should().Throw<MusicRequestNotHonouredException>().Which;

            if (!thrown.Message.Contains(EmbeddedReplay.Midi, StringComparison.Ordinal))
            {
                quietlyAccepted.Add(preset.Name);
            }
        }

        //Assert
        quietlyAccepted.Should().BeEmpty();
    }

    [Fact]
    public void the_replay_names_what_it_will_not_act_on()
    {
        //Arrange
        var replay = MusicGeneratorRegistry.Resolve(EmbeddedReplay.Midi);

        //Act
        Action act = () => MusicGeneratorCapabilities.EnsureHonoured(replay,
            SkyTNTPresets.AmbientElectronica.CreateRequest());

        //Assert
        act.Should().Throw<MusicRequestNotHonouredException>().Which
            .Message.Should().Contain("metre").And.Contain("tempo").And.Contain("a drum kit")
            .And.Contain("instrument hints");
    }

    [Fact]
    public void a_preset_hands_back_a_new_request_every_time()
    {
        //Act
        var first = MuPTPresets.DuetInC.CreateRequest();
        var second = MuPTPresets.DuetInC.CreateRequest();

        first.Seed = 1;

        //Assert
        first.Should().NotBeSameAs(second);
        second.Seed.Should().BeNull();
    }

    [Fact]
    public void a_preset_is_found_by_name_without_regard_to_case()
    {
        //Assert
        MuPTPresets.Find("waltzduetinaminor").Should().BeSameAs(MuPTPresets.WaltzDuetInAMinor);
        SkyTNTPresets.Find(" FOURONTHEFLOOR ").Should().BeSameAs(SkyTNTPresets.FourOnTheFloor);
        MuPTPresets.Find("nothing like it").Should().BeNull();
        SkyTNTPresets.Find(null).Should().BeNull();
    }

    [Fact]
    public void every_preset_names_the_family_its_generator_reports()
    {
        //Arrange
        var wrong = new List<string>();

        //Act
        foreach (var preset in MuPTPresets.All)
        {
            if (preset.Family != MuPTMusicGenerator.MuPTFamily)
            {
                wrong.Add(preset.Name);
            }
        }

        foreach (var preset in SkyTNTPresets.All)
        {
            if (preset.Family != SkyTNTMusicGenerator.SkyTNTFamily)
            {
                wrong.Add(preset.Name);
            }
        }

        //Assert
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void every_preset_describes_itself_in_one_line()
    {
        //Arrange
        var silent = new List<string>();

        //Act
        foreach (var preset in Every())
        {
            var line = preset.ToString();

            if (string.IsNullOrWhiteSpace(preset.Description) || !line.Contains(preset.Name) ||
                !line.Contains(preset.Family) || line.Contains('\n'))
            {
                silent.Add(preset.Name);
            }
        }

        //Assert
        silent.Should().BeEmpty();
        SkyTNTPresets.AmbientElectronica.ToString().Should().NotContain("provisional");
        MuPTPresets.WaltzDuetInAMinor.ToString().Should().Contain(BuiltInRenditions.AmbientDuet);
    }

    private static IEnumerable<MusicPreset> Every()
    {
        foreach (var preset in MuPTPresets.All)
        {
            yield return preset;
        }

        foreach (var preset in SkyTNTPresets.All)
        {
            yield return preset;
        }
    }

    private static IReadOnlyList<string> Names(IReadOnlyList<MusicPreset> presets)
    {
        var names = new List<string>(presets.Count);

        for (var index = 0; index < presets.Count; index++)
        {
            names.Add(presets[index].Name);
        }

        return names;
    }
}
