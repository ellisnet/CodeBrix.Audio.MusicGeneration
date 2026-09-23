using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.SkyTNT;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// EVERY PRESET, AGAINST THE REAL MODEL IT WAS WRITTEN FOR: each one writes music, and each one
/// comes out of the loudspeaker path as something other than silence.
/// </summary>
/// <remarks>
/// <para>
/// Both models use their published packages and run in the ordinary suite.
/// EVERY PASS HERE IS SHORT - a few bars or a few dozen events - because a suite is run over and
/// over. Nothing here makes a sound: the audio goes to arrays this test reads.
/// </para>
/// <para>
/// THE PRESETS THEMSELVES DO NOT NAME A GENERATOR, so each test registers one and specifies it -
/// which is the same two steps an application takes.
/// </para>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class PresetLiveTests : IClassFixture<MuPTLoadedModel>, IClassFixture<SkyTNTLoadedModel>
{
    private const int SampleRate = 44100;

    private static readonly TimeSpan HowLongToWaitForMusic = TimeSpan.FromMinutes(3.0);

    private readonly MuPTLoadedModel mupt;
    private readonly SkyTNTLoadedModel skytnt;

    /// <summary>Starts from freshly built built-ins, over the models the class loaded once.</summary>
    /// <param name="mupt">The MuPT model, loaded once for the whole class.</param>
    /// <param name="skytnt">The SkyTNT model, loaded once for the whole class.</param>
    public PresetLiveTests(MuPTLoadedModel mupt, SkyTNTLoadedModel skytnt)
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();

        this.mupt = mupt;
        this.skytnt = skytnt;
    }

    [Theory]
    [InlineData("ReelInGMinor")]
    [InlineData("JigInD")]
    [InlineData("WaltzInAMinor")]
    [InlineData("AirInDMixolydian")]
    [InlineData("HornpipeInG")]
    [InlineData("OpenInC")]
    [InlineData("DuetInC")]
    [InlineData("WaltzDuetInAMinor")]
    public async Task a_MuPT_preset_writes_a_few_bars_of_music(string name)
    {
        //Arrange
        var preset = MuPTPresets.Find(name);
        var request = preset.CreateRequest();

        request.Seed = 20260921;
        request.Controls.MaximumEvents = MuPTLoadedModel.ShortPass;

        //Act
        var notes = Notes(await Pull(mupt.Generator, request));

        //Assert - real notes, at ticks inside the segment
        notes.Should().NotBeEmpty();
        notes[0].AbsoluteTime.Should().BeGreaterThanOrEqualTo(0L);
    }

    [Theory]
    [InlineData("FourOnTheFloor")]
    [InlineData("ClubArrangement")]
    [InlineData("AmbientElectronica")]
    public async Task a_SkyTNT_preset_writes_a_few_dozen_events(string name)
    {
        //Arrange
        var request = SkyTNTPresets.Find(name).CreateRequest();

        request.Seed = 20260921;
        request.Controls.MaximumEvents = SkyTNTLoadedModel.ShortPass;

        //Act
        var notes = Notes(await Pull(skytnt.Generator, request));

        //Assert
        notes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task a_preset_that_asks_for_no_percussion_writes_none()
    {
        //Arrange - the generator is built WITH a kit, and the preset asks for none
        using var generator = new SkyTNTMusicGenerator("SkyTNTNoKit",
            SkyTNTModel.ModelDirectory,
            new SkyTNTGeneratorOptions
            {
                MaximumEventsPerPass = SkyTNTLoadedModel.ShortPass, DrumKit = 0
            });

        var request = SkyTNTPresets.AmbientElectronica.CreateRequest();

        request.Seed = 20260921;

        //Act
        var notes = Notes(await Pull(generator, request));

        //Assert - channel 10 is the percussion channel as this library counts channels
        notes.Should().NotBeEmpty();

        foreach (var note in notes)
        {
            note.Channel.Should().NotBe(10);
        }
    }

    [Fact]
    public async Task a_preset_that_asks_for_a_kit_gets_percussion()
    {
        //Arrange - the generator is built with NO kit, and the preset asks for one
        var request = SkyTNTPresets.FourOnTheFloor.CreateRequest();

        request.Seed = 20260921;
        request.Controls.MaximumEvents = 250;

        //Act
        var notes = Notes(await Pull(skytnt.Generator, request));

        //Assert
        var percussion = 0;

        foreach (var note in notes)
        {
            if (note.Channel == 10)
            {
                percussion++;
            }
        }

        percussion.Should().BeGreaterThan(0,
            "the request asked for a kit although the generator was built without one");
    }

    [Fact]
    public async Task a_voice_count_of_two_really_comes_back_as_two_parts()
    {
        //Arrange - an intent and nothing else: no notation is written by the caller at all
        var request = new MusicRequest { Seed = 20260921 };

        request.Intent = new MusicIntent
        {
            Key = "D",
            Mode = MusicMode.Dorian,
            Meter = new MusicMeter(4, 4),
            VoiceCount = 2
        };

        request.Intent.CharacterWords.Add("gentle");
        request.Controls.MaximumEvents = MuPTLoadedModel.ShortPass;

        //Act
        var notes = Notes(await Pull(mupt.Generator, request));

        //Assert - two parts really sound, which is the whole point of a voice count: a header
        //alone comes back as one line of melody
        var channels = new SortedSet<int>();

        foreach (var note in notes)
        {
            channels.Add(note.Channel);
        }

        notes.Should().NotBeEmpty();
        channels.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task the_best_rated_MuPT_preset_renders_two_parts_that_are_not_silence()
    {
        //Arrange
        var preset = MuPTPresets.WaltzDuetInAMinor;

        //Act
        var rendered = await RenderThrough(mupt.Generator, preset, MuPTLoadedModel.ShortPass);

        //Assert
        rendered.Peak.Should().BeGreaterThan(0.001F);
        rendered.Parts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Theory]
    [InlineData("FourOnTheFloor")]
    [InlineData("ClubArrangement")]
    [InlineData("AmbientElectronica")]
    public async Task an_electronica_preset_renders_audio_that_is_not_silence(string name)
    {
        //Act
        var rendered = await RenderThrough(skytnt.Generator, SkyTNTPresets.Find(name), 250);

        //Assert
        rendered.Peak.Should().BeGreaterThan(0.001F);
        rendered.Parts.Should().BeGreaterThanOrEqualTo(1);
    }

    // --- the rig ------------------------------------------------------------------------------

    private async Task<Rendered> RenderThrough(IMusicGenerator generator, MusicPreset preset,
        int cap)
    {
        TestInstrumentLibraries.GeneralMidi();
        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions
        {
            Generator = generator.Name,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Rendition = preset.SuggestedRendition ?? BuiltInRenditions.Automatic,
            ApplicationOwnsAudioOutput = true,
            SampleRate = SampleRate,
            Preroll = TimeSpan.FromSeconds(1.0),
            EndOfPiece = EndOfPiecePolicy.Stop
        };

        var request = preset.CreateRequest();

        request.Seed = 20260921;
        request.Controls.MaximumEvents = cap;
        options.Request = request;

        using var session = new MusicSession(options);

        session.Play();

        await WaitFor(() => session.Stream.HorizonTime >= TimeSpan.FromSeconds(2.0) ||
                            session.GenerationError != null);

        session.GenerationError.Should().BeNull();

        var peak = 0.0F;
        var left = new float[SampleRate];
        var right = new float[SampleRate];

        for (var second = 0; second < 2; second++)
        {
            session.Renderer.Render(left, right);
            peak = Math.Max(peak, Peak(left, right));
        }

        return new Rendered(peak, session.Diagnostics.Voicing.Parts.Count);
    }

    private static async Task<IReadOnlyList<GeneratedMusicEvent>> Pull(IMusicGenerator from,
        MusicRequest request)
    {
        var produced = new List<GeneratedMusicEvent>();

        await foreach (var item in from.GenerateAsync(request,
                           TestContext.Current.CancellationToken))
        {
            produced.Add(item);
        }

        return produced;
    }

    private static IReadOnlyList<NoteOnEvent> Notes(IReadOnlyList<GeneratedMusicEvent> produced)
    {
        var notes = new List<NoteOnEvent>();

        foreach (var item in produced)
        {
            if (item.HasEvent && item.Event is NoteOnEvent note)
            {
                notes.Add(note);
            }
        }

        return notes;
    }

    private static float Peak(float[] left, float[] right)
    {
        var peak = 0.0F;

        for (var index = 0; index < left.Length; index++)
        {
            peak = Math.Max(peak, Math.Max(Math.Abs(left[index]), Math.Abs(right[index])));
        }

        return peak;
    }

    private sealed class Rendered
    {
        public Rendered(float peak, int parts)
        {
            Peak = peak;
            Parts = parts;
        }

        public float Peak { get; }

        public int Parts { get; }
    }

    private static async Task WaitFor(Func<bool> until)
    {
        var started = Stopwatch.GetTimestamp();

        while (!until())
        {
            if (Stopwatch.GetElapsedTime(started) > HowLongToWaitForMusic)
            {
                throw new TimeoutException("The music never arrived.");
            }

            await Task.Yield();
        }
    }
}
