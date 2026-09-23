using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;
using CodeBrix.Audio.MusicGeneration.Models.Internal;
using CodeBrix.Ollama.ModelRunner;
using ModelMidiEvent = CodeBrix.Ollama.ModelRunner.MidiEvent;

namespace CodeBrix.Audio.MusicGeneration.Models;

/// <summary>
/// A generator backed by the SkyTNT MIDI model: it writes MIDI events directly, on the managed
/// road inside CodeBrix.Ollama.ModelRunner, from a bundle of files on disk. Nothing is installed,
/// nothing is downloaded and there is no Python anywhere near it.
/// </summary>
/// <remarks>
/// <para>
/// IT DOES NOT REGISTER ITSELF, and registering is not specifying. An application builds one,
/// registers it under a name, and then NAMES that name in
/// <see cref="MusicGenerationOptions.Generator"/>; until it does, the embedded replay plays.
/// </para>
/// <code>
/// var skytnt = new SkyTNTMusicGenerator("SkyTNT", "/models/skytnt-int4");
/// MusicGeneratorRegistry.Register(skytnt);                    // loads nothing
///
/// using var music = new MusicSession(new MusicGenerationOptions { Generator = "SkyTNT" });
/// music.Play();
/// </code>
/// <para>
/// CONSTRUCTING AND REGISTERING LOAD NOTHING. No file is opened until <see cref="PreloadAsync"/>
/// or the first <see cref="GenerateAsync"/>; the model then STAYS loaded and serves every later
/// segment, and <see cref="Release"/> gives the memory back and leaves the generator registered.
/// Reduced model files do not bound runtime memory: inference state and temporary buffers can be much larger.
/// </para>
/// <para>
/// A MODEL IS NOT ONE FILE. This one is a configuration and two graphs, and they are reached
/// either as a FOLDER or as a MAP of the publisher's name for each file against the path it is
/// really at - which is a first-class route, not a fallback: a store that keeps its files under
/// digests hands over exactly that map.
/// </para>
/// <para>
/// A PASS IS A WHOLE NUMBER OF BARS. The model stops where its event cap lands, which is usually
/// part-way through a bar; the ragged last bar is HELD BACK rather than played, and the pass's
/// final settled tick is the bar line - so the segment after it starts against a bar line and no
/// seam falls mid-bar. A pass that did not reach one whole bar is rounded up to one instead of
/// yielding nothing, so the music always moves forward.
/// </para>
/// <para>
/// A CONTINUATION IS THE GOOD PATH. The tail of the music so far goes in as the model's PROMPT -
/// a few bars, not the whole piece - its own events are not re-emitted, and the new events are
/// written from the tail's END. The best-sounding piece of the listening session this library's
/// taste decisions rest on WAS a continuation.
/// </para>
/// <para>
/// ONE GENERATION AT A TIME. The graphs run one step at a time, so a second generation asked for
/// while one is still running is refused, with a message that says so. A FOLLOW-UP PROMPT over
/// this generator is not that: the engine cancels the generation that is running and waits for it
/// to end before it asks for the new music, and the music already settled plays on through the
/// change. Only a caller driving this generator ITSELF has to finish one generation before it
/// asks for another.
/// </para>
/// </remarks>
public sealed class SkyTNTMusicGenerator : IMusicGenerator, IDisposable
{
    /// <summary>The family every SkyTNT generator belongs to.</summary>
    public const string SkyTNTFamily = "SkyTNT";

    private readonly object gate = new object();
    private readonly SemaphoreSlim loadGate = new SemaphoreSlim(1, 1);
    private readonly SkyTNTGeneratorOptions options;
    private readonly string bundleDirectory;
    private readonly IReadOnlyDictionary<string, string> bundleFiles;

    private IMidiGenerationModel model;
    private int loadedThreadCount;
    private bool generating;
    private bool disposed;
    private string description;

    /// <summary>Builds a generator over a bundle laid out as a folder.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="bundleDirectory">The folder the bundle's files are in.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="bundleDirectory"/> is blank.
    /// </exception>
    public SkyTNTMusicGenerator(string name, string bundleDirectory)
        : this(name, bundleDirectory, null)
    {
    }

