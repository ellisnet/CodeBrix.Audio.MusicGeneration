using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Audio.MusicGeneration.Models;

/// <summary>
/// Streams music from a caller-staged MuseCoco bundle through ModelRunner's managed ONNX runtime.
/// Registration loads nothing. No ModelManager, Python, download or staging step runs in the application.
/// </summary>
/// <remarks>
/// Named attributes come from <see cref="Schema"/> and override the optional text model's predictions.
/// Each request starts fresh; arbitrary MIDI primers and MusicRequest.Continuation are refused.
/// Options.ExperimentalContinuation uses recent MuseCoco token context across sections WITHIN a
/// request, on one timeline. The session can join requests on bar lines. A generation holds its models until enumeration
/// completes or is disposed; release or disposal during loading or generation is refused.
/// </remarks>
public sealed class MuseCocoMusicGenerator : IMusicGenerator, IDisposable
{
    /// <summary>The registry family name for MuseCoco generators.</summary>
    public const string MuseCocoFamily = "MuseCoco";

    private readonly object gate = new object();
    private readonly string musicDirectory;
    private readonly IReadOnlyDictionary<string, string> musicFiles;
    private readonly IReadOnlyDictionary<string, string> textFiles;
    private readonly MuseCocoGeneratorOptions options;
    private MuseCocoMusicModel music;
    private MuseCocoTextModel text;
    private int loadedThreadCount;
    private bool busy;
    private bool disposed;

    /// <summary>Constructs a lazy generator from a staged music directory and optional text directory.</summary>
    /// <param name="name">The name used to register and select this generator.</param>
    /// <param name="bundleDirectory">Directory containing musecoco.json and its music assets.</param>
    /// <param name="options">Load and generation settings; null uses defaults.</param>
    public MuseCocoMusicGenerator(string name, string bundleDirectory, MuseCocoGeneratorOptions options = null)
    {
        RequireName(name);
        if (string.IsNullOrWhiteSpace(bundleDirectory)) throw new ArgumentException("A music bundle directory is required.", nameof(bundleDirectory));
        Name = name;
        musicDirectory = Path.GetFullPath(bundleDirectory);
        this.options = (options ?? new MuseCocoGeneratorOptions()).Clone();
        NormalizeTextDirectory();
    }

