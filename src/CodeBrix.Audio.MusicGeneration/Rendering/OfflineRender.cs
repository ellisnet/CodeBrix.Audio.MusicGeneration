using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.MusicGeneration.Rendering;

/// <summary>
/// One render of one piece of music to one file or stream: the same generator, the same engine and
/// the same routing synthesizer a session plays with, driven flat out with no audio device.
/// </summary>
/// <remarks>
/// <para>
/// TWO HALVES. <see cref="Prepare"/> settles everything that can be refused - what to play, what
/// to play it with, what format to write and whether the destination can take it - and it runs
/// BEFORE a single event is generated, so nobody waits minutes to be told the file name was wrong.
/// <see cref="RunAsync"/> then generates, renders and writes, in blocks, holding nothing more than
/// a block of audio at a time.
/// </para>
/// <para>
/// THE STREAMING LIFECYCLE IS OFF, and it is off in one place - see the engine's construction in
/// <see cref="RunAsync"/>. A render has no play head to protect: pacing is off on the request, the
/// generate-ahead window is replaced by "generate until there is enough music for the file", the
/// music is never held back to a later bar line, and the segment-at-a-time fallback cannot engage.
/// What reaches the file is tick for tick what the generator wrote.
/// </para>
/// </remarks>
internal sealed class OfflineRender
{
    // 4096 frames is about 93 ms of audio at 44.1 kHz: long enough that the per-block work is
    // nothing, short enough that the engine is pumped often and that a cancelled render stops at
    // once. It is also what bounds the memory a render of any length needs.
    private const int RenderChunkFrames = 4096;

    // How often progress is reported while audio is being written: about four times a second.
    private const double ProgressSeconds = 0.25;

    // How long the render waits when it has caught up with the generator. With a model behind the
    // music this is where a render spends its time; with a replay it is barely reached at all.
    private static readonly TimeSpan WaitingForMusic = TimeSpan.FromMilliseconds(1.0);

    // Music is generated a little beyond the target so that the last block of the file is real
    // music rather than the very edge of what has been written.
    private static readonly TimeSpan BeyondTheTarget = TimeSpan.FromSeconds(1.0);

    private readonly MusicGenerationOptions sessionOptions;
    private readonly MusicRenderOptions renderOptions;
    private readonly TimeProvider timeProvider;
    private readonly IMusicGenerator generator;
    private readonly IInstrumentLibrary library;
    private readonly MusicRendition rendition;
    private readonly MusicRequest request;
    private readonly RenderEnding ending;
    private readonly IAudioFileWriter writer;
    private readonly Stream output;
    private readonly bool ownsOutput;
    private readonly string path;
    private readonly string extension;
    private readonly List<string> diagnostics = new List<string>();

    private RenditionVoicer voicer;

    private OfflineRender(MusicGenerationOptions sessionOptions, MusicRenderOptions renderOptions,
        TimeProvider timeProvider, IMusicGenerator generator, IInstrumentLibrary library,
        MusicRendition rendition, MusicRequest request, RenderEnding ending, IAudioFileWriter writer,
        Stream output, bool ownsOutput, string path, string extension)
    {
        this.sessionOptions = sessionOptions;
        this.renderOptions = renderOptions;
        this.timeProvider = timeProvider;
        this.generator = generator;
        this.library = library;
        this.rendition = rendition;
        this.request = request;
        this.ending = ending;
        this.writer = writer;
        this.output = output;
        this.ownsOutput = ownsOutput;
        this.path = path;
        this.extension = extension;
    }

