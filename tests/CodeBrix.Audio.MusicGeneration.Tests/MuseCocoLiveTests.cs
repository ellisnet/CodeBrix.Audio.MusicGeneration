using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;
using CodeBrix.Audio.MusicGeneration.Rendering;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>Opt-in playback proof using caller-staged MuseCoco weights and the public runner package.</summary>
[Collection("MusicGeneratorRegistry")]
public sealed class MuseCocoLiveTests
{
    private readonly ITestOutputHelper output;

    public MuseCocoLiveTests(ITestOutputHelper output) { this.output = output; }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task the_runner_decodes_the_real_model_directly_without_the_MusicGeneration_adapter(bool streaming)
    {
        var directory = Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(directory), "Set CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE to a staged music bundle.");
        using var model = await CodeBrix.Ollama.ModelRunner.MuseCocoMusicModel.LoadFromDirectoryAsync(directory,
            new CodeBrix.Ollama.ModelRunner.OnnxRunnerOptions { Threads = 4 }, TestContext.Current.CancellationToken);
        var attributes = model.Schema.CreateAttributes().With("instrument.piano", "present").With("tempo", "moderate");
        var options = new CodeBrix.Ollama.ModelRunner.MuseCocoGenerationOptions
        {
            MaximumTokens = 512, MinimumTokens = 512, Seed = 20260921, TopK = 15, TopP = 1, Temperature = 1
        };
        var tokens = 0;
        var notes = 0;
        var events = 0;
        var lastTick = -1L;
        var timer = Stopwatch.StartNew();
        try
        {
            if (streaming)
            {
                await foreach (var item in model.GenerateStreamingAsync(attributes, options,
                                   new TokenProgress(count => tokens = count), TestContext.Current.CancellationToken))
                {
                    events++;
                    lastTick = item.Tick;
                    if (item.Kind == CodeBrix.Ollama.ModelRunner.MidiEventKind.Note) notes++;
                }
            }
            else
            {
                var result = await model.GenerateAsync(attributes, options, new TokenProgress(count => tokens = count),
                    TestContext.Current.CancellationToken);
                events = result.Score.Events.Count;
                notes = result.Score.Events.Count(item => item.Kind == CodeBrix.Ollama.ModelRunner.MidiEventKind.Note);
            }
            notes.Should().BeGreaterThan(0);
        }
        finally
        {
            output.WriteLine($"Direct runner: streaming={streaming}, seconds={timer.Elapsed.TotalSeconds}, tokens={tokens}, events={events}, notes={notes}, lastTick={lastTick}");
        }
    }

    [Fact]
    public async Task staged_MuseCoco_rendered_with_a_session_tempo_plays_every_carried_piece_at_that_tempo()
    {
        //Arrange - fresh pieces on the real model, crossfaded, carried outside the band
        var directory = Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(directory), "Set CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE to a staged music bundle.");
        MusicGeneratorRegistry.ResetForTesting();
        TestInstrumentLibraries.GeneralMidi();
        using var generator = new MuseCocoMusicGenerator("MuseCocoTempoLive", directory,
            new MuseCocoGeneratorOptions
            {
                MaximumTokensPerPass = 512, MinimumTokensPerPass = 0,
                InferenceThreadCount = Math.Max(1, Math.Min(16, Environment.ProcessorCount / 2))
            });
        MusicGeneratorRegistry.Register(generator);
        var request = new MusicRequest
        {
            Seed = 20260923, Controls = new MusicGenerationControls { TopK = 15, TopP = 1, Temperature = 1 }
        };
        request.ModelAttributes["instrument.piano"] = "present";
        using var folder = new RenderOutputFolder("musecoco-session-tempo");
        using var session = new MusicSession(new MusicGenerationOptions
        {
            Generator = generator.Name, InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Request = request, SegmentPriming = SegmentPriming.Fresh,
            SeamCrossfade = TimeSpan.FromSeconds(4.0), TempoPolicy = SessionTempoPolicy.CarryOutsideBand,
            SampleRate = 22050
        });

        //Act
        var result = await session.RenderToFileAsync(folder.File("tempo.wav"), new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(60.0), Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken);

        //Assert - from every carried piece's start to the next seam, the only tempo is the session's
        var tempos = result.Music.SelectMany(track => track).OfType<TempoEvent>().ToArray();
        var decisions = result.Diagnostics.Where(line => line.StartsWith("Piece at tick", StringComparison.Ordinal)).ToArray();
        foreach (var line in decisions) output.WriteLine(line);
        foreach (var tempo in tempos) output.WriteLine($"tempo at {tempo.AbsoluteTime}: {60000000.0 / tempo.MicrosecondsPerQuarterNote:0.0} bpm");

        decisions.Should().NotBeEmpty();
        var seams = result.SeamTicks.ToList();

        foreach (var line in decisions.Where(line => line.Contains("carried to the session tempo")))
        {
            var tick = long.Parse(line.Split(' ')[3].TrimEnd(':'), System.Globalization.CultureInfo.InvariantCulture);
            var sessionTempo = double.Parse(line.Substring(line.LastIndexOf("of ", StringComparison.Ordinal) + 3).Split(' ')[0],
                System.Globalization.CultureInfo.InvariantCulture);
            var next = seams.Where(seam => seam > tick).DefaultIfEmpty(long.MaxValue).Min();

            tempos.Where(tempo => tempo.AbsoluteTime >= tick && tempo.AbsoluteTime < next)
                .Should().AllSatisfy(tempo =>
                    (60000000.0 / tempo.MicrosecondsPerQuarterNote).Should().BeApproximately(sessionTempo, 0.1));
        }
    }

    [Fact]
    public async Task staged_MuseCoco_six_minute_render_with_a_session_tempo_always_finishes()
    {
        //Arrange - THE ACCEPTANCE CASE of the render that hung: long passes with a minimum, fresh
        //pieces crossfaded and carried outside the band, six minutes to the file
        var directory = Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(directory) &&
                          Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS") == "1",
            "Set CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE and CODEBRIX_AUDIO_RUN_LONG_RENDER_TESTS=1 for the long MuseCoco render.");
        MusicGeneratorRegistry.ResetForTesting();
        TestInstrumentLibraries.GeneralMidi();
        using var generator = new MuseCocoMusicGenerator("MuseCocoLongLive", directory,
            new MuseCocoGeneratorOptions
            {
                ExperimentalContinuation = false, MaximumTokensPerPass = 4096, MinimumTokensPerPass = 3072,
                InferenceThreadCount = Math.Max(1, Math.Min(16, Environment.ProcessorCount))
            });
        MusicGeneratorRegistry.Register(generator);
        var request = new MusicRequest
        {
            Seed = 20260923, Controls = new MusicGenerationControls { TopK = 15, TopP = 1, Temperature = 1 }
        };
        request.ModelAttributes["instrument.synthesizer"] = "present";
        request.ModelAttributes["instrument.drum"] = "present";
        request.ModelAttributes["genre.electronic"] = "present";
        request.ModelAttributes["danceable"] = "yes";
        request.ModelAttributes["tempo"] = "fast";
        using var folder = new RenderOutputFolder("musecoco-long-session-tempo");
        using var session = new MusicSession(new MusicGenerationOptions
        {
            Generator = generator.Name, InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            Request = request, SegmentPriming = SegmentPriming.Fresh,
            SeamCrossfade = TimeSpan.FromSeconds(4.0), TempoPolicy = SessionTempoPolicy.CarryOutsideBand
        });
        CodeBrix.Audio.MusicGeneration.Streaming.MusicEngine engine = null;
        CodeBrix.Audio.MusicGeneration.Rendering.OfflineRender.EngineCreatedForTesting = created => engine = created;
        var timer = Stopwatch.StartNew();

        try
        {
            //Act - with a watchdog: the state every half minute, and a failure if nothing moves for
            //five minutes
            var render = session.RenderToFileAsync(folder.File("long.wav"), new MusicRenderOptions
            {
                TargetLength = TimeSpan.FromSeconds(360.0), Ending = RenderEnding.HardCut
            }, TestContext.Current.CancellationToken);
            var lastState = string.Empty;
            var unchangedSince = timer.Elapsed;

            while (!render.IsCompleted)
            {
                await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(30.0), TestContext.Current.CancellationToken));

                var state = engine == null ? "no engine yet" : engine.DescribeState();
                output.WriteLine($"{timer.Elapsed.TotalSeconds:0}s {state}");

                if (state != lastState)
                {
                    lastState = state;
                    unchangedSince = timer.Elapsed;
                }

                (timer.Elapsed - unchangedSince).Should().BeLessThan(TimeSpan.FromMinutes(5.0),
                    "a render must never stop making progress: " + state);
            }

            var result = await render;

            //Assert
            foreach (var line in result.Diagnostics) output.WriteLine(line);
            output.WriteLine($"Rendered {result.Duration} in {timer.Elapsed.TotalMinutes:0.0} min; seams {string.Join(", ", result.SeamTicks)}");
            result.Duration.Should().Be(TimeSpan.FromSeconds(360.0));
            result.ReachedTargetLength.Should().BeTrue();
        }
        finally
        {
            CodeBrix.Audio.MusicGeneration.Rendering.OfflineRender.EngineCreatedForTesting = null;
        }
    }

    private sealed class TokenProgress : IProgress<int>
    {
        private readonly Action<int> report;
        public TokenProgress(Action<int> report) { this.report = report; }
        public void Report(int value) => report(value);
    }

    [Fact]
    public async Task staged_MuseCoco_accepts_natural_language_with_explicit_attribute_overrides()
    {
        // Arrange
        var music = Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE");
        var text = Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_TEXT_BUNDLE");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(music) && !string.IsNullOrWhiteSpace(text),
            "Set the MuseCoco music and text bundle environment variables for real text prompting.");
        using var generator = new MuseCocoMusicGenerator("MuseCocoTextLive", music,
            new MuseCocoGeneratorOptions
            {
                TextBundleDirectory = text, MaximumTokensPerPass = 128,
                MinimumTokensPerPass = 128, InferenceThreadCount = 4
            });
        var request = new MusicRequest
        {
            Text = "A calm piece led by piano", Seed = 20260921,
            Controls = new MusicGenerationControls { Temperature = 1, TopK = 15, TopP = 1 }
        };
        request.ModelAttributes["instrument.piano"] = "present";
        request.ModelAttributes["tempo"] = "moderate";
        await generator.PreloadAsync(TestContext.Current.CancellationToken);
        generator.IsTextModelLoaded.Should().BeFalse();
        var notes = 0;

        // Act
        await foreach (var item in generator.GenerateAsync(request, TestContext.Current.CancellationToken))
            if (item.Event is NoteOnEvent) notes++;

        // Assert
        generator.IsTextModelLoaded.Should().BeTrue();
        notes.Should().BeGreaterThan(0);
        generator.Release();
        generator.IsLoaded.Should().BeFalse();
        generator.IsTextModelLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task staged_MuseCoco_experimental_sections_share_one_ordered_timeline()
    {
        // Arrange
        var directory = Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(directory), "Set CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE to a staged music bundle.");
        using var generator = new MuseCocoMusicGenerator("ExperimentalLive", directory, new MuseCocoGeneratorOptions
        {
            ExperimentalContinuation = true, ExperimentalContextBars = 4, ExperimentalSectionTokens = 192,
            MaximumTokensPerPass = 320, MinimumTokensPerPass = 0, InferenceThreadCount = 4
        });
        var request = new MusicRequest
        {
            Seed = 20260921, Controls = new MusicGenerationControls { TopK = 15, TopP = 1, Temperature = 1 }
        };
        request.ModelAttributes["instrument.piano"] = "present";
        request.ModelAttributes["tempo"] = "moderate";
        var settled = -1L;
        var firstBoundary = -1L;
        var laterNotes = 0;
        var programs = new Dictionary<int, int>();

        // Act and assert: consume the same GeneratedMusicEvent contract as MusicSession.
        await foreach (var item in generator.GenerateAsync(request, TestContext.Current.CancellationToken))
        {
            if (item.HasEvent) item.Event.AbsoluteTime.Should().BeGreaterThan(settled);
            if (!item.HasEvent && firstBoundary < 0) firstBoundary = item.SettledThroughTick;
            if (item.Event is PatchChangeEvent patch)
            {
                programs.ContainsKey(patch.Channel).Should().BeFalse("continuation keeps its original channel assignments");
                programs.Add(patch.Channel, patch.Patch);
            }
            if (item.Event is NoteOnEvent note)
            {
                programs.ContainsKey(note.Channel).Should().BeTrue();
                if (firstBoundary >= 0 && note.AbsoluteTime > firstBoundary) laterNotes++;
            }
            item.SettledThroughTick.Should().BeGreaterThanOrEqualTo(settled);
            settled = item.SettledThroughTick;
        }
        laterNotes.Should().BeGreaterThan(0);
        output.WriteLine($"Experimental continuation: {laterNotes} notes after first section, final settled tick {settled}.");
    }

    [Fact]
    public async Task staged_MuseCoco_produces_audio_before_its_single_pass_finishes()
    {
        var directory = Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(directory), "Set CODEBRIX_AUDIO_MUSICGEN_MUSECOCO_BUNDLE to a staged music bundle.");
        MusicGeneratorRegistry.ResetForTesting();
        TestInstrumentLibraries.GeneralMidi();
        using var generator = new MuseCocoMusicGenerator("MuseCocoLive", directory,
            new MuseCocoGeneratorOptions { MaximumTokensPerPass = 512, MinimumTokensPerPass = 512, InferenceThreadCount = 4 });
        var timer = Stopwatch.StartNew();
        await generator.PreloadAsync(TestContext.Current.CancellationToken);
        output.WriteLine("Load seconds: " + timer.Elapsed.TotalSeconds);
        MusicGeneratorRegistry.Register(generator);
        var request = new MusicRequest
        {
            Seed = 20260921,
            Controls = new MusicGenerationControls { TopK = 15, TopP = 1, Temperature = 1 }
        };
        request.ModelAttributes["instrument.piano"] = "present";
        request.ModelAttributes["tempo"] = "moderate";
        using var session = new MusicSession(new MusicGenerationOptions
        {
            Generator = generator.Name, InstrumentLibrary = TestInstrumentLibraries.GeneralMidiName,
            ApplicationOwnsAudioOutput = true, Request = request, EndOfPiece = EndOfPiecePolicy.Stop,
            Preroll = TimeSpan.FromSeconds(0.5), SampleRate = 44100
        });
        timer.Restart();
        session.Play();
        var left = new float[2048];
        var right = new float[2048];
        var peak = 0F;
        var blockDuration = TimeSpan.FromSeconds((double)left.Length / 44100);
        while (timer.Elapsed < TimeSpan.FromMinutes(5) && peak <= 0.001F && session.GenerationError == null)
        {
            session.Renderer.Render(left, right);
            peak = Math.Max(peak, Math.Max(left.Max(Math.Abs), right.Max(Math.Abs)));
            await Task.Delay(blockDuration, TestContext.Current.CancellationToken);
        }
        output.WriteLine("First audio seconds: " + timer.Elapsed.TotalSeconds + "; peak: " + peak + "; " + session.Diagnostics);
        session.GenerationError.Should().BeNull();
        peak.Should().BeGreaterThan(0.001F);
        session.Stream.IsCompleted.Should().BeFalse("later bars must still be generated in this single pass");
        var horizonAtFirstAudio = session.Stream.HorizonTicks;
        // Application-owned output advances only when rendered. Keep pulling through normal
        // completion so the engine can commit later music relative to the advancing play head.
        while (timer.Elapsed < TimeSpan.FromMinutes(5) && !session.IsFinished && session.GenerationError == null)
        {
            session.Renderer.Render(left, right);
            await Task.Delay(blockDuration, TestContext.Current.CancellationToken);
        }
        session.GenerationError.Should().BeNull();
        session.Stream.IsCompleted.Should().BeTrue();
        session.IsFinished.Should().BeTrue();
        session.Stream.HorizonTicks.Should().BeGreaterThan(horizonAtFirstAudio);
        session.Diagnostics.LateEventCount.Should().Be(0);
        session.Diagnostics.StarvationGapCount.Should().Be(0);
        output.WriteLine("Later horizon: " + session.Stream.HorizonTicks + "; peak working set MiB: " + Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0);
        session.Stop();
    }
}
