using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using ModelMidiEvent = CodeBrix.Ollama.ModelRunner.MidiEvent;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>Exercises the public runner package using synthetic ONNX bundles, without Python.</summary>
public sealed class MuseCocoMusicGeneratorTests
{
    private static string Fixture(string kind) => Path.Combine(AppContext.BaseDirectory, "Assets", "MuseCoco", kind);

    private static MuseCocoMusicGenerator Generator(bool withText = false) => new MuseCocoMusicGenerator(
        "MuseCocoTest", Fixture("music"), new MuseCocoGeneratorOptions
        {
            MaximumTokensPerPass = 20, MinimumTokensPerPass = 0, InferenceThreadCount = 1,
            TextBundleDirectory = withText ? Fixture("text") : null
        });

    private static MusicRequest Request() => new MusicRequest
    {
        Seed = 0, Controls = new MusicGenerationControls { TopK = 1 }, PaceInRealTime = false
    };

    private static async Task<List<GeneratedMusicEvent>> Collect(MuseCocoMusicGenerator generator, MusicRequest request)
    {
        var result = new List<GeneratedMusicEvent>();
        await foreach (var item in generator.GenerateAsync(request, TestContext.Current.CancellationToken)) result.Add(item);
        return result;
    }

    [Fact]
    public async Task loading_is_lazy_and_the_optional_text_model_is_loaded_only_for_text()
    {
        // Arrange
        using var generator = Generator(true);
        generator.IsLoaded.Should().BeFalse();
        generator.Schema.Should().BeNull();
        // Act and assert: loading and release are separate observable lifecycle transitions.
        await generator.PreloadAsync(TestContext.Current.CancellationToken);
        generator.IsLoaded.Should().BeTrue();
        generator.Schema.Definitions.Count.Should().Be(63);
        generator.IsTextModelLoaded.Should().BeFalse();

        await Collect(generator, Request());
        generator.IsTextModelLoaded.Should().BeFalse();
        var request = Request();
        request.Text = "A slow piano melody.";
        request.ModelAttributes["tempo"] = "moderate";
        var result = await Collect(generator, request);
        generator.IsTextModelLoaded.Should().BeTrue();
        result.Any(item => item.Event is NoteOnEvent).Should().BeTrue();

        generator.Release();
        generator.IsLoaded.Should().BeFalse();
        generator.IsTextModelLoaded.Should().BeFalse();
        generator.Schema.Should().BeNull();
        await generator.PreloadAsync(TestContext.Current.CancellationToken);
        generator.IsLoaded.Should().BeTrue();
    }

    [Theory]
    [InlineData(480)]
    [InlineData(960)]
    [InlineData(24)]
    public async Task ordered_streams_map_notes_channels_and_completion_at_the_requested_resolution(int resolution)
    {
        // Arrange
        using var generator = Generator();
        var request = Request();
        request.TicksPerQuarterNote = resolution;
        // Act
        var result = await Collect(generator, request);

        // Assert
        var notes = result.Where(item => item.Event is NoteOnEvent).Select(item => (NoteOnEvent)item.Event).ToArray();
        notes.Length.Should().Be(2);
        (notes[0].AbsoluteTime, notes[0].Channel, notes[0].NoteNumber, notes[0].NoteLength)
            .Should().Be((0L, 1, 60, resolution));
        (notes[1].AbsoluteTime, notes[1].Channel, notes[1].NoteNumber, notes[1].NoteLength)
            .Should().Be((4L * resolution, 10, 36, resolution / 2));
        result.Last().HasEvent.Should().BeFalse();
        result.Last().SettledThroughTick.Should().Be(8L * resolution);

        var settled = -1L;
        foreach (var item in result)
        {
            if (item.HasEvent) item.Event.AbsoluteTime.Should().BeGreaterThan(settled);
            item.SettledThroughTick.Should().BeGreaterThanOrEqualTo(settled);
            settled = item.SettledThroughTick;
        }
        result.Where(item => item.Event is PatchChangeEvent).Select(item => item.Event.Channel)
            .Should().Contain(new[] { 1, 10 });
    }