    /// <summary>
    /// Settles and refuses everything that can be settled and refused before anything is
    /// generated, and opens the file.
    /// </summary>
    /// <param name="sessionOptions">The session's own options: what to play and what with.</param>
    /// <param name="renderOptions">The render's options: how long, how it ends, how it is stored.</param>
    /// <param name="libraries">Where instrument libraries are found.</param>
    /// <param name="timeProvider">The clock the engine runs on.</param>
    /// <param name="path">The file to write, or null when writing to a caller's stream.</param>
    /// <param name="output">The caller's stream, or null when writing to a file.</param>
    /// <param name="fileNameOrExtension">What the format is chosen by.</param>
    /// <returns>A render ready to run.</returns>
    public static OfflineRender Prepare(MusicGenerationOptions sessionOptions,
        MusicRenderOptions renderOptions, IInstrumentLibraryLookup libraries,
        TimeProvider timeProvider, string path, Stream output, string fileNameOrExtension)
    {
        var options = renderOptions == null ? new MusicRenderOptions() : renderOptions.Clone();
        var ending = options.EndingOrDefault();

        if (!options.TargetLength.HasValue && ending != RenderEnding.NaturalStop)
        {
            throw new ArgumentException(
                $"A {(ending == RenderEnding.Fade ? "fade" : "hard cut")} needs a target length: it " +
                "is what the file is exactly as long as. Set MusicRenderOptions.TargetLength, or " +
                "choose RenderEnding.NaturalStop to render the generator's own ending.",
                nameof(renderOptions));
        }

        // FIRST, and before anything else is looked at: with no instruments there is no music,
        // whatever else the options say.
        if (!libraries.HasAny)
        {
            throw new InvalidOperationException(MusicSession.NoInstrumentLibraryMessage);
        }

        var resolvedGenerator = MusicGeneratorRegistry.Resolve(sessionOptions.Generator);
        var resolvedLibrary = libraries.Resolve(sessionOptions.InstrumentLibrary);
        var resolvedRendition = MusicRenditionRegistry.Resolve(sessionOptions.Rendition).Clone();

        if (!resolvedLibrary.SupportsPerPart)
        {
            throw new MusicGenerationException(
                $"The instrument library '{resolvedLibrary.Name}' does not make one instrument per " +
                "part, which is how a rendition gives each part its own voice and its own level. " +
                "Name a library that does.");
        }

        var request = BuildRequest(sessionOptions, options, resolvedGenerator, ending);

        // Refused here rather than deep inside the generation, so that a request the generator
        // cannot honour is an error on the line that asked for the render.
        MusicGeneratorCapabilities.EnsureHonoured(resolvedGenerator, request);

        // THE FORMAT IS THE FILE NAME'S. An extension nothing is registered for is CodeBrix.Audio's
        // own error, which names it and lists what IS registered - and it is raised here, before
        // the file is created and before a note is generated.
        var factory = AudioFileWriterRegistry.Resolve(fileNameOrExtension);
        var format = FormatFor(factory, options, sessionOptions.SampleRate);

        var stream = output;
        var ownsOutput = false;

        if (stream == null)
        {
            stream = File.Create(path);
            ownsOutput = true;
        }

        IAudioFileWriter writer;

        try
        {
            // A format that patches its header refuses a stream it cannot seek back through, in
            // CodeBrix.Audio's own words. Creating the writer here is what makes that the caller's
            // error rather than a surprise minutes into a render.
            writer = factory.Create(stream, format);
        }
        catch
        {
            if (ownsOutput)
            {
                stream.Dispose();
                Delete(path);
            }

            throw;
        }

        return new OfflineRender(sessionOptions, options, timeProvider, resolvedGenerator,
            resolvedLibrary, resolvedRendition, request, ending, writer, stream, ownsOutput, path,
            ExtensionOf(fileNameOrExtension));
    }

    /// <summary>Generates, renders and writes the file.</summary>
    /// <param name="progress">Where progress is reported, or null.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>What the render was.</returns>
    public async Task<MusicRenderResult> RunAsync(IProgress<MusicRenderProgress> progress,
        CancellationToken cancellationToken)
    {
        var stream = new MidiStream(request.TicksPerQuarterNote);
        var host = new OfflineMusicHost(sessionOptions.SampleRate);
        MusicEngine engine = null;
        var finished = false;

        try
        {
            host.Load(stream, rate =>
            {
                voicer = new RenditionVoicer(rendition, library, rate, request.InstrumentHints,
                    sessionOptions.MasterVolume);

                return voicer.Router;
            }, DeliverMessage);

            engine = new MusicEngine(generator, request, stream, voicer, host, TimeSpan.Zero,
                timeProvider, 0L)
            {
                // THE STREAMING LIFECYCLE IS OFF, AND THIS IS THE ONE PLACE IT IS TURNED OFF.
                // A render has no play head: nobody is listening, and the renderer takes exactly
                // what has been written as soon as it is written. So the music is never held back
                // to a later bar line (which would put rests into a rendered piece), the
                // segment-at-a-time fallback can never engage, and the generate-ahead window is
                // replaced by "generate until there is enough music for the file". Pacing is
                // already off on the request.
                KeepsMusicAheadOfTheHead = false,
                FallsBackToSegmentAtATime = false,
                EndOfPiece = ending == RenderEnding.NaturalStop
                    ? EndOfPiecePolicy.Stop
                    : EndOfPiecePolicy.KeepGenerating
            };

            engine.CanPull = () => MoreMusicIsWanted(engine);
            engine.Start();
            host.Play();

            var result = await RenderAsync(engine, host, stream, progress, cancellationToken)
                .ConfigureAwait(false);

            finished = true;

            return result;
        }
        finally
        {
            if (engine != null)
            {
                engine.Dispose();
            }

            host.Dispose();

            if (!finished)
            {
                AbandonTheFile();
            }
        }
    }