    /// <summary>Constructs a lazy generator from logical filenames mapped to physical paths.</summary>
    /// <param name="name">The name used to register and select this generator.</param>
    /// <param name="bundleFiles">The complete music bundle, including external graph data.</param>
    /// <param name="textBundleFiles">Optional complete text bundle; null disables text prompting.</param>
    /// <param name="options">Load and generation settings; do not also specify a text directory.</param>
    public MuseCocoMusicGenerator(string name, IReadOnlyDictionary<string, string> bundleFiles,
        IReadOnlyDictionary<string, string> textBundleFiles = null, MuseCocoGeneratorOptions options = null)
    {
        RequireName(name);
        Name = name;
        musicFiles = CopyFiles(bundleFiles ?? throw new ArgumentNullException(nameof(bundleFiles)));
        textFiles = textBundleFiles == null ? null : CopyFiles(textBundleFiles);
        this.options = (options ?? new MuseCocoGeneratorOptions()).Clone();
        if (textFiles != null && !string.IsNullOrWhiteSpace(this.options.TextBundleDirectory))
            throw new ArgumentException("Specify a text bundle file map or directory, not both.", nameof(options));
        NormalizeTextDirectory();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => MuseCocoFamily;

    /// <inheritdoc />
    public string Description => "Streams MIDI from staged MuseCoco attributes and optional natural-language prompts.";

    /// <summary>
    /// Categorical attributes, sampling, token cap, seed and inference threads. Free text is supported
    /// when a text bundle is supplied. Generic intent, instrument programs, primers and continuation
    /// are refused: use the schema's categorical attributes for musical conditioning.
    /// </summary>
    public MusicRequestFeatures Honours => MusicRequestFeatures.ModelAttributes | MusicRequestFeatures.Seed |
        MusicRequestFeatures.SamplingControls | MusicRequestFeatures.MaximumEvents |
        MusicRequestFeatures.InferenceThreadCount | (HasTextBundle ? MusicRequestFeatures.FreeText : MusicRequestFeatures.None);

    /// <inheritdoc />
    public bool IsLoaded { get { lock (gate) return music != null; } }

    /// <summary>Whether the optional text classifier has been loaded by a text request.</summary>
    public bool IsTextModelLoaded { get { lock (gate) return text != null; } }

    /// <summary>The loaded music model's immutable attribute schema, or null before loading.</summary>
    public MusicAttributeSchema Schema { get { lock (gate) return music?.Schema; } }

    /// <summary>Thread count of the loaded models, or null when released.</summary>
    public int? LoadedThreadCount { get { lock (gate) return music == null ? null : loadedThreadCount; } }

    private bool HasTextBundle => textFiles != null || !string.IsNullOrWhiteSpace(options.TextBundleDirectory);

    /// <inheritdoc />
    public async Task PreloadAsync(CancellationToken cancellationToken)
    {
        BeginUse();
        try { await LoadMusicAsync(options.InferenceThreadCount, cancellationToken).ConfigureAwait(false); }
        finally { EndUse(); }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request, CancellationToken cancellationToken)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        MusicGeneratorCapabilities.EnsureHonoured(this, request);
        if (request.Controls.RepetitionPenalty != MusicGenerationControls.NoRepetitionPenalty)
            throw new MusicRequestNotHonouredException("MuseCoco does not apply a repetition penalty.", Name, MusicRequestFeatures.SamplingControls);
        if (request.Controls.TopK == 0)
            throw new MusicRequestNotHonouredException("MuseCoco requires a positive top-k; zero is not supported.", Name, MusicRequestFeatures.SamplingControls);
        if (request.Seed < 0) throw new ArgumentOutOfRangeException(nameof(request), "MuseCoco requires a nonnegative seed.");
        return Generate(request.Clone(), cancellationToken);
    }

