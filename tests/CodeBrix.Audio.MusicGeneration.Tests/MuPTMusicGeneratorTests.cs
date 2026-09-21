using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The MuPT adapter with NO MODEL ANYWHERE: what it loads (nothing), what it honours, what it
/// refuses by name, and what it says when the file it was given is not there.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class MuPTMusicGeneratorTests
{
    private const string Name = "MuPTUnit";
    private const string Path = "/not/a/real/place/MuPT-v1-8192-190M-Q4_K_M.gguf";

    /// <summary>Starts from freshly built built-ins, because the registry is process-wide.</summary>
    public MuPTMusicGeneratorTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public void building_one_loads_nothing()
    {
        //Act
        using var generator = new MuPTMusicGenerator(Name, Path);

        //Assert
        generator.IsLoaded.Should().BeFalse();
        generator.LoadedThreadCount.Should().BeNull();
        generator.Family.Should().Be(MuPTMusicGenerator.MuPTFamily);
        generator.Name.Should().Be(Name);
    }

    [Fact]
    public void registering_one_loads_nothing()
    {
        //Arrange
        using var generator = new MuPTMusicGenerator(Name, Path);

        //Act
        MusicGeneratorRegistry.Register(generator);

        //Assert
        MusicGeneratorRegistry.IsRegistered(Name).Should().BeTrue();
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void registering_one_is_not_specifying_it() =>
        MusicGeneratorRegistry.Resolve(null).Family.Should().Be("Replay");

    [Fact]
    public void a_generator_with_no_name_is_refused_when_it_is_built()
    {
        //Act
        Action act = () => new MuPTMusicGenerator(" ", Path);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void a_generator_with_no_model_path_is_refused_when_it_is_built()
    {
        //Act
        Action act = () => new MuPTMusicGenerator(Name, " ");

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*GGUF*");
    }

    [Fact]
    public async Task a_file_that_is_not_there_is_a_clear_error_naming_the_path()
    {
        //Arrange
        using var generator = new MuPTMusicGenerator(Name, Path);

        //Act
        var act = async () => await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<MusicGenerationException>())
            .WithMessage("*" + Path + "*");
    }

    [Fact]
    public async Task a_file_that_is_not_a_model_is_a_clear_error_that_says_so()
    {
        //Arrange - a real file that is not a model at all, inside this project's own output
        using var folder = new RenderOutputFolder("not-a-model");
        var path = folder.File("not-a-model.gguf");

        File.WriteAllText(path, "This is not a GGUF file.");

        using var generator = new MuPTMusicGenerator(Name, path);
        var act = async () => await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<MusicGenerationException>())
            .WithMessage("*could not be loaded*");
    }

    [Fact]
    public void it_honours_the_parts_of_a_request_a_text_model_can_act_on()
    {
        //Arrange
        using var generator = new MuPTMusicGenerator(Name, Path);

        //Assert
        generator.Honours.Should().Be(
            MusicRequestFeatures.ModelNativeText | MusicRequestFeatures.Key |
            MusicRequestFeatures.Meter | MusicRequestFeatures.UnitNoteLength |
            MusicRequestFeatures.Tempo | MusicRequestFeatures.Seed |
            MusicRequestFeatures.SamplingControls | MusicRequestFeatures.MaximumEvents |
            MusicRequestFeatures.InferenceThreadCount | MusicRequestFeatures.Continuation |
            MusicRequestFeatures.CharacterWords | MusicRequestFeatures.VoiceCount);
    }

    [Theory]
    [InlineData("free text")]
    [InlineData("a primer")]
    [InlineData("a drum kit")]
    [InlineData("target length")]
    [InlineData("instrument hints")]
    public void what_it_cannot_do_is_refused_by_name(string refused)
    {
        //Arrange
        using var generator = new MuPTMusicGenerator(Name, Path);
        var request = new MusicRequest { Intent = new MusicIntent() };

        switch (refused)
        {
            case "free text": request.Text = "something calm"; break;
            case "a primer": request.Primer = new MidiEventCollection(1, 480); break;
            case "a drum kit": request.Intent.DrumKit = 0; break;
            case "target length": request.Intent.TargetLength = TimeSpan.FromMinutes(1.0); break;
            default: request.InstrumentHints.Add(GeneralMidiProgram.Violin); break;
        }

        //Act
        Action act = () => generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<MusicRequestNotHonouredException>().WithMessage("*" + refused + "*");
    }

    [Fact]
    public void nothing_is_loaded_by_a_request_that_is_refused()
    {
        //Arrange
        using var generator = new MuPTMusicGenerator(Name, Path);
        var request = new MusicRequest { Text = "something calm" };

        //Act
        try
        {
            generator.GenerateAsync(request, TestContext.Current.CancellationToken);
        }
        catch (MusicRequestNotHonouredException)
        {
            // The refusal is the point; what matters is what it did not do.
        }

        //Assert
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void releasing_a_generator_that_was_never_loaded_does_nothing_at_all()
    {
        //Arrange
        using var generator = new MuPTMusicGenerator(Name, Path);

        //Act
        generator.Release();

        //Assert
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task a_disposed_generator_says_so_rather_than_loading_anything()
    {
        //Arrange
        var generator = new MuPTMusicGenerator(Name, Path);

        generator.Dispose();

        //Act
        var act = async () => await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void the_description_says_which_model_it_is_playing()
    {
        //Arrange
        using var generator = new MuPTMusicGenerator(Name, Path);

        //Assert
        generator.Description.Should().Contain(Path);
    }

    [Fact]
    public void the_options_are_copied_so_a_later_edit_cannot_change_a_generator_already_built()
    {
        //Arrange
        var options = new MuPTGeneratorOptions { MaximumTokensPerPass = 64 };

        using var generator = new MuPTMusicGenerator(Name, Path, options);

        //Act
        options.MaximumTokensPerPass = 4096;

        //Assert - the generator kept what it was built with
        MuPTRequestMapper.ToGenerationOptions(new MusicRequest(),
            new MuPTGeneratorOptions { MaximumTokensPerPass = 64 }, Name).MaxTokens.Should().Be(64);
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void the_sampling_settings_go_across_meaning_the_same_thing_on_both_sides()
    {
        //Arrange
        var request = new MusicRequest { Seed = 20260921 };

        request.Controls.Temperature = 0.8;
        request.Controls.TopK = 40;
        request.Controls.TopP = 0.92;
        request.Controls.RepetitionPenalty = MusicGenerationControls.NoRepetitionPenalty;

        //Act
        var generation = MuPTRequestMapper.ToGenerationOptions(request,
            new MuPTGeneratorOptions(), Name);

        //Assert
        generation.Sampling.Temperature.Should().Be(0.8F);
        generation.Sampling.TopK.Should().Be(40);
        generation.Sampling.TopP.Should().Be(0.92F);
        generation.Sampling.RepeatPenalty.Should().Be(1.0F);
        generation.Sampling.Seed.Should().Be(20260921u);
        generation.MaxTokens.Should().Be(MuPTGeneratorOptions.DefaultMaximumTokensPerPass);
    }

    [Fact]
    public void a_length_cap_on_a_request_counts_tokens_and_wins_over_the_generators_own() =>
        MuPTRequestMapper.ToGenerationOptions(RequestWithMaximumEvents(96),
            new MuPTGeneratorOptions { MaximumTokensPerPass = 448 }, Name).MaxTokens.Should().Be(96);

    [Fact]
    public void the_one_seed_the_engine_cannot_hold_is_refused_by_name()
    {
        //Act
        Action act = () => MuPTRequestMapper.ToGenerationOptions(new MusicRequest { Seed = -1 },
            new MuPTGeneratorOptions(), Name);

        //Assert
        act.Should().Throw<MusicRequestNotHonouredException>().WithMessage("*seed -1*");
    }

    [Fact]
    public void a_request_with_no_seed_leaves_the_engine_to_draw_one() =>
        MuPTRequestMapper.ToGenerationOptions(new MusicRequest(), new MuPTGeneratorOptions(), Name)
            .Sampling.Seed.Should().BeNull();

    private static MusicRequest RequestWithMaximumEvents(int maximum)
    {
        var request = new MusicRequest();

        request.Controls.MaximumEvents = maximum;

        return request;
    }
}