    private async Task<MusicRenderResult> RenderAsync(MusicEngine engine, OfflineMusicHost host,
        MidiStream stream, IProgress<MusicRenderProgress> progress,
        CancellationToken cancellationToken)
    {
        var rate = sessionOptions.SampleRate;
        var left = new float[RenderChunkFrames];
        var right = new float[RenderChunkFrames];
        var interleaved = new float[RenderChunkFrames * 2];

        var targetFrames = renderOptions.TargetLength.HasValue
            ? FramesOf(renderOptions.TargetLength.Value, rate)
            : -1L;
        var fadeFrames = FadeFrames(targetFrames, rate);
        var fadeFrom = targetFrames - fadeFrames;
        var ringOutFrames = FramesOf(renderOptions.RingOut, rate);
        var ringOutWritten = 0L;
        var faded = false;

        var report = new ProgressReporter(progress, renderOptions.TargetLength, rate,
            (long)(ProgressSeconds * rate));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            engine.Pump();

            if (targetFrames >= 0L && host.FramesWritten >= targetFrames)
            {
                break;
            }

            var want = RenderChunkFrames;

            if (targetFrames >= 0L)
            {
                want = (int)Math.Min(want, targetFrames - host.FramesWritten);
            }

            if (host.MusicHasEnded)
            {
                // THE MUSIC IS OVER AND ONLY THE RING-OUT IS LEFT. It stops as soon as nothing is
                // sounding, so a piece that ends cleanly adds nothing at all.
                if (host.ActiveVoiceCount == 0 || ringOutWritten >= ringOutFrames)
                {
                    break;
                }

                want = (int)Math.Min(want, ringOutFrames - ringOutWritten);
                ringOutWritten += want;
            }
            else if (!host.CanRender(want))
            {
                // Caught up with the generator: wait for it rather than write silence the music
                // does not have.
                report.Report(MusicRenderStage.Generating, host.FramesWritten, engine.SegmentCount);

                await Task.Delay(WaitingForMusic, cancellationToken).ConfigureAwait(false);

                continue;
            }
            else if (stream.IsCompleted)
            {
                // THE LAST BLOCK OF THE MUSIC IS THE LAST BLOCK OF THE FILE. Without this, a
                // render with no target length would write however much of a block of silence was
                // left over when it noticed the music had ended.
                var remaining = FramesOf(host.MusicLength, rate) - host.FramesWritten;

                if (remaining < want)
                {
                    // One block, never less: the head has to step PAST the last thing on the
                    // timeline for the sequencer to deliver it and call the music over.
                    want = (int)Math.Max(remaining, (long)host.BlockFrames);
                }
            }

            var firstFrame = host.FramesWritten;

            host.Render(left.AsSpan(0, want), right.AsSpan(0, want));

            for (var index = 0; index < want; index++)
            {
                var gain = 1.0F;

                if (fadeFrames > 0L && firstFrame + index >= fadeFrom)
                {
                    // THE FADE IS APPLIED AS THE AUDIO IS WRITTEN, because the file's length is
                    // known before the first frame of it: nothing has to be held back to fade it.
                    var into = firstFrame + index - fadeFrom;

                    gain = MusicFade.GainAt(renderOptions.FadeCurve,
                        fadeFrames <= 1L ? 1.0 : (double)into / (fadeFrames - 1L));
                    faded = true;
                }

                interleaved[index * 2] = left[index] * gain;
                interleaved[(index * 2) + 1] = right[index] * gain;
            }

            writer.Write(interleaved, 0, want * 2);

            report.Report(MusicRenderStage.Rendering, host.FramesWritten, engine.SegmentCount);
        }

        if (engine.GenerationError != null)
        {
            throw new MusicGenerationException(
                "The music could not be generated, so nothing was rendered: " +
                engine.GenerationError.Message, engine.GenerationError);
        }

        report.Report(MusicRenderStage.Writing, host.FramesWritten, engine.SegmentCount, true);

        engine.Stop();
        host.Stop();
        writer.Finish();

        if (ownsOutput)
        {
            output.Dispose();
        }

        var reachedTarget = targetFrames < 0L || host.FramesWritten >= targetFrames;

