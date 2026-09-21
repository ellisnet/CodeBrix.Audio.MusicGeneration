using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The four replay generators that ship inside the package, and the contract every generator
/// keeps: segment-relative ticks at the resolution the request names, tick order, an honest
/// settled tick, and a pass that ends on a bar line.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class ReplayMusicGeneratorTests
{
    /// <summary>Starts every test from freshly built built-ins with nothing loaded.</summary>
    public ReplayMusicGeneratorTests() => MusicGeneratorRegistry.ResetForTesting();

    /// <summary>The four reserved names.</summary>
    /// <returns>One name per case.</returns>
    public static TheoryData<string> BuiltInNames() =>
        new TheoryData<string>(EmbeddedReplay.Midi, EmbeddedReplay.MidiSecond,
            EmbeddedReplay.Abc, EmbeddedReplay.AbcSecond);

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task every_built_in_replay_yields_music(string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert
        items.Should().NotBeEmpty();
        items.Count(item => item.HasEvent && item.Event is NoteOnEvent).Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task ticks_start_at_the_beginning_of_the_segment_and_never_go_backwards(string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);

        //Act
        var ticks = TicksOf(await DrainAsync(generator, 480));

        //Assert
        ticks[0].Should().Be(0L);
        ticks.Should().BeInAscendingOrder();
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task the_settled_tick_never_runs_ahead_of_what_has_been_yielded(string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);
        var items = await DrainAsync(generator, 480);
        var settled = GeneratedMusicEvent.NothingSettled;

        //Act
        foreach (var item in items)
        {
            //Assert
            if (item.HasEvent)
            {
                // Nothing is ever claimed to have settled beyond the tick being written.
                item.SettledThroughTick.Should().BeLessThanOrEqualTo(item.Event.AbsoluteTime);
            }

            item.SettledThroughTick.Should().BeGreaterThanOrEqualTo(settled);
            settled = item.SettledThroughTick;
        }
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task events_that_share_a_tick_settle_it_only_once_they_have_all_been_yielded(
        string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);
        var items = await DrainAsync(generator, 480);

        //Act
        var events = items.Where(item => item.HasEvent).ToArray();

        //Assert
        for (var i = 0; i < events.Length - 1; i++)
        {
            if (events[i + 1].Event.AbsoluteTime == events[i].Event.AbsoluteTime)
            {
                events[i].SettledThroughTick
                    .Should().BeLessThan(events[i].Event.AbsoluteTime);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task the_last_item_settles_the_whole_bar_rounded_length(string name)
    {
        //Arrange
        var generator = (ReplayMusicGenerator)MusicGeneratorRegistry.Resolve(name);

        //Act
        var items = await DrainAsync(generator, 480);
        var passTicks = generator.PassTicks(480);
        var lastSounding = items.Where(item => item.HasEvent)
            .Select(item => item.Event is NoteOnEvent note
                ? note.AbsoluteTime + note.NoteLength
                : item.Event.AbsoluteTime)
            .Max();

        //Assert
        items[items.Count - 1].SettledThroughTick.Should().Be(passTicks);
        passTicks.Should().BeGreaterThanOrEqualTo(lastSounding);
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task a_second_pass_is_identical_to_the_first(string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);

        //Act
        var first = await DrainAsync(generator, 480);
        var second = await DrainAsync(generator, 480);

        //Assert
        Describe(second).Should().Equal(Describe(first));
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task the_ticks_are_at_the_resolution_the_request_named(string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);

        //Act
        var atFourEighty = await DrainAsync(generator, 480);
        var atNineSixty = await DrainAsync(generator, 960);

        //Assert
        atNineSixty.Should().HaveCount(atFourEighty.Count);

        var coarse = TicksOf(atFourEighty);
        var fine = TicksOf(atNineSixty);

        for (var i = 0; i < coarse.Length; i++)
        {
            // Twice the resolution, twice the tick - give or take the tick a rounded subdivision
            // lands on.
            fine[i].Should().BeInRange((coarse[i] * 2L) - 2L, (coarse[i] * 2L) + 2L);
        }
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task a_pass_at_a_coarser_resolution_keeps_every_note(string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);

        //Act
        var atFourEighty = await DrainAsync(generator, 480);
        var atNinetySix = await DrainAsync(generator, 96);

        //Assert
        NoteCount(atNinetySix).Should().Be(NoteCount(atFourEighty));
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task no_note_off_is_ever_yielded_because_a_note_carries_its_own_length(string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert
        items.Where(item => item.HasEvent)
            .Should().OnlyContain(item => !MidiEvent.IsNoteOff(item.Event));
        items.Where(item => item.HasEvent && item.Event is NoteOnEvent)
            .Should().OnlyContain(item => ((NoteOnEvent)item.Event).NoteLength > 0);
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public async Task every_channel_is_counted_from_one(string name)
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(name);

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert
        items.Where(item => item.HasEvent && item.Event.CommandCode != MidiCommandCode.MetaEvent)
            .Should().OnlyContain(item => item.Event.Channel >= 1 && item.Event.Channel <= 16);
    }

    [Fact]
    public async Task the_two_abc_replays_carry_the_tune_that_was_written_in_them()
    {
        //Arrange
        var duet = MusicGeneratorRegistry.Resolve(EmbeddedReplay.Abc);
        var air = MusicGeneratorRegistry.Resolve(EmbeddedReplay.AbcSecond);

        //Act
        var duetChannels = ChannelsOf(await DrainAsync(duet, 480));
        var airChannels = ChannelsOf(await DrainAsync(air, 480));

        //Assert
        duetChannels.Should().HaveCountGreaterThan(1);
        airChannels.Should().HaveCount(1);
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public void a_replay_belongs_to_the_replay_family_and_says_what_it_is(string name)
    {
        //Act
        var generator = MusicGeneratorRegistry.Resolve(name);

        //Assert
        generator.Family.Should().Be(ReplayMusicGenerator.ReplayFamily);
        generator.Name.Should().Be(name);
        generator.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public void a_replay_honours_a_continuation_and_nothing_else(string name)
        => MusicGeneratorRegistry.Resolve(name).Honours
            .Should().Be(MusicRequestFeatures.Continuation);

    [Fact]
    public async Task a_continuation_request_is_the_next_pass()
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(EmbeddedReplay.AbcSecond);
        var first = new MusicRequest { PaceInRealTime = false };
        var next = new MusicRequest
        {
            PaceInRealTime = false,
            Continuation = new MusicContinuation { BeatsPerMinute = 92.0 }
        };

        //Act
        var pass = await CollectAsync(generator, first);
        var continued = await CollectAsync(generator, next);

        //Assert
        Describe(continued).Should().Equal(Describe(pass));
    }

    [Fact]
    public async Task a_request_a_replay_cannot_honour_is_refused_by_name()
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(EmbeddedReplay.Midi);
        var request = new MusicRequest { Seed = 29, PaceInRealTime = false };

        //Act
        Func<Task> act = async () => await CollectAsync(generator, request);

        //Assert
        var thrown = await act.Should().ThrowAsync<MusicRequestNotHonouredException>();
        thrown.Which.Message.Should().Contain("seed");
        thrown.Which.Message.Should().Contain(EmbeddedReplay.Midi);
    }

    [Fact]
    public void GenerateAsync_rejects_a_null_request()
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(EmbeddedReplay.Midi);
        Action act = () => generator.GenerateAsync(null, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void a_replay_has_nothing_loaded_until_it_is_used()
        => MusicGeneratorRegistry.Resolve(EmbeddedReplay.Midi).IsLoaded.Should().BeFalse();

    [Fact]
    public async Task PreloadAsync_loads_it_before_the_first_request()
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(EmbeddedReplay.AbcSecond);

        //Act
        await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        generator.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task PreloadAsync_twice_is_harmless()
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(EmbeddedReplay.AbcSecond);

        //Act
        await generator.PreloadAsync(TestContext.Current.CancellationToken);
        await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        generator.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task the_first_request_loads_it_when_nothing_preloaded_it()
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(EmbeddedReplay.AbcSecond);

        //Act
        await DrainAsync(generator, 480);

        //Assert
        generator.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task Release_gives_the_memory_back_and_leaves_the_generator_usable()
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(EmbeddedReplay.AbcSecond);
        var before = await DrainAsync(generator, 480);

        //Act
        generator.Release();
        var loadedAfterRelease = generator.IsLoaded;
        var after = await DrainAsync(generator, 480);

        //Assert
        loadedAfterRelease.Should().BeFalse();
        generator.IsLoaded.Should().BeTrue();
        Describe(after).Should().Equal(Describe(before));
        MusicGeneratorRegistry.IsRegistered(EmbeddedReplay.AbcSecond).Should().BeTrue();
    }

    private static async Task<IReadOnlyList<GeneratedMusicEvent>> DrainAsync(
        IMusicGenerator generator, int ticksPerQuarterNote)
    {
        var request = new MusicRequest
        {
            TicksPerQuarterNote = ticksPerQuarterNote,
            PaceInRealTime = false
        };

        return await CollectAsync(generator, request);
    }

    private static async Task<IReadOnlyList<GeneratedMusicEvent>> CollectAsync(
        IMusicGenerator generator, MusicRequest request)
    {
        var items = new List<GeneratedMusicEvent>();

        await foreach (var item in generator
                           .GenerateAsync(request, TestContext.Current.CancellationToken)
                           .WithCancellation(TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    private static int NoteCount(IReadOnlyList<GeneratedMusicEvent> items) =>
        items.Count(item => item.HasEvent && item.Event is NoteOnEvent);

    private static long[] TicksOf(IReadOnlyList<GeneratedMusicEvent> items) =>
        items.Select(item => item.HasEvent ? item.Event.AbsoluteTime : item.SettledThroughTick)
            .ToArray();

    private static IReadOnlyList<int> ChannelsOf(IReadOnlyList<GeneratedMusicEvent> items) =>
        items.Where(item => item.HasEvent && item.Event is NoteOnEvent)
            .Select(item => item.Event.Channel)
            .Distinct()
            .ToArray();

    private static string[] Describe(IReadOnlyList<GeneratedMusicEvent> items) =>
        items.Select(item => item.HasEvent
                ? $"{item.Event.AbsoluteTime}|{item.Event.CommandCode}|{item.Event.Channel}|" +
                  $"{item.Event}|{item.SettledThroughTick}"
                : $"settled|{item.SettledThroughTick}")
            .ToArray();
}
