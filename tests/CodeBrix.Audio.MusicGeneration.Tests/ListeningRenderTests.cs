using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE RENDERS A PERSON LISTENS TO. It writes audio files - it makes no sound of its own - into a
/// git-ignored TestResults folder inside this repository, with an INDEX.txt describing every one
/// of them and giving THE TIME OF EVERY SEAM.
/// </summary>
/// <remarks>
/// <para>
/// GATED BY TIME AND BY SIZE, and by both model variables. It renders about twenty-five minutes of
/// audio and writes a few hundred megabytes. Opt in with CODEBRIX_AUDIO_RUN_LISTENING_RENDERS=1.
/// </para>
/// <code>
/// CODEBRIX_AUDIO_RUN_LISTENING_RENDERS=1 \
///   CODEBRIX_AUDIO_MUSICGEN_MUPT_MODEL=... CODEBRIX_AUDIO_MUSICGEN_SKYTNT_BUNDLE=... \
///   dotnet tests/CodeBrix.Audio.MusicGeneration.Tests/bin/Release/net10.0/CodeBrix.Audio.MusicGeneration.Tests.dll \
///   -class "CodeBrix.Audio.MusicGeneration.Tests.ListeningRenderTests"
/// </code>
/// <para>
/// IT IS ONE TEST ON PURPOSE. Every file goes in one folder under one index, and an index written
/// by three tests running in an order nobody chose is not an index.
/// </para>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class ListeningRenderTests : IDisposable
{
    /// <summary>The variable that opts in to writing the listening set.</summary>
    public const string VariableName = "CODEBRIX_AUDIO_RUN_LISTENING_RENDERS";

    private const int SampleRate = 44100;
    private const int BitsPerSample = 16;
    private const int FirstSeed = 20260921;
    private const int SecondSeed = 20260922;

    /// <summary>How long a preset is rendered for.</summary>
    private static readonly TimeSpan OneMinute = TimeSpan.FromSeconds(60.0);

    /// <summary>How long a seam render is: long enough to cross two seams and be listened to.</summary>
    private static readonly TimeSpan SeamRenderLength = TimeSpan.FromSeconds(110.0);

    /// <summary>How long a follow-up render waits for music before it gives up.</summary>
    private static readonly TimeSpan HowLongToWaitForMusic = TimeSpan.FromMinutes(5.0);

    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable(VariableName) == "1";

    private const string SkipReason =
        "Set " + VariableName + "=1, with both model variables, to write the listening renders.";

    // THE PASS LENGTHS THE SEAM RENDERS USE, AND WHY THEY ARE NOT THE DEFAULTS. A default SkyTNT
    // pass is about a hundred seconds of music and a default MuPT pass thirty to sixty, so a
    // two-minute render at the defaults would cross one seam or none - and the whole point of
    // this set is to hear several. These two numbers put a seam about every half minute. Every
    // OTHER render in this file uses the shipped defaults, and the index says which is which.
    private const int SkyTNTSeamPassEvents = 300;
    private const int MuPTSeamPassTokens = 288;

    private readonly MuPTMusicGenerator mupt;
    private readonly SkyTNTMusicGenerator skytnt;

    /// <summary>
    /// Starts from freshly built built-ins, over generators built with THE SHIPPED DEFAULTS.
    /// </summary>
    /// <remarks>
    /// IT DOES NOT USE THE LIVE-TEST FIXTURES: those cap a pass at a few dozen events so that the
    /// ordinary suite stays quick, and what a listener needs to hear is what a consumer gets.
    /// Building a generator loads nothing.
    /// </remarks>
    public ListeningRenderTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();

        mupt = MuPTModelFile.IsAvailable
            ? new MuPTMusicGenerator("MuPTListening", MuPTModelFile.Path)
            : null;

        skytnt = SkyTNTModelBundle.IsAvailable
            ? new SkyTNTMusicGenerator("SkyTNTListening", SkyTNTModelBundle.Directory)
            : null;
    }

    /// <summary>Gives both models' memory back when the class is finished with them.</summary>
    public void Dispose()
    {
        mupt?.Dispose();
        skytnt?.Dispose();
    }

    [Fact]
    public async Task the_listening_set_is_written_with_an_index_of_every_seam()
    {
        Assert.SkipUnless(Enabled, SkipReason);
        Assert.SkipUnless(MuPTModelFile.IsAvailable, MuPTModelFile.SkipReason);
        Assert.SkipUnless(SkyTNTModelBundle.IsAvailable, SkyTNTModelBundle.SkipReason);

        //Arrange
        TestInstrumentLibraries.GeneralMidi();

        var folder = ListeningFolder();
        var index = new StringBuilder();
        var written = new List<string>();

        Heading(index, folder);

        //Act - every preset, for about a minute, through the voicing its music was rated with
        index.AppendLine("THE PRESETS, one minute each, AT THE SHIPPED DEFAULTS");
        index.AppendLine(new string('-', 78));
        index.AppendLine(
            "Nothing is capped here: each generator was built with its own default pass length, " +
            "so the number of segments below is what a consumer really gets.");
        index.AppendLine();

        foreach (var preset in MuPTPresets.All)
        {
            written.Add(await RenderPreset(mupt, preset, FirstSeed, null, folder, index));
        }

        foreach (var preset in SkyTNTPresets.All)
        {
            written.Add(await RenderPreset(skytnt, preset, FirstSeed, null, folder,
                index));
            written.Add(await RenderPreset(skytnt, preset, SecondSeed, null, folder,
                index));
        }

        //... then the seam set, in which each model continues its own piece across two seams
        index.AppendLine();
        index.AppendLine("THE SEAM SET - each model continuing its own piece");
        index.AppendLine(new string('-', 78));
        index.AppendLine(
            "Every seam is a BAR LINE. The continuation was given the last " +
            StreamingDefaults.ContinuationTailBars.ToString(CultureInfo.InvariantCulture) +
            " bars of the music before it, and nothing of that tail is played twice.");
        index.AppendLine(
            "THESE FOUR ASK FOR A SHORTER PASS THAN THE SHIPPED DEFAULT, and each says by how " +
            "much: at the default a two-minute render crosses one seam or none, and the point of " +
            "this set is to hear several. EVERY OTHER RENDER IN THIS FOLDER USES THE SHIPPED " +
            "DEFAULTS.");
        index.AppendLine();

        written.Add(await RenderSeams(mupt, MuPTPresets.WaltzDuetInAMinor, FirstSeed,
            MuPTSeamPassTokens, folder, index));
        written.Add(await RenderSeams(mupt, MuPTPresets.WaltzDuetInAMinor, SecondSeed,
            MuPTSeamPassTokens, folder, index));
        written.Add(await RenderSeams(skytnt, SkyTNTPresets.ClubArrangement, FirstSeed,
            SkyTNTSeamPassEvents, folder, index));
        written.Add(await RenderSeams(skytnt, SkyTNTPresets.AmbientElectronica,
            SecondSeed, SkyTNTSeamPassEvents, folder, index));

        //... and one render per model in which a follow-up prompt changes the music mid-piece
        index.AppendLine();
        index.AppendLine("A FOLLOW-UP PROMPT, changing the music in mid-piece");
        index.AppendLine(new string('-', 78));
        index.AppendLine(
            "The offline render has no way to change a prompt part-way through, so these two are " +
            "rendered through the device-less host - the same road a game uses - with the audio " +
            "written to the file as it is pulled.");
        index.AppendLine();

        written.Add(await RenderFollowUp(mupt, MuPTPresets.WaltzDuetInAMinor,
            MuPTPresets.JigInD, MuPTSeamPassTokens, folder, index));
        written.Add(await RenderFollowUp(skytnt, SkyTNTPresets.AmbientElectronica,
            SkyTNTPresets.ClubArrangement, SkyTNTSeamPassEvents, folder, index));

        File.WriteAllText(Path.Combine(folder, "INDEX.txt"), index.ToString());

        //Assert - every file named in the index is really there and is really audio
        written.Should().HaveCount(20);

        foreach (var file in written)
        {
            var path = Path.Combine(folder, file);

            File.Exists(path).Should().BeTrue(file + " was not written");

            using var reader = new WaveFileReader(path);

            reader.SampleCount.Should().BeGreaterThan(0L, file + " holds no audio");
        }

        TestContext.Current.TestOutputHelper.WriteLine("Listening renders written to " + folder);
    }

    // --- the three kinds of render ---------------------------------------------------------------

    private async Task<string> RenderPreset(IMusicGenerator generator, MusicPreset preset,
        int seed, int? cap, string folder, StringBuilder index)
    {
        var name = FileName(preset, seed, "minute");
        var result = await Render(generator, preset, seed, cap, OneMinute,
            Path.Combine(folder, name));

        index.AppendLine(Line(name, preset, seed, result));

        return name;
    }

    private async Task<string> RenderSeams(IMusicGenerator generator, MusicPreset preset, int seed,
        int cap, string folder, StringBuilder index)
    {
        var name = FileName(preset, seed, "seams");
        var path = Path.Combine(folder, name);
        var result = await Render(generator, preset, seed, cap, SeamRenderLength, path);

        result.SegmentCount.Should().BeGreaterThanOrEqualTo(3,
            name + " has to cross at least two seams");

        index.AppendLine(Line(name, preset, seed, result));
        index.AppendLine("      pass length asked for: " +
                         cap.ToString(CultureInfo.InvariantCulture) +
                         (preset.Family == MuPTMusicGenerator.MuPTFamily
                             ? " tokens - SHORTER THAN THE DEFAULT " +
                               MuPTGeneratorOptions.DefaultMaximumTokensPerPass.ToString(
                                   CultureInfo.InvariantCulture) + ", so that several seams fall " +
                               "inside a render worth sitting through"
                             : " events - SHORTER THAN THE DEFAULT " +
                               SkyTNTGeneratorOptions.DefaultMaximumEventsPerPass.ToString(
                                   CultureInfo.InvariantCulture) + ", so that several seams fall " +
                               "inside a render worth sitting through"));

        var music = new MusicTimeline(result.Music);

        for (var segment = 1; segment < result.SeamTicks.Count; segment++)
        {
            var tick = result.SeamTicks[segment];

            index.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      SEAM {0} at {1:mm\\:ss} - bar {2}, {3:0.#} bpm, tick {4}, given the last " +
                "{5} bars", segment, music.TimeOf(tick), music.BarAt(tick), music.TempoAt(tick),
                tick, StreamingDefaults.ContinuationTailBars));
        }

        return name;
    }

    private async Task<string> RenderFollowUp(IMusicGenerator generator, MusicPreset first,
        MusicPreset second, int cap, string folder, StringBuilder index)
    {
        var name = Safe(first.Family) + "-followup-" + Safe(first.Name) + "-then-" +
                   Safe(second.Name) + ".wav";

        var changeAt = TimeSpan.FromSeconds(30.0);
        var length = TimeSpan.FromSeconds(60.0);

        MusicGeneratorRegistry.ResetForTesting();
        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions
        {
            Generator = generator.Name,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Rendition = first.SuggestedRendition ?? BuiltInRenditions.Automatic,
            ApplicationOwnsAudioOutput = true,
            SampleRate = SampleRate,
            Preroll = TimeSpan.FromSeconds(2.0),
            Request = Requested(first, FirstSeed, cap)
        };

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        var started = Stopwatch.GetTimestamp();

        session.Play();

        var block = SampleRate / 10;
        var left = new float[block];
        var right = new float[block];
        var interleaved = new float[block * 2];
        var blockTime = TimeSpan.FromSeconds((double)block / SampleRate);
        var atTime = TimeSpan.Zero;
        var changed = false;

        var factory = AudioFileWriterRegistry.Resolve(name);

        using (var file = File.Create(Path.Combine(folder, name)))
        {
            var writer = factory.Create(file, new WaveFormat(SampleRate, BitsPerSample, 2));

            using (writer as IDisposable)
            {
                while (atTime < length)
                {
                    // THE HEAD MOVES ONLY AS AUDIO IS PULLED, so the render runs as fast as the
                    // model can fill the timeline rather than in real time - but it never pulls
                    // music that has not been written, which would be a silence nobody asked for.
                    var wanted = atTime + TimeSpan.FromSeconds(2.0);

                    await WaitFor(() => session.Stream.HorizonTime >= wanted ||
                                        session.GenerationError != null || session.IsFinished);

                    session.GenerationError.Should().BeNull();
                    session.Renderer.Render(left, right);

                    for (var frame = 0; frame < block; frame++)
                    {
                        interleaved[frame * 2] = left[frame];
                        interleaved[(frame * 2) + 1] = right[frame];
                    }

                    writer.Write(interleaved, 0, block * 2);

                    session.Pump();

                    atTime += blockTime;

                    if (!changed && atTime >= changeAt)
                    {
                        session.FollowUp(Requested(second, SecondSeed, cap));
                        changed = true;
                    }
                }
            }
        }

        var took = Stopwatch.GetElapsedTime(started);

        index.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0}\n      {1}, {2:mm\\:ss}, {3} segment(s); {4} until {5:mm\\:ss}, then {6}; " +
            "rendered in {7:mm\\:ss}",
            name, first.Family, atTime, session.Diagnostics.SegmentCount, first.Name, changeAt,
            second.Name, took));

        return name;
    }

    // --- the offline render every file but the follow-ups is made with --------------------------

    private async Task<MusicRenderResult> Render(IMusicGenerator generator, MusicPreset preset,
        int seed, int? cap, TimeSpan length, string path)
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicGeneratorRegistry.Register(generator);

        var options = new MusicGenerationOptions
        {
            Generator = generator.Name,
            InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Rendition = preset.SuggestedRendition ?? BuiltInRenditions.Automatic,
            SampleRate = SampleRate,
            Request = Requested(preset, seed, cap)
        };

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        var render = new MusicRenderOptions
        {
            TargetLength = length,
            BitsPerSample = BitsPerSample
        };

        return await session.RenderToFileAsync(path, render, TestContext.Current.CancellationToken);
    }

    private static MusicRequest Requested(MusicPreset preset, int seed, int? cap)
    {
        var request = preset.CreateRequest();

        request.Seed = seed;

        if (cap.HasValue)
        {
            request.Controls.MaximumEvents = cap.Value;
        }

        return request;
    }

    // --- the index -----------------------------------------------------------------------------

    private static void Heading(StringBuilder index, string folder)
    {
        index.AppendLine("LISTENING RENDERS - CodeBrix.Audio.MusicGeneration");
        index.AppendLine(new string('=', 78));
        index.AppendLine();
        index.AppendLine("Every file here is 16-bit stereo WAV at " +
                         SampleRate.ToString(CultureInfo.InvariantCulture) +
                         " Hz, synthesized through the General MIDI instrument library " +
                         "\"ModestSynthGm\".");
        index.AppendLine("Written to: " + folder);
        index.AppendLine();
        index.AppendLine("A rendition named beside a file is the voicing that piece's music was " +
                         "rated through; \"Automatic\" means no listening session rated one.");
        index.AppendLine();
    }

    private static string Line(string name, MusicPreset preset, int seed,
        MusicRenderResult result) =>
        string.Format(CultureInfo.InvariantCulture,
            "{0}\n      {1}, seed {2}, {3}, {4:mm\\:ss\\.fff}, {5} segment(s){6}",
            name, preset.Family, seed,
            preset.SuggestedRendition ?? BuiltInRenditions.Automatic, result.Duration,
            result.SegmentCount, preset.IsProvisional ? " - PROVISIONAL preset" : string.Empty);

    private static string FileName(MusicPreset preset, int seed, string what) =>
        Safe(preset.Family) + "-" + Safe(preset.Name) + "-" + what + "-seed" +
        seed.ToString(CultureInfo.InvariantCulture) + ".wav";

    private static string Safe(string name) => name.Replace(' ', '-');

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

    /// <summary>
    /// The git-ignored folder the listening set is written to, INSIDE this repository.
    /// </summary>
    private static string ListeningFolder()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);

        while (at != null &&
               !File.Exists(Path.Combine(at.FullName, "CodeBrix.Audio.MusicGeneration.slnx")))
        {
            at = at.Parent;
        }

        var root = at == null ? AppContext.BaseDirectory : at.FullName;
        var folder = Path.Combine(root, "TestResults", "listening-renders");

        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>Where a tick falls in a finished render: the time, the bar and the tempo.</summary>
    private sealed class MusicTimeline
    {
        private readonly List<MeterSpan> meters = new List<MeterSpan>();
        private readonly List<TempoSpan> tempi = new List<TempoSpan>();
        private readonly SettledTempoMap map;
        private readonly int resolution;

        public MusicTimeline(MidiEventCollection music)
        {
            resolution = music.DeltaTicksPerQuarterNote;
            map = new SettledTempoMap(resolution);

            foreach (var track in music)
            {
                foreach (var item in track)
                {
                    if (item is TempoEvent tempo)
                    {
                        map.Add(tempo.AbsoluteTime, tempo.MicrosecondsPerQuarterNote);
                        tempi.Add(new TempoSpan(tempo.AbsoluteTime,
                            60000000.0 / tempo.MicrosecondsPerQuarterNote));
                    }
                    else if (item is TimeSignatureEvent signature && signature.Numerator >= 1 &&
                             signature.Denominator >= 0 && signature.Denominator <= 7)
                    {
                        meters.Add(new MeterSpan(signature.AbsoluteTime, signature.Numerator,
                            1 << signature.Denominator));
                    }
                }
            }

            meters.Sort((left, right) => left.Tick.CompareTo(right.Tick));
            tempi.Sort((left, right) => left.Tick.CompareTo(right.Tick));

            if (meters.Count == 0 || meters[0].Tick > 0L)
            {
                meters.Insert(0, new MeterSpan(0L, 4, 4));
            }
        }

        public TimeSpan TimeOf(long tick) => map.TimeOfTick(tick);

        public double TempoAt(long tick)
        {
            var found = 120.0;

            for (var index = 0; index < tempi.Count && tempi[index].Tick <= tick; index++)
            {
                found = tempi[index].BeatsPerMinute;
            }

            return found;
        }

        /// <summary>Which bar of the piece a tick falls in, counting the first bar as 1.</summary>
        public long BarAt(long tick)
        {
            var bars = 0L;

            for (var index = 0; index < meters.Count; index++)
            {
                var from = meters[index].Tick;

                if (from > tick)
                {
                    break;
                }

                var until = index + 1 < meters.Count && meters[index + 1].Tick <= tick
                    ? meters[index + 1].Tick
                    : tick;

                var barTicks = (long)meters[index].BeatsPerBar * 4L * resolution /
                               meters[index].BeatNoteValue;

                if (barTicks > 0L)
                {
                    bars += (until - from) / barTicks;
                }
            }

            return bars + 1L;
        }

        private readonly struct MeterSpan
        {
            public MeterSpan(long tick, int beatsPerBar, int beatNoteValue)
            {
                Tick = tick;
                BeatsPerBar = beatsPerBar;
                BeatNoteValue = beatNoteValue;
            }

            public long Tick { get; }

            public int BeatsPerBar { get; }

            public int BeatNoteValue { get; }
        }

        private readonly struct TempoSpan
        {
            public TempoSpan(long tick, double beatsPerMinute)
            {
                Tick = tick;
                BeatsPerMinute = beatsPerMinute;
            }

            public long Tick { get; }

            public double BeatsPerMinute { get; }
        }
    }
}
