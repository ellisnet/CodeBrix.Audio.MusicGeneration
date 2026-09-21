using System;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Streaming;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// EVERY STARTING NUMBER THE STREAMING LIFECYCLE RUNS ON, held to what the model adapters'
/// measurements settled.
/// </summary>
/// <remarks>
/// THE THREAD COUNT IS A SHARE OF THE MACHINE, so the test cannot assert a number: it asserts the
/// rule - a quarter of the processors, at least one, never more than four - which is right on a
/// four-core board and on a workstation alike.
/// </remarks>
public class StreamingDefaultsTests
{
    [Fact]
    public void the_default_thread_count_is_a_share_of_the_machine_with_a_cap()
    {
        //Arrange
        var expected = Math.Max(1,
            Math.Min(StreamingDefaults.MaximumDefaultInferenceThreads,
                Environment.ProcessorCount / StreamingDefaults.InferenceThreadShare));

        //Assert
        StreamingDefaults.DefaultInferenceThreadCount.Should().Be(expected);
        StreamingDefaults.DefaultInferenceThreadCount.Should().BeGreaterThanOrEqualTo(1);
        StreamingDefaults.DefaultInferenceThreadCount.Should()
            .BeLessThanOrEqualTo(StreamingDefaults.MaximumDefaultInferenceThreads);
    }

    [Fact]
    public void the_default_thread_count_never_takes_more_than_a_quarter_of_the_machine() =>
        StreamingDefaults.DefaultInferenceThreadCount.Should()
            .BeLessThanOrEqualTo(Math.Max(1, Environment.ProcessorCount / 4));

    [Fact]
    public void both_adapters_take_the_same_conservative_share()
    {
        //Assert
        SkyTNTGeneratorOptions.DefaultInferenceThreadCount.Should()
            .Be(StreamingDefaults.DefaultInferenceThreadCount);
        MuPTGeneratorOptions.DefaultInferenceThreadCount.Should()
            .Be(StreamingDefaults.DefaultInferenceThreadCount);
    }

    [Fact]
    public void a_freshly_built_options_object_carries_the_defaults()
    {
        //Act
        var skytnt = new SkyTNTGeneratorOptions();
        var mupt = new MuPTGeneratorOptions();

        //Assert
        skytnt.InferenceThreadCount.Should().Be(StreamingDefaults.DefaultInferenceThreadCount);
        skytnt.MaximumEventsPerPass.Should()
            .Be(SkyTNTGeneratorOptions.DefaultMaximumEventsPerPass);
        skytnt.MaximumPromptBars.Should().Be(SkyTNTGeneratorOptions.DefaultMaximumPromptBars);

        mupt.InferenceThreadCount.Should().Be(StreamingDefaults.DefaultInferenceThreadCount);
        mupt.MaximumTokensPerPass.Should().Be(MuPTGeneratorOptions.DefaultMaximumTokensPerPass);
        mupt.MaximumPromptBars.Should().Be(MuPTGeneratorOptions.DefaultMaximumPromptBars);
        mupt.ContextTokens.Should().Be(MuPTGeneratorOptions.DefaultContextTokens);
    }

    [Fact]
    public void the_measured_pass_lengths_are_what_the_adapters_write()
    {
        //Assert - a thousand SkyTNT events is the last round number still above real time, and
        //448 MuPT tokens is the length the rated music was written at
        SkyTNTGeneratorOptions.DefaultMaximumEventsPerPass.Should().Be(1000);
        MuPTGeneratorOptions.DefaultMaximumTokensPerPass.Should().Be(448);
    }

    [Fact]
    public void every_tail_is_the_same_four_bars()
    {
        //Assert - the engine hands over four bars and neither adapter shortens them
        StreamingDefaults.ContinuationTailBars.Should().Be(4);
        SkyTNTGeneratorOptions.DefaultMaximumPromptBars.Should()
            .Be(StreamingDefaults.ContinuationTailBars);
        MuPTGeneratorOptions.DefaultMaximumPromptBars.Should()
            .Be(StreamingDefaults.ContinuationTailBars);
    }

    [Fact]
    public void the_generate_ahead_window_and_the_pre_roll_stand_where_they_were()
    {
        //Assert - both confirmed by the adapters' measurements rather than changed by them
        StreamingDefaults.GenerateAhead.Should().Be(TimeSpan.FromSeconds(30.0));
        StreamingDefaults.Preroll.Should().Be(TimeSpan.FromSeconds(5.0));
    }

    [Fact]
    public void controller_events_are_off_by_default_because_a_pass_of_them_has_no_music_in_it() =>
        new SkyTNTGeneratorOptions().AllowControlChange.Should().BeFalse();

    [Fact]
    public void a_cloned_options_object_keeps_every_setting()
    {
        //Arrange
        var options = new SkyTNTGeneratorOptions
        {
            InferenceThreadCount = 2,
            MaximumEventsPerPass = 250,
            MaximumPromptBars = 2,
            DrumKit = 24,
            AllowControlChange = true
        };

        //Act
        var copy = options.Clone();

        //Assert
        copy.InferenceThreadCount.Should().Be(2);
        copy.MaximumEventsPerPass.Should().Be(250);
        copy.MaximumPromptBars.Should().Be(2);
        copy.DrumKit.Should().Be(24);
        copy.AllowControlChange.Should().BeTrue();
    }
}
