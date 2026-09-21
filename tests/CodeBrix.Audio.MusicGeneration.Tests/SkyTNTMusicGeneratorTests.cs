using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// What the SkyTNT generator does with NO MODEL ANYWHERE NEAR IT: what it honours, what it
/// refuses, that building and registering it load nothing, and what a folder holding nothing
/// runnable says.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class SkyTNTMusicGeneratorTests : IDisposable
{
    private const string Name = "SkyTNTUnderTest";

    private readonly string temporary;

    /// <summary>Starts every test from freshly built built-ins, because the registry is process-wide.</summary>
    public SkyTNTMusicGeneratorTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();

        temporary = Path.Combine(Path.GetTempPath(),
            "codebrix-musicgen-skytnt-" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>Clears away whatever a test made inside its own temporary folder.</summary>
    public void Dispose()
    {
        if (Directory.Exists(temporary))
        {
            Directory.Delete(temporary, true);
        }
    }

    [Fact]
    public void building_one_loads_nothing()
    {
        //Arrange and Act
        var generator = new SkyTNTMusicGenerator(Name, temporary);

        //Assert - the folder does not even exist, and building it said nothing about that
        generator.IsLoaded.Should().BeFalse();
        generator.LoadedThreadCount.Should().BeNull();
    }

    [Fact]
    public void registering_one_loads_nothing()
    {
        //Arrange
        var generator = new SkyTNTMusicGenerator(Name, temporary);

        //Act
        MusicGeneratorRegistry.Register(generator);

        //Assert
        MusicGeneratorRegistry.Resolve(Name).Should().BeSameAs(generator);
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void it_does_not_register_itself()
    {
        //Arrange and Act
        var generator = new SkyTNTMusicGenerator(Name, temporary);

        //Assert - registering is not specifying, and building is not registering either
        MusicGeneratorRegistry.RegisteredNames.Should().NotContain(generator.Name);
    }

    [Fact]
    public void registered_but_not_specified_leaves_the_embedded_replay_playing()
    {
        //Arrange - a SkyTNT generator over a folder that holds nothing at all
        TestInstrumentLibraries.GeneralMidi();

        var generator = new SkyTNTMusicGenerator(Name, temporary);

        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions
        {
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            ApplicationOwnsAudioOutput = true,
            EndOfPiece = EndOfPiecePolicy.Stop
        };

        using var session = new MusicSession(options);

        //Act - registering is not specifying, so nothing names it and nothing loads it
        session.Play();

        //Assert
        session.ActiveSource.GeneratorName.Should().Be(EmbeddedReplay.Midi);
        generator.IsLoaded.Should().BeFalse();
        session.GenerationError.Should().BeNull();
    }

    [Fact]
    public void it_belongs_to_the_SkyTNT_family() =>
        new SkyTNTMusicGenerator(Name, "/nowhere").Family.Should().Be("SkyTNT");

    [Fact]
    public void it_honours_the_parts_of_a_request_this_model_really_acts_on()
    {
        //Arrange
        var generator = new SkyTNTMusicGenerator(Name, temporary);

        //Act
        var honours = generator.Honours;

        //Assert
        honours.Should().Be(
            MusicRequestFeatures.InstrumentHints | MusicRequestFeatures.Tempo |
            MusicRequestFeatures.Meter | MusicRequestFeatures.Key | MusicRequestFeatures.Seed |
            MusicRequestFeatures.SamplingControls | MusicRequestFeatures.MaximumEvents |
            MusicRequestFeatures.InferenceThreadCount | MusicRequestFeatures.Primer |
            MusicRequestFeatures.Continuation | MusicRequestFeatures.DrumKit |
            MusicRequestFeatures.CharacterWords);
    }

    [Theory]
    [InlineData("free text")]
    [InlineData("model-native text")]
    [InlineData("unit note length")]
    [InlineData("voice count")]
    [InlineData("target length")]
    public void what_it_does_not_honour_is_refused_by_name(string what)
    {
        //Arrange
        var generator = new SkyTNTMusicGenerator(Name, temporary);
        var request = RequestUsing(what);

        //Act
        Action act = () => generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<MusicRequestNotHonouredException>().Which
            .Message.Should().Contain(what);
    }

    [Fact]
    public async Task a_folder_that_holds_nothing_runnable_says_what_is_there_and_what_is_wanted()
    {
        //Arrange - an empty folder inside the test's own temporary area
        Directory.CreateDirectory(temporary);
        File.WriteAllText(Path.Combine(temporary, "readme.txt"), "not a model");

        var generator = new SkyTNTMusicGenerator(Name, temporary);

        //Act
        Func<Task> act = () => generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        var thrown = (await act.Should().ThrowAsync<MusicGenerationException>()).Which;
        thrown.Message.Should().Contain("config.json");
        thrown.Message.Should().Contain("model_base.onnx");
        thrown.Message.Should().Contain("model_token.onnx");
        thrown.Message.Should().Contain("readme.txt");
        thrown.Message.Should().Contain("ModelManager");
    }

    [Fact]
    public async Task a_folder_that_is_not_there_says_so()
    {
        //Arrange
        var generator = new SkyTNTMusicGenerator(Name, temporary);

        //Act
        Func<Task> act = () => generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<MusicGenerationException>()).Which
            .Message.Should().Contain("does not exist");
    }

    [Fact]
    public void a_map_of_files_missing_one_of_the_three_is_refused_where_it_is_written()
    {
        //Arrange
        var files = new Dictionary<string, string>
        {
            ["config.json"] = "/nowhere/config.json",
            ["model_base.onnx"] = "/nowhere/model_base.onnx"
        };

        //Act
        Action act = () => new SkyTNTMusicGenerator(Name, files);

        //Assert
        act.Should().Throw<MusicGenerationException>().Which
            .Message.Should().Contain("model_token.onnx");
    }

    [Fact]
    public void a_map_of_all_three_files_builds_and_loads_nothing()
    {
        //Arrange
        var files = new Dictionary<string, string>
        {
            ["config.json"] = "/nowhere/config.json",
            ["model_base.onnx"] = "/nowhere/model_base.onnx",
            ["model_token.onnx"] = "/nowhere/model_token.onnx"
        };

        //Act
        var generator = new SkyTNTMusicGenerator(Name, files);

        //Assert
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void a_generator_with_no_name_is_refused_where_it_is_written()
    {
        //Act
        Action act = () => new SkyTNTMusicGenerator(" ", "/nowhere");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void a_generator_with_no_folder_and_no_files_is_refused_where_it_is_written()
    {
        //Act
        Action act = () => new SkyTNTMusicGenerator(Name, (string)null);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void releasing_one_that_was_never_loaded_does_nothing()
    {
        //Arrange
        var generator = new SkyTNTMusicGenerator(Name, temporary);

        //Act
        generator.Release();

        //Assert
        generator.IsLoaded.Should().BeFalse();
    }

    private static MusicRequest RequestUsing(string what)
    {
        var request = new MusicRequest();

        switch (what)
        {
            case "free text":
                request.Text = "something calm";
                break;

            case "model-native text":
                request.ModelNativeText = "X:1";
                break;

            case "unit note length":
                request.Intent = new MusicIntent { UnitNoteLength = new MusicNoteLength(1, 8) };
                break;

            case "voice count":
                request.Intent = new MusicIntent { VoiceCount = 2 };
                break;

            case "target length":
                request.Intent = new MusicIntent { TargetLength = TimeSpan.FromMinutes(2.0) };
                break;
        }

        return request;
    }
}