    /// <summary>Builds a generator over a bundle laid out as a folder.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="bundleDirectory">The folder the bundle's files are in.</param>
    /// <param name="options">
    /// What is fixed when the model loads, and what it writes with when a request says nothing.
    /// Null takes the defaults.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="bundleDirectory"/> is blank.
    /// </exception>
    /// <remarks>THE FOLDER IS NOT LOOKED AT HERE. Constructing loads nothing and reads nothing.</remarks>
    public SkyTNTMusicGenerator(string name, string bundleDirectory,
        SkyTNTGeneratorOptions options)
    {
        RequireName(name);

        if (string.IsNullOrWhiteSpace(bundleDirectory))
        {
            throw new ArgumentException(
                "A folder holding the SkyTNT bundle is required. Hand over the files as a map of " +
                "names against paths instead when they are not laid out as a folder.",
                nameof(bundleDirectory));
        }

        Name = name;
        this.bundleDirectory = bundleDirectory;
        this.options = options == null ? new SkyTNTGeneratorOptions() : options.Clone();
        description = $"Writes music with the SkyTNT MIDI model in '{bundleDirectory}'.";
    }

    /// <summary>Builds a generator over a bundle whose files are held under names of their own.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="bundleFiles">
    /// The publisher's name for each of the bundle's files - <c>config.json</c>,
    /// <c>model_base.onnx</c> and <c>model_token.onnx</c> - against the path it is really at.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="bundleFiles"/> is null.</exception>
    /// <exception cref="MusicGenerationException">One of the bundle's three files is not named.</exception>
    public SkyTNTMusicGenerator(string name, IReadOnlyDictionary<string, string> bundleFiles)
        : this(name, bundleFiles, null)
    {
    }

    /// <summary>Builds a generator over a bundle whose files are held under names of their own.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="bundleFiles">
    /// The publisher's name for each of the bundle's files against the path it is really at.
    /// </param>
    /// <param name="options">
    /// What is fixed when the model loads, and what it writes with when a request says nothing.
    /// Null takes the defaults.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="bundleFiles"/> is null.</exception>
    /// <exception cref="MusicGenerationException">One of the bundle's three files is not named.</exception>
    /// <remarks>
    /// THE MAP IS CHECKED HERE AND THE FILES ARE NOT OPENED. A map missing one of the three files
    /// is a mistake a caller can fix at once, so it is not left until the music is asked for.
    /// </remarks>
    public SkyTNTMusicGenerator(string name, IReadOnlyDictionary<string, string> bundleFiles,
        SkyTNTGeneratorOptions options)
    {
        RequireName(name);

        Name = name;
        this.bundleFiles = SkyTNTBundle.Copy(bundleFiles);
        this.options = options == null ? new SkyTNTGeneratorOptions() : options.Clone();
        description = "Writes music with the SkyTNT MIDI model supplied by the application.";
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => SkyTNTFamily;

    /// <summary>
    /// A sentence a developer can read to know what this generator produces. It can be set, so an
    /// application can say which SkyTNT bundle it is playing.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public string Description
    {
        get { lock (gate) { return description; } }
        set
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (gate) { description = value; }
        }
    }

    /// <summary>
    /// What this model acts on: the instruments, the drum kit, the tempo, the metre, the key,
    /// character words, a seed, the sampling settings, a length cap, a thread count, a primer and
    /// a continuation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHARACTER WORDS ARE HONOURED THROUGH <see cref="MusicCharacterWords"/>, which turns a word
    /// into a tempo, a mode, or both. A word that names neither is refused BY NAME - this model
    /// reads no prose, so a word it cannot turn into a setting is a word it cannot act on.
    /// </para>
    /// <para>
    /// WHAT IS REFUSED BY NAME, and why. FREE TEXT and MODEL-NATIVE TEXT: this model reads music,
    /// not words, and has no notation of its own to be handed. A UNIT NOTE LENGTH is an ABC idea.
    /// A VOICE COUNT is not a control it has - the instruments are, and they decide the parts. A
    /// TARGET LENGTH is not its business either: it writes until its event cap and the engine asks
    /// again, which is how a piece of any length is made.
    /// </para>
    /// <para>
    /// A REPETITION PENALTY is inside the sampling settings and is refused separately, by name,
    /// because this model applies none at all - and music repeats on purpose.
    /// </para>
    /// </remarks>
    public MusicRequestFeatures Honours =>
        MusicRequestFeatures.InstrumentHints | MusicRequestFeatures.Tempo |
        MusicRequestFeatures.Meter | MusicRequestFeatures.Key | MusicRequestFeatures.Seed |
        MusicRequestFeatures.SamplingControls | MusicRequestFeatures.MaximumEvents |
        MusicRequestFeatures.InferenceThreadCount | MusicRequestFeatures.Primer |
        MusicRequestFeatures.Continuation | MusicRequestFeatures.DrumKit |
        MusicRequestFeatures.CharacterWords;

    /// <inheritdoc />
    public bool IsLoaded
    {
        get { lock (gate) { return model != null; } }
    }