    private async IAsyncEnumerable<GeneratedMusicEvent> Generate(MusicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        BeginUse();
        try
        {
            await LoadMusicAsync(request.InferenceThreadCount ?? options.InferenceThreadCount, cancellationToken).ConfigureAwait(false);
            var attributes = music.Schema.CreateAttributes();
            if (!string.IsNullOrWhiteSpace(request.Text))
            {
                await LoadTextAsync(cancellationToken).ConfigureAwait(false);
                attributes = (await text.PredictAsync(request.Text, cancellationToken).ConfigureAwait(false)).Attributes;
            }
            foreach (var pair in request.ModelAttributes) attributes = attributes.With(pair.Key, pair.Value);

            var maximum = request.Controls.MaximumEvents ?? options.MaximumTokensPerPass;
            var generation = new MuseCocoGenerationOptions
            {
                MaximumTokens = maximum,
                MinimumTokens = Math.Min(options.MinimumTokensPerPass, maximum),
                Seed = request.Seed,
                Temperature = request.Controls.Temperature,
                TopK = request.Controls.TopK,
                TopP = request.Controls.TopP
            };
            var pass = new MuseCocoPass(request.TicksPerQuarterNote);
            if (options.ExperimentalContinuation)
            {
                // Context never escapes this enumeration. Interruption invalidates it upstream,
                // and a later request creates a fresh one while reusing the loaded model.
                var context = music.CreateContinuation(options.ExperimentalContextBars);
                var remaining = maximum;
                var section = 0L;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var available = music.MaximumGenerationTokens - context.ContextTokenCount;
                    if (available < 1)
                        throw new MusicGenerationException("MuseCoco continuation context fills its position capacity. Reduce ExperimentalContextBars.");
                    generation.MaximumTokens = Math.Min(remaining, Math.Min(options.ExperimentalSectionTokens, available));
                    generation.MinimumTokens = Math.Min(options.MinimumTokensPerPass, generation.MaximumTokens);
                    generation.Seed = request.Seed.HasValue ? request.Seed.Value + section : null;
                    var before = context.NextTick;
                    await foreach (var item in music.GenerateContinuationStreamingAsync(context, attributes, generation,
                                       cancellationToken: cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // The runner already assigns absolute timestamps and stable channels.
                        // Do not add a section offset or drain the piece between sections.
                        yield return pass.Accept(item);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    var boundary = pass.SettleBefore(context.NextTick);
                    if (boundary != null) yield return boundary;
                    if (context.LastGeneratedTokenCount == 0 || context.NextTick <= before) break;
                    remaining -= context.LastGeneratedTokenCount;
                    section++;
                }
            }
            else
            {
                await foreach (var item in music.GenerateStreamingAsync(attributes, generation,
                                   cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return pass.Accept(item);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var finished = pass.Finish();
            if (finished != null) yield return finished;
        }
        finally { EndUse(); }
    }

    private async Task LoadMusicAsync(int threads, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (music != null && loadedThreadCount == threads) return;
            ReleaseLoaded();
        }
        MuseCocoMusicModel built;
        try
        {
            var runner = new OnnxRunnerOptions { Threads = threads };
            built = musicFiles == null
                ? await MuseCocoMusicModel.LoadFromDirectoryAsync(musicDirectory, runner, cancellationToken).ConfigureAwait(false)
                : await MuseCocoMusicModel.LoadFromFilesAsync(musicFiles, runner, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelRunnerException error)
        {
            throw new MusicGenerationException($"The MuseCoco music bundle for '{Name}' could not be loaded: {error.Message}", error);
        }
        lock (gate) { music = built; loadedThreadCount = threads; }
    }

    private async Task LoadTextAsync(CancellationToken cancellationToken)
    {
        if (text != null) return;
        MuseCocoTextModel built = null;
        try
        {
            var runner = new OnnxRunnerOptions { Threads = loadedThreadCount };
            built = textFiles == null
                ? await MuseCocoTextModel.LoadFromDirectoryAsync(options.TextBundleDirectory, runner, cancellationToken).ConfigureAwait(false)
                : await MuseCocoTextModel.LoadFromFilesAsync(textFiles, runner, cancellationToken).ConfigureAwait(false);
            if (!music.Schema.IsCompatibleWith(built.Schema))
                throw new MusicGenerationException("The MuseCoco text and music bundles use incompatible attribute schemas.");
            lock (gate) { text = built; }
            built = null;
        }
        catch (ModelRunnerException error)
        {
            throw new MusicGenerationException($"The MuseCoco text bundle for '{Name}' could not be loaded: {error.Message}", error);
        }
        finally { built?.Dispose(); }
    }

    /// <summary>Releases both models. Finish or dispose the active enumeration first.</summary>
    /// <exception cref="InvalidOperationException">Loading or generation is active.</exception>
    public void Release()
    {
        lock (gate) { RequireIdle(); ReleaseLoaded(); }
    }

    /// <summary>Releases models and permanently closes this generator; active use is refused.</summary>
    /// <exception cref="InvalidOperationException">Loading or generation is active.</exception>
    public void Dispose()
    {
        lock (gate) { RequireIdle(); ReleaseLoaded(); disposed = true; }
    }

    private void ReleaseLoaded()
    {
        text?.Dispose();
        music?.Dispose();
        text = null;
        music = null;
    }

    private void BeginUse()
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MuseCocoMusicGenerator));
            RequireIdle();
            busy = true;
        }
    }

    private void EndUse() { lock (gate) busy = false; }

    private void RequireIdle()
    {
        if (busy) throw new InvalidOperationException($"MuseCoco generator '{Name}' is in use. Finish or dispose its active enumeration before starting another operation.");
    }

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A generator name is required.", nameof(name));
    }

    private void NormalizeTextDirectory()
    {
        if (!string.IsNullOrWhiteSpace(options.TextBundleDirectory))
            options.TextBundleDirectory = Path.GetFullPath(options.TextBundleDirectory);
    }

    private static IReadOnlyDictionary<string, string> CopyFiles(IReadOnlyDictionary<string, string> source)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in source) copy.Add(pair.Key, Path.GetFullPath(pair.Value));
        if (!copy.ContainsKey("musecoco.json")) throw new ArgumentException("A MuseCoco file map must include musecoco.json.", nameof(source));
        return copy;
    }
}
