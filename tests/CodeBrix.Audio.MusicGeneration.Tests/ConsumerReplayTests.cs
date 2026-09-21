using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
/// A consumer replaying their OWN music: a MIDI file, a MIDI stream, a MIDI event collection, or a
/// tune written in ABC - under a name of their own - and what a malformed piece says.
/// </summary>
public class ConsumerReplayTests
{
    [Fact]
    public async Task a_consumer_s_own_midi_events_are_replayed()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromMidiEvents("MyTheme",
            TestMusic.OneBarOfQuarterNotes(480));

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert
        generator.Name.Should().Be("MyTheme");
        generator.Family.Should().Be(ReplayMusicGenerator.ReplayFamily);
        NotesOf(items).Should().Equal(60, 61, 62, 63);
        items[items.Count - 1].SettledThroughTick.Should().Be(1920L);
    }

    [Fact]
    public async Task a_consumer_s_own_midi_stream_is_replayed()
    {
        //Arrange
        using var stream = ExportToStream(TestMusic.OneBarOfQuarterNotes(480));
        var generator = ReplayMusicGenerator.FromMidiStream("FromStream", stream);

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert
        NotesOf(items).Should().Equal(60, 61, 62, 63);
    }

    [Fact]
    public async Task a_consumer_s_own_midi_file_is_replayed()
    {
        //Arrange - inside the test project's own output folder, which is inside this repository,
        //and under a name of this run's own: two suites running at once would otherwise share one
        //file, and one of them would delete it while the other was reading it
        var path = Path.Combine(AppContext.BaseDirectory,
            $"consumer-replay-test-{Environment.ProcessId}-{Guid.NewGuid():N}.mid");

        using (var exported = ExportToStream(TestMusic.OneBarOfQuarterNotes(480)))
        {
            File.WriteAllBytes(path, exported.ToArray());
        }

        try
        {
            var generator = ReplayMusicGenerator.FromMidiFile("FromFile", path);

            //Act
            var items = await DrainAsync(generator, 480);

            //Assert
            NotesOf(items).Should().Equal(60, 61, 62, 63);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task a_consumer_s_own_abc_is_replayed()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromAbc("MyTune", TestMusic.SimpleAbc);

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert
        NotesOf(items).Should().HaveCount(16);
        items[items.Count - 1].SettledThroughTick.Should().Be(7680L);
    }

    [Fact]
    public async Task a_pass_ends_on_a_bar_line_even_when_the_music_does_not()
    {
        //Arrange - one chord on the second beat of a bar of 4/4, over in half a bar
        var generator = ReplayMusicGenerator.FromMidiEvents("HalfBar", TestMusic.OneChord(480));

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert
        items[items.Count - 1].SettledThroughTick.Should().Be(1920L);
    }

    [Fact]
    public async Task events_that_share_a_tick_settle_that_tick_only_once()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromMidiEvents("Chord", TestMusic.OneChord(480));

        //Act
        var items = await DrainAsync(generator, 480);
        var chord = items.Where(item => item.HasEvent && item.Event is NoteOnEvent).ToArray();

        //Assert
        chord.Should().HaveCount(3);
        chord[0].SettledThroughTick.Should().Be(0L);
        chord[1].SettledThroughTick.Should().Be(0L);
        chord[2].SettledThroughTick.Should().Be(480L);
    }

    [Fact]
    public async Task an_event_alone_at_its_tick_settles_that_tick_straight_away()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromMidiEvents("Chord", TestMusic.OneChord(480));

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert - the time signature shares tick 0 with nothing else, so it settles tick 0
        items[0].SettledThroughTick.Should().Be(0L);
    }

    [Fact]
    public async Task a_consumer_s_replay_can_be_registered_and_resolved_by_name()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromAbc("RegisteredTune", TestMusic.SimpleAbc);
        generator.Description = "The theme of my game.";

        //Act
        var items = await DrainAsync(generator, 480);

        //Assert
        generator.Description.Should().Be("The theme of my game.");
        items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task midi_that_is_not_midi_says_so_clearly()
    {
        //Arrange
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("this is not a MIDI file"));
        var generator = ReplayMusicGenerator.FromMidiStream("Rubbish", stream);
        Func<Task> act = async () =>
            await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<MusicGenerationException>();
        thrown.Which.Message.Should().Contain("Rubbish");
        thrown.Which.Message.Should().Contain("MIDI");
    }

    [Fact]
    public async Task abc_that_is_not_abc_says_so_clearly()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromAbc("NotATune", TestMusic.NotAbc);
        Func<Task> act = async () =>
            await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<MusicGenerationException>();
        thrown.Which.Message.Should().Contain("NotATune");
        thrown.Which.Message.Should().Contain("ABC");
    }

    [Fact]
    public async Task a_piece_with_no_music_in_it_says_so_clearly()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromMidiEvents("Empty", new MidiEventCollection(1, 480));
        Func<Task> act = async () =>
            await generator.PreloadAsync(TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<MusicGenerationException>();
        thrown.Which.Message.Should().Contain("no music");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void a_replay_must_be_given_a_name(string name)
    {
        //Arrange
        Action act = () => ReplayMusicGenerator.FromAbc(name, TestMusic.SimpleAbc);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromAbc_rejects_text_with_nothing_in_it()
    {
        //Arrange
        Action act = () => ReplayMusicGenerator.FromAbc("Blank", "   ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromMidiStream_rejects_a_null_stream()
    {
        //Arrange
        Action act = () => ReplayMusicGenerator.FromMidiStream("NoStream", null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromMidiEvents_rejects_a_null_collection()
    {
        //Arrange
        Action act = () => ReplayMusicGenerator.FromMidiEvents("NoEvents", null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromMidiFile_rejects_a_blank_path()
    {
        //Arrange
        Action act = () => ReplayMusicGenerator.FromMidiFile("NoPath", "  ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task a_consumer_s_replay_loads_nothing_until_it_is_used()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromAbc("Lazy", TestMusic.SimpleAbc);
        var loadedAtConstruction = generator.IsLoaded;

        //Act
        await DrainAsync(generator, 480);

        //Assert
        loadedAtConstruction.Should().BeFalse();
        generator.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void Description_rejects_null()
    {
        //Arrange
        var generator = ReplayMusicGenerator.FromAbc("Described", TestMusic.SimpleAbc);
        Action act = () => generator.Description = null;

        //Assert
        act.Should().Throw<ArgumentNullException>();
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

    private static MemoryStream ExportToStream(MidiEventCollection events)
    {
        events.PrepareForExport();

        var stream = new MemoryStream();
        MidiFile.Export(stream, events, true);
        stream.Position = 0L;

        return stream;
    }

    private static int[] NotesOf(IReadOnlyList<GeneratedMusicEvent> items) =>
        items.Where(item => item.HasEvent && item.Event is NoteOnEvent)
            .Select(item => item.Event is NoteEvent note ? note.NoteNumber : -1)
            .ToArray();
}
