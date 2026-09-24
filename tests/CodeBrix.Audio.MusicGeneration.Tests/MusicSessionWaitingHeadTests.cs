using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// SILENCE WHILE THE HEAD WAITS, NOT A DRONE. The sequencer delivers every event the head has
/// reached, in every state - so a note put at the very place a waiting head stands would sound at
/// once while the head went on waiting, and its note-off, further on, would not be reached until
/// the wait was over. These prove nothing is put there until the head can play through it.
/// </summary>
/// <remarks>
/// The test library's instruments make a sound only while a note is on, and write down every
/// message they are sent, so both the output and the notes held on can be read directly.
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MusicSessionWaitingHeadTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;

    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicSessionWaitingHeadTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public async Task behind_a_generator_ten_times_slower_than_real_time_the_output_is_silence_until_the_music_plays()
    {
        //Arrange
        var time = new ManualTimeProvider();
        var library = Library("WaitingSlowParts");
        MusicGeneratorRegistry.Register(ReplayOnTestClock.On(
            ReplayMusicGenerator.FromMidiEvents("SlowPiece", TestMusic.Bars(8, Resolution)), time, 0.1));

        using var session = new MusicSession(Options("SlowPiece", library.Name), TestInstrumentLibraries.Lookup(),
            time);
        session.Play();

        var left = new float[SampleRate / 10];
        var right = new float[SampleRate / 10];
        var silentWaits = 0;

        //Act - listen a tenth of a second at a time for as long as the head has not moved
        for (var step = 0; step < 400 && session.Position == TimeSpan.Zero; step++)
        {
            session.Renderer.Render(left, right);

            if (session.Position == TimeSpan.Zero)
            {
                //Assert - while the head has not moved, not a single sample is anything but zero
                left.Should().OnlyContain(sample => sample == 0.0F);
                NotesStillOn(library).Should().Be(0);
                silentWaits++;
            }

            await Advance(time, TimeSpan.FromSeconds(0.1));
        }

        silentWaits.Should().BeGreaterThan(50, "the generator takes many seconds to write the first pre-roll");
        session.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task a_head_that_runs_out_mid_piece_leaves_no_note_on_while_it_waits()
    {
        //Arrange - four bars, then the generator stalls, then it carries on
        var time = new ManualTimeProvider();
        var library = Library("WaitingStallParts");
        var generator = new StallingMusicGenerator("StallingPiece", time, InTickOrder(TestMusic.Bars(8, Resolution)),
            8L * 4L * Resolution, Resolution);
        generator.StallFrom(4L * 4L * Resolution);
        MusicGeneratorRegistry.Register(generator);

        using var session = new MusicSession(Options("StallingPiece", library.Name), TestInstrumentLibraries.Lookup(),
            time);
        session.Play();

        var left = new float[SampleRate / 10];
        var right = new float[SampleRate / 10];

        //Act - play until the head has run out of the four bars, then let the music come back
        for (var step = 0; step < 400 && !(session.IsStarved && session.Position > TimeSpan.FromSeconds(4.0)); step++)
        {
            session.Renderer.Render(left, right);
            await Advance(time, TimeSpan.FromSeconds(0.1));
        }

        var waitingAt = session.Position;
        var heldNotes = new List<int>();

        generator.Resume();

        for (var step = 0; step < 400 && session.Position == waitingAt; step++)
        {
            session.Renderer.Render(left, right);

            if (session.Position == waitingAt)
            {
                heldNotes.Add(NotesStillOn(library));
            }

            await Advance(time, TimeSpan.FromSeconds(0.1));
        }

        //Assert - it did run out, and at no moment while it waited was a note left on
        waitingAt.Should().BeGreaterThan(TimeSpan.FromSeconds(4.0));
        heldNotes.Should().NotBeEmpty();
        heldNotes.Should().OnlyContain(count => count == 0);
        session.Position.Should().BeGreaterThan(waitingAt);
    }

    [Fact]
    public async Task a_short_finished_first_pass_is_not_heard_until_the_head_can_play_through_it()
    {
        //Arrange - a first piece of ONE bar (two seconds) against a five-second pre-roll, and a
        //next piece that takes twenty seconds to start: the head has to wait for it
        var time = new ManualTimeProvider();
        var library = Library("WaitingShortFirstParts");
        var generator = new SequenceMusicGenerator("ShortFirst", new[]
        {
            ReplayOnTestClock.On(ReplayMusicGenerator.FromMidiEvents("ShortFirst", TestMusic.Bars(1, Resolution)), time, 8.0),
            ReplayOnTestClock.On(ReplayMusicGenerator.FromMidiEvents("ShortFirst", TestMusic.Bars(8, Resolution)), time, 8.0)
        })
        {
            FirstItemDelay = TimeSpan.FromSeconds(20.0),
            Clock = time
        };
        MusicGeneratorRegistry.Register(generator);
        var options = Options("ShortFirst", library.Name);
        options.Preroll = TimeSpan.FromSeconds(5.0);
        options.GenerateAhead = TimeSpan.FromSeconds(30.0);
        options.SegmentPriming = SegmentPriming.Fresh;

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(), time);
        session.Play();

        var left = new float[SampleRate / 10];
        var right = new float[SampleRate / 10];
        var started = time.GetUtcNow();
        var silentWaits = 0;

        //Act - listen a tenth of a second at a time until the head moves
        for (var step = 0; step < 600 && session.Position == TimeSpan.Zero; step++)
        {
            session.Renderer.Render(left, right);

            if (session.Position == TimeSpan.Zero)
            {
                //Assert - silence, and no note on, for as long as the head has not moved
                left.Should().OnlyContain(sample => sample == 0.0F);
                NotesStillOn(library).Should().Be(0);
                silentWaits++;
            }

            await Advance(time, TimeSpan.FromSeconds(0.1));
        }

        //Assert - it waited through the slow second pass, then started within a pre-roll of it
        var waited = time.GetUtcNow() - started;
        silentWaits.Should().BeGreaterThan(150);
        session.Position.Should().BeGreaterThan(TimeSpan.Zero);
        waited.Should().BeLessThan(TimeSpan.FromSeconds(20.0) + options.Preroll);
        session.GenerationError.Should().BeNull();
    }

    private static TestInstrumentLibrary Library(string name)
    {
        var library = TestInstrumentLibrary.Complete(name);
        library.Register();

        return library;
    }

    private static MusicGenerationOptions Options(string generator, string library) =>
        new MusicGenerationOptions
        {
            ApplicationOwnsAudioOutput = true,
            Generator = generator,
            InstrumentLibrary = library,
            Preroll = TimeSpan.FromSeconds(1.0),
            GenerateAhead = TimeSpan.FromSeconds(2.0),
            SampleRate = SampleRate
        };

    private static async Task Advance(ManualTimeProvider time, TimeSpan by)
    {
        var until = time.GetUtcNow() + by;

        await EnginePump.RunUntilAsync(time, () => time.GetUtcNow() >= until);
    }

    // How many notes are sounding across every instrument the library built: every note-on that
    // has not had its note-off yet.
    private static int NotesStillOn(TestInstrumentLibrary library)
    {
        var on = 0;

        foreach (var synthesizer in library.Created)
        {
            var held = new Dictionary<string, int>();

            foreach (var message in synthesizer.Messages)
            {
                var parts = message.Split(' ');
                var key = parts[1] + " " + parts[2];
                var velocity = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);

                if (parts[0] == "90" && velocity > 0)
                {
                    held[key] = held.GetValueOrDefault(key) + 1;
                }
                else if (parts[0] == "80" || parts[0] == "90")
                {
                    held[key] = Math.Max(0, held.GetValueOrDefault(key) - 1);
                }
            }

            on += held.Values.Sum();
        }

        return on;
    }

    private static IReadOnlyList<MidiEvent> InTickOrder(MidiEventCollection music)
    {
        var events = new List<MidiEvent>();

        for (var track = 0; track < music.Tracks; track++)
        {
            foreach (var midiEvent in music.GetTrackEvents(track))
            {
                if (!MidiEvent.IsEndTrack(midiEvent) && !MidiEvent.IsNoteOff(midiEvent))
                {
                    events.Add(midiEvent);
                }
            }
        }

        return events.OrderBy(midiEvent => midiEvent.AbsoluteTime).ToArray();
    }
}