    /// <summary>
    /// How many threads the loaded model's arithmetic is spread over, or null when nothing is
    /// loaded. It is fixed when the model loads, which is why a request naming a different count
    /// reloads it.
    /// </summary>
    public int? LoadedThreadCount
    {
        get { lock (gate) { return model == null ? null : loadedThreadCount; } }
    }

    /// <inheritdoc />
    /// <remarks>
    /// It loads with the thread count on the generator's own options. A later request naming a
    /// DIFFERENT count reloads the model with that count rather than ignoring it.
    /// </remarks>
    public async Task PreloadAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Release()
    {
        IMidiGenerationModel released;

        lock (gate)
        {
            released = model;
            model = null;
        }

        released?.Dispose();
    }

    /// <summary>
    /// Gives back the memory the model is holding, exactly as <see cref="Release"/> does. The
    /// generator stays usable afterwards: the next request loads the model again.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
        }

        Release();
        loadGate.Dispose();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        MusicGeneratorCapabilities.EnsureHonoured(this, request);

        // THE CHARACTER WORDS ARE READ HERE, at the edge, so that everything below this line sees
        // an ordinary intent: a word that names a tempo or a mode has become one, and a word that
        // names neither has already been refused by name.
        return Generate(MusicCharacterWords.ApplyTo(request, Name).Clone(), cancellationToken);
    }

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A music generator must have a name: it is how a consumer asks for it.", nameof(name));
        }
    }

    private async IAsyncEnumerable<GeneratedMusicEvent> Generate(MusicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(request.InferenceThreadCount, cancellationToken)
            .ConfigureAwait(false);

        var pass = SkyTNTPass.For(request, options, loaded.Metadata.TicksPerQuarterNote);
        var generation = SkyTNTRequestMapper.ToGenerationOptions(request, options, pass.Prompt, Name);

        StartOneGeneration();

        try
        {
            await foreach (var item in loaded.GenerateAsync(generation, cancellationToken)
                               .ConfigureAwait(false))
            {
                foreach (var produced in pass.Accept(item))
                {
                    yield return produced;
                }
            }

            foreach (var produced in pass.Finish())
            {
                yield return produced;
            }
        }
        finally
        {
            EndOneGeneration();
        }
    }

    private void StartOneGeneration()
    {
        lock (gate)
        {
            if (generating)
            {
                throw new MusicGenerationException(
                    $"The music generator '{Name}' is already writing a piece. This model's graphs " +
                    "run one step at a time, so a second generation is refused rather than queued. " +
                    "A follow-up prompt does not need a second one: the engine cancels the " +
                    "generation that is running and waits for it to end before it asks for the new " +
                    "music. A caller driving this generator itself finishes one generation, or " +
                    "cancels it, before asking for another.");
            }

            generating = true;
        }
    }

    private void EndOneGeneration()
    {
        lock (gate)
        {
            generating = false;
        }
    }

    private async Task<IMidiGenerationModel> LoadAsync(int? requestedThreadCount,
        CancellationToken cancellationToken)
    {
        var wanted = requestedThreadCount ?? options.InferenceThreadCount;

        lock (gate)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SkyTNTMusicGenerator));
            }

            if (model != null && loadedThreadCount == wanted)
            {
                return model;
            }
        }

        await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IMidiGenerationModel stale;

            lock (gate)
            {
                if (model != null && loadedThreadCount == wanted)
                {
                    return model;
                }

                // A LOADED GRAPH CANNOT CHANGE ITS THREAD COUNT, so a request naming a different
                // one is honoured by loading the model again rather than being ignored.
                stale = model;
                model = null;
            }

            stale?.Dispose();

            var built = await Open(wanted, cancellationToken).ConfigureAwait(false);

            lock (gate)
            {
                model = built;
                loadedThreadCount = wanted;
            }

            return built;
        }
        finally
        {
            loadGate.Release();
        }
    }

    private async Task<IMidiGenerationModel> Open(int threadCount,
        CancellationToken cancellationToken)
    {
        var runner = new OnnxRunnerOptions { Threads = threadCount };

        try
        {
            if (bundleFiles != null)
            {
                return await MidiGenerationModel
                    .LoadFromFilesAsync(bundleFiles, runner, cancellationToken)
                    .ConfigureAwait(false);
            }

            SkyTNTBundle.RequireRunnable(bundleDirectory);

            return await MidiGenerationModel
                .LoadFromDirectoryAsync(bundleDirectory, runner, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ModelRunnerException exception)
        {
            throw new MusicGenerationException(
                $"The SkyTNT model for the music generator '{Name}' could not be loaded: " +
                exception.Message, exception);
        }
    }
}