        if (!reachedTarget)
        {
            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                "The music ended after {0:mm\\:ss\\.fff}, before the target length of {1:mm\\:ss\\.fff}: " +
                "the generator stopped and could not be continued, so the file is as long as the " +
                "music.", TimeSpan.FromSeconds((double)host.FramesWritten / rate),
                renderOptions.TargetLength.GetValueOrDefault()));
        }

        if (ending == RenderEnding.Fade && !faded)
        {
            diagnostics.Add("No fade was applied: the music ended before the fade would have begun.");
        }

        report.Report(MusicRenderStage.Finished, host.FramesWritten, engine.SegmentCount, true);

        return new MusicRenderResult(path, extension, host.FramesWritten, rate, ending,
            renderOptions.TargetLength, reachedTarget, FadeApplied(fadeFrames, faded, rate),
            renderOptions.FadeCurve, Source(engine), engine.SegmentCount,
            SeamTicks(engine), stream.ToMidiEventCollection(), Diagnostics());
    }

    // RUNS ON THE RENDERING THREAD - which, in a render, is the thread doing the rendering - for
    // every message the sequencer delivers. It hands the routing table whatever has already been
    // built and then delivers the message: the hook REPLACES delivery, so a hook that forgets the
    // second line renders silence.
    private void DeliverMessage(IMidiSynthesizer synthesizer, int channel, int command, int data1,
        int data2)
    {
        var current = voicer;

        if (current != null)
        {
            current.ApplyPending(channel, command, data1);
        }

        synthesizer.ProcessMidiMessage(channel, command, data1, data2);
    }

    // GENERATE FLAT OUT UNTIL THERE IS ENOUGH MUSIC FOR THE FILE, and then stop. It replaces the
    // generate-ahead window completely: a window measured against a play head means nothing here,
    // and generating a piece longer than the file is work nobody asked for.
    private bool MoreMusicIsWanted(MusicEngine engine)
    {
        if (!renderOptions.TargetLength.HasValue)
        {
            return true;
        }

        return engine.SettledThroughTime < renderOptions.TargetLength.Value + BeyondTheTarget;
    }

    private long FadeFrames(long targetFrames, int rate)
    {
        if (ending != RenderEnding.Fade || targetFrames <= 0L)
        {
            return 0L;
        }

        var frames = FramesOf(renderOptions.FadeLength, rate);

        if (frames > targetFrames)
        {
            // A FADE LONGER THAN THE FILE IS CLAMPED, not refused: the whole file fades, which is
            // what the caller asked for as nearly as it can be given, and a render that has taken
            // minutes is not thrown away over it.
            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                "The fade of {0:mm\\:ss\\.fff} is longer than the file, so it was clamped to the " +
                "whole of it ({1:mm\\:ss\\.fff}).", renderOptions.FadeLength,
                TimeSpan.FromSeconds((double)targetFrames / rate)));

            frames = targetFrames;
        }

        return frames;
    }

    private TimeSpan FadeApplied(long fadeFrames, bool faded, int rate) =>
        faded ? TimeSpan.FromSeconds((double)fadeFrames / rate) : TimeSpan.Zero;

    private ActiveMusicSource Source(MusicEngine engine)
    {
        var playing = engine.ActiveGenerator;

        return new ActiveMusicSource(playing.Name, playing.Family, playing.Description, library.Name,
            rendition.Name, voicer.Snapshot());
    }

    private string[] Diagnostics()
    {
        var all = new List<string>(diagnostics);

        // Why a part is not what was asked for belongs to the render as much as to the voicing.
        all.AddRange(voicer.Snapshot().Diagnostics);

        return all.ToArray();
    }

    private void AbandonTheFile()
    {
        // NOTHING HALF-WRITTEN IS EVER CLAIMED AS GOOD. The writer is deliberately NOT finished: a
        // WAV or an AIFF only becomes a valid file when its header is patched on the way out, so
        // an unfinished one cannot be mistaken for a complete render. A file this render created
        // is deleted outright; a stream the caller supplied is theirs, and is left as it stands.
        if (!ownsOutput)
        {
            return;
        }

        try
        {
            output.Dispose();
        }
        catch (IOException)
        {
            // Nothing can be done about it, and the failure being reported is the real one.
        }

        Delete(path);
    }

    private static long[] SeamTicks(MusicEngine engine)
    {
        var starts = engine.SegmentStartTicks;
        var ticks = new long[starts.Count];

        for (var index = 0; index < starts.Count; index++)
        {
            ticks[index] = starts[index];
        }

        return ticks;
    }

    private static MusicRequest BuildRequest(MusicGenerationOptions sessionOptions,
        MusicRenderOptions renderOptions, IMusicGenerator generator, RenderEnding ending)
    {
        var copy = sessionOptions.Request.Clone();

        if (sessionOptions.InferenceThreadCount.HasValue)
        {
            copy.InferenceThreadCount = sessionOptions.InferenceThreadCount;
        }

        // OFFLINE RENDERING GENERATES FLAT OUT. Pacing is what makes a replay behave like a
        // producer writing into a timeline that is being listened to; there is nothing to listen
        // to here, and a render that took as long as the music would be a strange thing to offer.
        copy.PaceInRealTime = false;

        var wantsALength = ending != RenderEnding.NaturalStop && renderOptions.TargetLength.HasValue;

        if (wantsALength && copy.Intent != null && copy.Intent.TargetLength.HasValue)
        {
            // THE CALLER'S OWN HINT IS NEVER TOUCHED. Nothing a consumer put in a request is
            // stripped, replaced or quietly improved.
            return copy;
        }

        if (wantsALength &&
            (generator.Honours & MusicRequestFeatures.TargetLength) == MusicRequestFeatures.TargetLength)
        {
            // A generator that DECLARES it honours a target length is told what the file's length
            // is, as a hint. One that does not is never given something it would have to refuse:
            // the length is the render's business, and it is reached by continuing the music.
            if (copy.Intent == null)
            {
                copy.Intent = new MusicIntent();
            }

            copy.Intent.TargetLength = renderOptions.TargetLength;
        }

        return copy;
    }

    private static WaveFormat FormatFor(IAudioFileWriterFactory factory, MusicRenderOptions options,
        int sampleRate)
    {
        if (!options.BitsPerSample.HasValue)
        {
            // The format's own default: 32-bit float for a .wav, 16-bit PCM for an .aiff.
            return factory.DefaultFormat(sampleRate, 2);
        }

        return options.BitsPerSample.Value == 32
            ? WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2)
            : new WaveFormat(sampleRate, options.BitsPerSample.Value, 2);
    }

    private static long FramesOf(TimeSpan time, int rate) =>
        (long)Math.Round(time.TotalSeconds * rate, MidpointRounding.AwayFromZero);

    private static string ExtensionOf(string fileNameOrExtension)
    {
        var extension = Path.GetExtension(fileNameOrExtension);

        if (string.IsNullOrEmpty(extension))
        {
            extension = fileNameOrExtension.StartsWith('.')
                ? fileNameOrExtension
                : "." + fileNameOrExtension;
        }

        return extension.ToLowerInvariant();
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A partial file that cannot be removed is not worth replacing the real failure with.
        }
    }

    /// <summary>
    /// What reports progress, and the one place that decides how often it is worth reporting.
    /// </summary>
    private sealed class ProgressReporter
    {
        private readonly IProgress<MusicRenderProgress> progress;
        private readonly TimeSpan? targetLength;
        private readonly int rate;
        private readonly long everyFrames;

        private MusicRenderStage lastStage = MusicRenderStage.Generating;
        private long lastFrames = -1L;
        private bool reported;

        public ProgressReporter(IProgress<MusicRenderProgress> progress, TimeSpan? targetLength,
            int rate, long everyFrames)
        {
            this.progress = progress;
            this.targetLength = targetLength;
            this.rate = rate;
            this.everyFrames = everyFrames < 1L ? 1L : everyFrames;
        }

        public void Report(MusicRenderStage stage, long frames, int segmentCount) =>
            Report(stage, frames, segmentCount, false);

        public void Report(MusicRenderStage stage, long frames, int segmentCount, bool always)
        {
            if (progress == null)
            {
                return;
            }

            if (!always && reported)
            {
                if (stage == lastStage && frames - lastFrames < everyFrames)
                {
                    // Too soon to be worth saying again.
                    return;
                }

                if (stage != lastStage && frames == lastFrames)
                {
                    // Nothing has been written since the last report, so nothing has happened that
                    // a caller has not already been told.
                    return;
                }
            }

            lastStage = stage;
            lastFrames = frames;
            reported = true;

            var music = TimeSpan.FromSeconds((double)frames / rate);

            progress.Report(new MusicRenderProgress(stage, music, targetLength,
                FractionOf(stage, music), segmentCount));
        }

        private double FractionOf(MusicRenderStage stage, TimeSpan music)
        {
            if (stage == MusicRenderStage.Finished)
            {
                return 1.0;
            }

            if (!targetLength.HasValue || targetLength.Value <= TimeSpan.Zero)
            {
                return 0.0;
            }

            var fraction = music.TotalSeconds / targetLength.Value.TotalSeconds;

            return fraction < 0.0 ? 0.0 : (fraction > 1.0 ? 1.0 : fraction);
        }
    }
}
