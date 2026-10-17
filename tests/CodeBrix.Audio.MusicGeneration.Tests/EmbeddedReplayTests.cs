using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The embedded MIDI cues and the shuffled default that plays them. Nothing here renders audio:
/// the cues are about ten minutes each, so they are checked through their metadata and the event
/// stream a pass yields.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class EmbeddedReplayTests
{
    private const int Resolution = 480;

    /// <summary>Starts every test from freshly built built-ins with nothing loaded.</summary>
    public EmbeddedReplayTests() => MusicGeneratorRegistry.ResetForTesting();

    /// <summary>The number of every embedded cue.</summary>
    /// <returns>One cue number per case.</returns>
    public static TheoryData<int> CueNumbers() => new TheoryData<int>(Enumerable.Range(0, 10));

    [Fact]
    public void there_are_ten_embedded_cues_and_each_has_a_reserved_name()
    {
        //Assert
        EmbeddedMusicResources.MidiCues.Should().HaveCount(10);
        EmbeddedReplay.Cues.Should().HaveCount(10);
        EmbeddedReplay.Cues.Should().OnlyContain(name => name.StartsWith("EmbeddedReplay"));
        EmbeddedReplay.Cues.Should().OnlyContain(name => EmbeddedReplay.IsReservedName(name));
    }

    [Theory]
    [MemberData(nameof(CueNumbers))]
    public void every_cue_reads_as_a_long_piece_with_notes_on_several_channels(int cue)
    {
        //Act - read directly from the bytes, the way the replay reads it
        var piece = ReplayPieceBuilder.FromMidiBytes(
            EmbeddedMusicResources.Read(EmbeddedMusicResources.MidiCues[cue]), Resolution, "cue");
        var noteChannels = piece.Items
            .Where(item => item.Event is NoteOnEvent)
            .Select(item => item.Event.Channel)
            .Distinct()
            .ToArray();

        //Assert
        piece.Duration.Should().BeGreaterThan(TimeSpan.FromMinutes(9));
        piece.Duration.Should().BeLessThan(TimeSpan.FromMinutes(11));
        noteChannels.Length.Should().BeGreaterThanOrEqualTo(6);
        noteChannels.Should().Contain(10, "the cues carry General MIDI drums");
        piece.Items.Count(item => item.Event is ControlChangeEvent).Should().BeGreaterThan(0);
    }

    [Fact]
    public void the_cues_are_ten_different_pieces()
    {
        //Act
        var lengths = EmbeddedMusicResources.MidiCues
            .Select(name => ReplayPieceBuilder.FromMidiBytes(EmbeddedMusicResources.Read(name),
                Resolution, name))
            .Select(piece => $"{piece.TotalTicks}|{piece.Items.Count}")
            .ToArray();

        //Assert
        lengths.Distinct().Should().HaveCount(10);
    }

    [Fact]
    public void the_default_is_still_reported_as_the_embedded_midi_replay()
    {
        //Act
        var generator = MusicGeneratorRegistry.Resolve(null);

        //Assert
        generator.Name.Should().Be(EmbeddedReplay.Midi);
        generator.Family.Should().Be(ReplayMusicGenerator.ReplayFamily);
        generator.Description.Should().Contain("shuffled");
        generator.Description.Should().Contain("not a model");
        ((ReplayMusicGenerator)generator).CueCount.Should().Be(10);
    }

    [Theory]
    [MemberData(nameof(CueNumbers))]
    public async Task each_reserved_cue_name_plays_only_its_own_cue(int cue)
    {
        //Arrange
        var named = (ReplayMusicGenerator)MusicGeneratorRegistry.Resolve(EmbeddedReplay.Cues[cue]);
        var expected = ReplayPieceBuilder.FromMidiBytes(
            EmbeddedMusicResources.Read(EmbeddedMusicResources.MidiCues[cue]), Resolution, "cue");

        //Act - three passes: a named cue never shuffles
        var passes = new List<string[]>();
        for (var pass = 0; pass < 3; pass++)
        {
            passes.Add(Describe(await DrainAsync(named, Resolution)));
        }

        //Assert
        named.CueCount.Should().Be(1);
        named.Name.Should().Be(EmbeddedReplay.Cues[cue]);
        passes.Should().OnlyContain(pass => pass.SequenceEqual(Describe(expected)));
    }

    [Fact]
    public async Task each_pass_of_the_default_is_the_cue_its_shuffle_draws()
    {
        //Arrange - the same seed twice: one for the generator, one to say what it should play
        var generator = EmbeddedReplay.CreateDefault(new Random(424242));
        var order = new ShuffledCueOrder(10, new Random(424242));
        var cueLengths = EmbeddedMusicResources.MidiCues
            .Select(name => ReplayPieceBuilder.FromMidiBytes(EmbeddedMusicResources.Read(name),
                Resolution, name).TotalTicks)
            .ToArray();

        //Act and Assert - eleven passes: a whole bag and the first of the next
        var played = new List<long>();
        for (var pass = 0; pass < 11; pass++)
        {
            var expectedCue = order.Next();
            generator.UpcomingCue().Should().Be(expectedCue);
            generator.PassTicks(Resolution).Should().Be(cueLengths[expectedCue]);

            var items = await DrainAsync(generator, Resolution);

            items[items.Count - 1].SettledThroughTick.Should().Be(cueLengths[expectedCue]);
            played.Add(items[items.Count - 1].SettledThroughTick);
        }

        played.Take(10).Should().BeEquivalentTo(cueLengths);
        played[10].Should().NotBe(played[9]);
    }

    [Fact]
    public async Task the_default_keeps_its_cues_once_prepared_and_Release_lets_them_go()
    {
        //Arrange
        var generator = EmbeddedReplay.CreateDefault(new Random(3));

        //Act
        var loadedAtFirst = generator.IsLoaded;
        await generator.PreloadAsync(TestContext.Current.CancellationToken);
        var loadedAfterPreload = generator.IsLoaded;
        await DrainAsync(generator, MusicRequest.DefaultTicksPerQuarterNote);
        var loadedAfterAPass = generator.IsLoaded;
        generator.Release();

        //Assert
        loadedAtFirst.Should().BeFalse();
        loadedAfterPreload.Should().BeTrue();
        loadedAfterAPass.Should().BeTrue();
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task the_default_PassTicks_names_the_cue_about_to_play_not_the_one_just_played()
    {
        //Arrange
        var generator = EmbeddedReplay.CreateDefault(new Random(17));
        var first = generator.UpcomingCue();

        //Act
        await DrainAsync(generator, Resolution);
        var next = generator.UpcomingCue();

        //Assert
        next.Should().NotBe(first);
        generator.PassTicks(Resolution).Should().Be(generator.PieceOfCue(next, Resolution).TotalTicks);
    }

    private static async Task<IReadOnlyList<GeneratedMusicEvent>> DrainAsync(
        IMusicGenerator generator, int ticksPerQuarterNote)
    {
        var request = new MusicRequest
        {
            TicksPerQuarterNote = ticksPerQuarterNote,
            PaceInRealTime = false
        };

        var items = new List<GeneratedMusicEvent>();

        await foreach (var item in generator
                           .GenerateAsync(request, TestContext.Current.CancellationToken)
                           .WithCancellation(TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    private static string[] Describe(IReadOnlyList<GeneratedMusicEvent> items) =>
        items.Select(item => item.HasEvent
                ? $"{item.Event.AbsoluteTime}|{item.Event.CommandCode}|{item.Event.Channel}|" +
                  $"{item.Event}|{item.SettledThroughTick}"
                : $"settled|{item.SettledThroughTick}")
            .ToArray();

    private static string[] Describe(ReplayPiece piece) =>
        Describe(piece.Items.Select(item => item.ToGeneratedEvent()).ToArray());
}
