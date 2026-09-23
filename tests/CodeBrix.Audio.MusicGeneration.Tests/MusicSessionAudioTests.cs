using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The whole road, end to end, with NO audio device: music is generated, voiced, written to a
/// timeline and rendered to samples the test looks at.
/// </summary>
/// <remarks>
/// The application-owns-its-output mode is what makes this possible: the play head moves only as
/// audio is pulled, so the test decides how far the music has been heard and nothing sleeps.
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MusicSessionAudioTests
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;
    private const string ProgramChangePiece = "ProgramChangePiece";

    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicSessionAudioTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public async Task the_embedded_replay_makes_real_sound_through_the_General_MIDI_library()
    {
        //Arrange
        TestInstrumentLibraries.GeneralMidi();
        var time = new ManualTimeProvider();

        ReplayOnTestClock.Registered(EmbeddedReplay.Midi, time);

        var options = new MusicGenerationOptions
        {
            ApplicationOwnsAudioOutput = true,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Preroll = TimeSpan.FromSeconds(0.25),
            SampleRate = SampleRate
        };

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(), time);
        session.Play();

        //Act - buffer what the commit window allows, then listen to two seconds of it
        await EnginePump.RunUntilAsync(time,
            () => session.Stream.HorizonTime >= TimeSpan.FromSeconds(1.5));

        var peak = 0.0F;
        var left = new float[SampleRate];
        var right = new float[SampleRate];

        for (var second = 0; second < 2; second++)
        {
            session.Renderer.Render(left, right);
            peak = Math.Max(peak, Peak(left, right));
            time.Advance(TimeSpan.FromSeconds(1.0));
            session.Pump();
        }

        //Assert
        peak.Should().BeGreaterThan(0.001F);
        session.Position.Should().BeGreaterThan(TimeSpan.Zero);
        session.ActiveSource.Voicing.Parts.Should().NotBeEmpty();
        session.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task the_whole_embedded_piece_reaches_the_timeline_and_the_timeline_completes()
    {
        //Arrange
        var generator = MusicGeneratorRegistry.Resolve(null);
        var expected = await GeneratedAsync(generator);

        var time = new ManualTimeProvider();
        var stream = new MidiStream(Resolution);
        var host = new FakeMusicHost(SampleRate);
        var voicer = new RenditionVoicer(MusicRenditionRegistry.Resolve(null).Clone(),
            TestInstrumentLibraries.Complete(), SampleRate, null, 1.0F);

        host.Load(stream, _ => voicer.Router, (synthesizer, channel, command, data1, data2) =>
            synthesizer.ProcessMidiMessage(channel, command, data1, data2));
        host.FollowTheMusic();

        using var engine = new MusicEngine(generator, new MusicRequest { PaceInRealTime = false },
            stream, voicer, host, TimeSpan.FromSeconds(1.0), time, 0L);

        // A head that never falls behind sits at the end of the music that has been WRITTEN.
        host.EndOfTheMusic = () => engine.WrittenThroughTime;
        engine.Start();

        //Act
        await EnginePump.RunUntilAsync(time, () => stream.IsCompleted);

        //Assert
        Committed(stream).Should().Equal(expected);
        stream.IsCompleted.Should().BeTrue();
        stream.LateEventCount.Should().Be(0);
        engine.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task a_program_change_part_way_through_re_voices_that_part_at_its_own_tick()
    {
        //Arrange - a bar of four notes, with the music changing instrument half way through it
        var library = TestInstrumentLibraries.Complete();
        var created = library.Created.Count;

        var time = new ManualTimeProvider();

        MusicGeneratorRegistry.Register(ReplayOnTestClock.On(
            ReplayMusicGenerator.FromMidiEvents(ProgramChangePiece, TwoInstrumentsInOneBar()), time));

        var options = new MusicGenerationOptions
        {
            ApplicationOwnsAudioOutput = true,
            Generator = ProgramChangePiece,
            InstrumentLibrary = TestInstrumentLibraries.CompleteName,
            Preroll = TimeSpan.FromSeconds(0.2),
            SampleRate = SampleRate,
            EndOfPiece = EndOfPiecePolicy.Stop
        };

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(), time);
        session.Play();

        await EnginePump.RunUntilAsync(time, () => session.Stream.IsCompleted);

        //Act - listen to the first second, which is the two notes before the change
        Render(session, 0.9);
        var first = Built(library, created, (int)GeneralMidiProgram.Celesta);
        var second = Built(library, created, (int)GeneralMidiProgram.Vibraphone);
        var beforeTheChange = new[] { first.NoteOnCount, second.NoteOnCount };

        // ... and then the rest of the bar, which is the two notes after it
        Render(session, 0.8);

        //Assert
        beforeTheChange.Should().Equal(2, 0);
        first.NoteOnCount.Should().Be(2);
        second.NoteOnCount.Should().Be(2);
        session.ActiveSource.Voicing.Parts[0].Program.Should().Be((int)GeneralMidiProgram.Vibraphone);
    }

    private static MidiEventCollection TwoInstrumentsInOneBar()
    {
        var music = new MidiEventCollection(1, Resolution);
        music.AddTrack();
        music.AddEvent(new TimeSignatureEvent(0L, 4, 2, 24, 8), 0);
        music.AddEvent(new TempoEvent(500000, 0L), 0);
        music.AddEvent(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.Celesta), 0);
        music.AddEvent(new PatchChangeEvent(960L, 1, (int)GeneralMidiProgram.Vibraphone), 0);

        for (var beat = 0; beat < 4; beat++)
        {
            var note = new NoteOnEvent(beat * (long)Resolution, 1, 60 + beat, 100, Resolution / 2);
            music.AddEvent(note, 0);
            music.AddEvent(note.OffEvent, 0);
        }

        return music;
    }

    private static TestSynthesizer Built(TestInstrumentLibrary library, int from, int program) =>
        library.Created.Skip(from).First(synthesizer => synthesizer.Program == program);

    private static void Render(MusicSession session, double seconds)
    {
        var frames = (int)(seconds * SampleRate);
        var left = new float[frames];
        var right = new float[frames];

        session.Renderer.Render(left, right);
    }

    private static float Peak(float[] left, float[] right)
    {
        var peak = 0.0F;

        for (var i = 0; i < left.Length; i++)
        {
            peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
        }

        return peak;
    }

    private static async Task<IReadOnlyList<string>> GeneratedAsync(IMusicGenerator generator)
    {
        var produced = new List<string>();
        var request = new MusicRequest { PaceInRealTime = false };

        await foreach (var item in generator.GenerateAsync(request, TestContext.Current.CancellationToken))
        {
            if (item.HasEvent)
            {
                produced.Add(Describe(item.Event));
            }
        }

        produced.Sort(StringComparer.Ordinal);

        return produced;
    }

    private static IReadOnlyList<string> Committed(MidiStream stream)
    {
        var recording = stream.ToMidiEventCollection();
        var described = new List<string>();

        for (var track = 0; track < recording.Tracks; track++)
        {
            foreach (var midiEvent in recording.GetTrackEvents(track))
            {
                if (midiEvent == null || MidiEvent.IsNoteOff(midiEvent) ||
                    MidiEvent.IsEndTrack(midiEvent))
                {
                    continue;
                }

                described.Add(Describe(midiEvent));
            }
        }

        described.Sort(StringComparer.Ordinal);

        return described;
    }

    private static string Describe(MidiEvent midiEvent)
    {
        if (midiEvent is NoteOnEvent note)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|note|{1}|{2}|{3}|{4}",
                note.AbsoluteTime, note.Channel, note.NoteNumber, note.Velocity, note.NoteLength);
        }

        if (midiEvent is PatchChangeEvent patch)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|patch|{1}|{2}",
                patch.AbsoluteTime, patch.Channel, patch.Patch);
        }

        if (midiEvent is TempoEvent tempo)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|tempo|{1}",
                tempo.AbsoluteTime, tempo.MicrosecondsPerQuarterNote);
        }

        if (midiEvent is ControlChangeEvent control)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|control|{1}|{2}|{3}",
                control.AbsoluteTime, control.Channel, (int)control.Controller, control.ControllerValue);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", midiEvent.AbsoluteTime,
            midiEvent.GetType().Name, midiEvent.Channel);
    }
}