    [Fact]
    public async Task completion_and_early_disposal_release_the_generation_lease()
    {
        // Arrange
        using var generator = Generator();
        // Act and assert: a live enumeration holds the lease; disposal makes it reusable.
        var first = await Collect(generator, Request());
        await using (var active = generator.GenerateAsync(Request(), TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            (await active.MoveNextAsync()).Should().BeTrue();
            Action release = generator.Release;
            Action dispose = generator.Dispose;
            release.Should().Throw<InvalidOperationException>();
            dispose.Should().Throw<InvalidOperationException>();
            Func<Task> concurrent = () => Collect(generator, Request());
            await concurrent.Should().ThrowAsync<InvalidOperationException>();
        }
        var repeated = await Collect(generator, Request());
        repeated.Select(item => item.ToString()).Should().Equal(first.Select(item => item.ToString()));
        generator.Release();
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task cancellation_does_not_flush_the_tail_and_the_model_can_be_used_again()
    {
        // Arrange
        using var generator = Generator();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        // Act and assert
        await using (var active = generator.GenerateAsync(Request(), cancellation.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            (await active.MoveNextAsync()).Should().BeTrue();
            active.Current.SettledThroughTick.Should().Be(-1);
            cancellation.Cancel();
            Func<Task> next = async () => { await active.MoveNextAsync(); };
            await next.Should().ThrowAsync<OperationCanceledException>();
        }
        (await Collect(generator, Request())).Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task file_maps_are_copied_and_text_maps_work_without_a_store()
    {
        // Arrange
        var files = Directory.GetFiles(Fixture("music")).ToDictionary(Path.GetFileName, path => path);
        var textFiles = Directory.GetFiles(Fixture("text")).ToDictionary(Path.GetFileName, path => path);
        using var generator = new MuseCocoMusicGenerator("Mapped", files, textFiles,
            new MuseCocoGeneratorOptions { MaximumTokensPerPass = 20, MinimumTokensPerPass = 0, InferenceThreadCount = 1 });
        // Act: changing the caller maps must not change the constructed generator.
        files.Clear();
        textFiles.Clear();
        var request = Request();
        request.Text = "piano";
        // Assert
        (await Collect(generator, request)).Any(item => item.Event is NoteOnEvent).Should().BeTrue();
    }

    [Fact]
    public async Task invalid_attributes_fail_clearly_and_leave_the_model_reusable()
    {
        // Arrange
        using var generator = Generator(true);
        var request = Request();
        request.Text = "slow piano";
        request.ModelAttributes["tempo"] = "not-a-category";
        // Act and assert: failure must leave the loaded generator reusable.
        Func<Task> generate = () => Collect(generator, request);
        await generate.Should().ThrowAsync<ArgumentException>().WithMessage("*tempo*");
        request.ModelAttributes["tempo"] = "fast";
        (await Collect(generator, request)).Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task a_short_request_clamps_the_minimum_and_thread_changes_reload_the_model()
    {
        // Arrange
        using var generator = new MuseCocoMusicGenerator("Short", Fixture("music"),
            new MuseCocoGeneratorOptions { InferenceThreadCount = 1 });
        var request = Request();
        request.Controls.MaximumEvents = 7;
        // Act and assert
        var first = await Collect(generator, request);
        first.Count(item => item.Event is NoteOnEvent).Should().Be(1);
        request.InferenceThreadCount = 2;
        await Collect(generator, request);
        generator.LoadedThreadCount.Should().Be(2);
    }

    [Fact]
    public void unsupported_prompt_fields_are_refused_before_any_model_load()
    {
        // Arrange
        using var generator = Generator();
        var request = Request();
        request.Text = "A piano piece";
        // Act and assert
        Action generate = () => generator.GenerateAsync(request, CancellationToken.None);
        generate.Should().Throw<MusicRequestNotHonouredException>().WithMessage("*free text*");
        generator.IsLoaded.Should().BeFalse();
        request.Text = null;
        request.Intent = new MusicIntent { BeatsPerMinute = 120 };
        generate.Should().Throw<MusicRequestNotHonouredException>().WithMessage("*tempo*");
    }

    [Fact]
    public async Task requests_are_snapshotted_before_enumeration_and_disposal_is_permanent()
    {
        // Arrange
        var generator = Generator();
        var request = Request();
        request.ModelAttributes["tempo"] = "slow";
        // Act
        var stream = generator.GenerateAsync(request, TestContext.Current.CancellationToken);
        request.ModelAttributes["tempo"] = "not-a-category";
        await foreach (var item in stream) { }
        generator.Dispose();
        // Assert
        Func<Task> preload = () => generator.PreloadAsync(TestContext.Current.CancellationToken);
        await preload.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void model_attributes_are_capabilities_and_are_copied_with_requests()
    {
        // Arrange
        var request = new MusicRequest();
        request.ModelAttributes["structure"] = "AB";
        // Act and assert
        MusicGeneratorCapabilities.FeaturesUsedBy(request).Should().Be(MusicRequestFeatures.ModelAttributes);
        var copy = request.Clone();
        copy.ModelAttributes["structure"] = "AA";
        request.ModelAttributes["structure"].Should().Be("AB");
    }

    [Fact]
    public void a_coarse_horizon_still_leaves_same_tick_events_unsettled()
    {
        // Arrange
        var pass = new MuseCocoPass(1);
        // Act
        var first = pass.Accept(ModelMidiEvent.Note(40, 0, 0, 60, 80, 40));
        var second = pass.Accept(ModelMidiEvent.Note(80, 0, 0, 64, 80, 40));
        // Assert
        first.Event.AbsoluteTime.Should().Be(0);
        first.SettledThroughTick.Should().Be(-1);
        second.Event.AbsoluteTime.Should().Be(0);
        second.SettledThroughTick.Should().Be(-1);
    }

    [Fact]
    public async Task experimental_sections_replay_context_without_reemitting_it_or_resetting_channels()
    {
        // Arrange: the synthetic model continues its second bar after a one-bar context replay.
        using var generator = ExperimentalGenerator();

        // Act
        var result = await Collect(generator, Request());
        var notes = result.Where(item => item.Event is NoteOnEvent).Select(item => (NoteOnEvent)item.Event).ToArray();

        // Assert: four notes consume 14 + 6 + 6 NEW tokens, on one continuous timeline.
        notes.Select(note => note.AbsoluteTime).Should().Equal(0L, 1920L, 3840L, 5760L);
        notes.Select(note => note.Channel).Should().Equal(1, 10, 10, 10);
        result.Count(item => item.Event is PatchChangeEvent).Should().Be(2);
        result.Where(item => !item.HasEvent).Select(item => item.SettledThroughTick)
            .Should().Equal(3839L, 5759L, 7679L, 7680L);
        var repeated = await Collect(generator, Request());
        repeated.Select(item => item.ToString()).Should().Equal(result.Select(item => item.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task interrupting_experimental_continuation_discards_context_and_preserves_model_reuse(bool cancel)
    {
        // Arrange
        using var generator = ExperimentalGenerator();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using (var stream = generator.GenerateAsync(Request(), cancellation.Token).GetAsyncEnumerator(cancellation.Token))
        {
            (await stream.MoveNextAsync()).Should().BeTrue();

            // Act
            if (cancel)
            {
                cancellation.Cancel();
                Func<Task> next = async () => { await stream.MoveNextAsync(); };
                await next.Should().ThrowAsync<OperationCanceledException>();
            }
        }

        // Assert: new context starts at zero, rather than reusing the interrupted context.
        var result = await Collect(generator, Request());
        result.First(item => item.Event is NoteOnEvent).Event.AbsoluteTime.Should().Be(0);
        result.Count(item => item.Event is NoteOnEvent).Should().Be(4);
    }

    [Fact]
    public async Task experimental_total_token_cap_still_limits_the_whole_request()
    {
        // Arrange
        using var generator = ExperimentalGenerator();
        var request = Request();
        request.Controls.MaximumEvents = 20;

        // Act
        var result = await Collect(generator, request);

        // Assert: 14 + 6 tokens produce three notes; there is no third section.
        result.Count(item => item.Event is NoteOnEvent).Should().Be(3);
        result.Last().SettledThroughTick.Should().Be(5760);
    }

    private static MuseCocoMusicGenerator ExperimentalGenerator() => new MuseCocoMusicGenerator(
        "Experimental", Fixture("music"), new MuseCocoGeneratorOptions
        {
            ExperimentalContinuation = true, ExperimentalSectionTokens = 14, ExperimentalContextBars = 1,
            MaximumTokensPerPass = 26, MinimumTokensPerPass = 0, InferenceThreadCount = 1
        });
}
