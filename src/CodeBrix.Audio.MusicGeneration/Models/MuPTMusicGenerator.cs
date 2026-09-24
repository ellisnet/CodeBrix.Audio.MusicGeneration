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
/// A generator backed by the MuPT model: it writes ABC NOTATION, token by token, on the native
/// road inside CodeBrix.Ollama.ModelRunner, from one file on disk. Nothing is installed, nothing
/// is downloaded and there is no Python anywhere near it.
/// </summary>
/// <remarks>
/// <para>
/// IT DOES NOT REGISTER ITSELF, and registering is not specifying. An application builds one,
/// registers it under a name, and then NAMES that name in
/// <see cref="MusicGenerationOptions.Generator"/>; until it does, the embedded replay plays.
/// </para>
/// <code>
/// var mupt = new MuPTMusicGenerator("MuPT", "/models/MuPT-v1-8192-190M-Q4_K_M.gguf");
/// MusicGeneratorRegistry.Register(mupt);                      // loads nothing
///
/// using var music = new MusicSession(new MusicGenerationOptions { Generator = "MuPT" });
/// music.Play();
/// </code>
/// <para>
/// CONSTRUCTING AND REGISTERING LOAD NOTHING. The file is not opened until
/// <see cref="PreloadAsync"/> or the first <see cref="GenerateAsync"/>; the model then STAYS
/// loaded and serves every later segment, and <see cref="Release"/> gives the memory back and
/// leaves the generator registered.
/// </para>
/// <para>
/// IT IS A PATH, NOT A PACKAGE. A consumer's own MuPT - a larger one, or one quantized another
/// way - works exactly the same: hand over its file.
/// </para>
/// <para>
/// TEXT BECOMES MUSIC WHILE IT IS STILL BEING WRITTEN. The model writes several parts MERGED, one
/// time slice at a time, and every completed slice is regrouped into standard ABC voices, read and
/// converted, with only the NEW events let out. Nothing already heard is ever changed: a note the
/// text could still lengthen - the far end of a tie - is held back until it cannot.
/// </para>
/// <para>
/// WHAT IT WRITES IS THE IDIOM ABC KNOWS: folk and classical, in parts, with no percussion. A
/// header alone produces a single line of melody; the duets that this library's taste rests on
/// came from an OPENING written in the model's own form, which
/// <see cref="MusicRequest.ModelNativeText"/> carries verbatim.
/// </para>
/// <para>
/// A CONTINUATION IS RE-PROMPTED WITH THE MODEL'S OWN TEXT - the header it started from and the
/// last few bars it wrote - so the key, the metre and the idiom carry over and none of it is
/// played twice. THE ADAPTER REMEMBERS THAT TEXT ITSELF, because nothing else in this library
/// holds it: the engine carries music as MIDI events. A caller driving this generator directly can
/// hand over its own through <see cref="MusicContinuation.ModelNativeTail"/>, which wins.
/// </para>
/// <para>
/// A SEED MEANS WHAT IT SAYS. The engine underneath keeps the last prompt it evaluated and re-uses
/// whatever prefix of the next one matches, and a prompt evaluated from that cache is not
/// evaluated the same way as one evaluated from nothing - so the same seeded request could
/// otherwise write different music depending on what the model had been doing. A request carrying
/// a seed therefore starts from an empty context. It costs the prompt's own evaluation, a fraction
/// of a second at this size.
/// </para>
/// <para>
/// ONE GENERATION AT A TIME. The engine underneath serializes requests on one loaded model, so a
/// second generation would silently WAIT for the first; this refuses it instead, with a message
/// that says so. A FOLLOW-UP PROMPT does not need a second one: the engine cancels the generation
/// that is running and waits for it to end before it asks for the new music.
/// </para>
/// </remarks>
public sealed class MuPTMusicGenerator : IMusicGenerator, IDisposable
{
    /// <summary>The family every MuPT generator belongs to.</summary>
    public const string MuPTFamily = "MuPT";

    private readonly object gate = new object();
    private readonly SemaphoreSlim loadGate = new SemaphoreSlim(1, 1);
    private readonly MuPTGeneratorOptions options;
    private readonly string modelPath;

    private IRunningModel model;
    private int loadedThreadCount;
    private bool generating;
    private bool disposed;
    private string description;
    private string rememberedHeader;
    private string rememberedText;

    /// <summary>Builds a generator over a model file.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="modelPath">The path of the model's GGUF file.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="modelPath"/> is blank.
    /// </exception>
    public MuPTMusicGenerator(string name, string modelPath)
        : this(name, modelPath, null)
    {
    }

    /// <summary>Builds a generator over a model file.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="modelPath">The path of the model's GGUF file.</param>
    /// <param name="options">
    /// What is fixed when the model loads, and what it writes with when a request says nothing.
    /// Null takes the defaults.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="modelPath"/> is blank.
    /// </exception>
    /// <remarks>THE FILE IS NOT LOOKED AT HERE. Constructing loads nothing and reads nothing.</remarks>
    public MuPTMusicGenerator(string name, string modelPath, MuPTGeneratorOptions options)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A music generator must have a name: it is how a consumer asks for it.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException(
                "The path of the MuPT model's GGUF file is required. The tokenizer is inside the " +
                "file, so one path is the whole of it.", nameof(modelPath));
        }

        Name = name;
        this.modelPath = modelPath;
        this.options = options == null ? new MuPTGeneratorOptions() : options.Clone();
        description = $"Writes music with the MuPT model in '{modelPath}'.";
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => MuPTFamily;

    /// <summary>
    /// A sentence a developer can read to know what this generator produces. It can be set, so an
    /// application can say which MuPT model it is playing.
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
    /// What this model acts on: text in its own notation, the key, the metre, the unit note
    /// length, the tempo, character words, a voice count, a seed, the sampling settings, a length
    /// cap, a thread count and a continuation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A VOICE COUNT IS HONOURED BY WRITING THE OPENING, which is the only way a number of parts
    /// can mean anything to a model that reads notation: a header alone produces one line of
    /// melody, and what makes a duet is two parts in the first bars. Asking for TWO voices writes
    /// those bars from the key and the metre; asking for one writes none; asking for more is
    /// refused BY NAME, because there is no honest opening to write for it - a caller that wants
    /// three parts writes them itself through <see cref="MusicRequest.ModelNativeText"/>.
    /// </para>
    /// <para>
    /// CHARACTER WORDS ARE HONOURED THROUGH <see cref="MusicCharacterWords"/>, which turns a word
    /// into a tempo, a mode, or both. A word that names neither is refused BY NAME - this model
    /// reads ABC, not prose.
    /// </para>
    /// <para>
    /// WHAT IS REFUSED BY NAME, and why. FREE TEXT: it reads music, not words - ABC is its only
    /// language. A PRIMER is MIDI, and there is no way back from MIDI to the notation this model
    /// reads; a continuation carries its own text instead. INSTRUMENT HINTS: ABC is a notation for
    /// parts, not for sounds, and what plays each part is the RENDITION's business. A DRUM KIT:
    /// ABC has no percussion at all. A TARGET LENGTH is not its business either: it writes until
    /// its token cap and the engine asks again, which is how a piece of any length is made.
    /// </para>
    /// <para>
    /// EVERY SAMPLING SETTING IS HONOURED, the repetition penalty included - this model's engine
    /// applies one, and the music default is none at all.
    /// </para>
    /// </remarks>
    public MusicRequestFeatures Honours =>
        MusicRequestFeatures.ModelNativeText | MusicRequestFeatures.Key |
        MusicRequestFeatures.Meter | MusicRequestFeatures.UnitNoteLength |
        MusicRequestFeatures.Tempo | MusicRequestFeatures.Seed |
        MusicRequestFeatures.SamplingControls | MusicRequestFeatures.MaximumEvents |
        MusicRequestFeatures.InferenceThreadCount | MusicRequestFeatures.Continuation |
        MusicRequestFeatures.CharacterWords | MusicRequestFeatures.VoiceCount;

    /// <inheritdoc />
    public bool IsLoaded
    {
        get { lock (gate) { return model != null; } }
    }

    /// <summary>
    /// Everything the last pass wrote, in the model's own form - what the next continuation is
    /// prompted with. It is here so that a test can hold the incremental path to the same text the
    /// model really wrote.
    /// </summary>
    internal string LastModelText
    {
        get { lock (gate) { return rememberedText; } }
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
        IRunningModel released;

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

        // A VOICE COUNT THIS MODEL CANNOT BE GIVEN AN OPENING FOR IS REFUSED HERE, on the line
        // that asked for the music, rather than silently producing one line of melody.
        MuPTOpening.EnsureWritable(request.Intent, Name);

        // THE CHARACTER WORDS ARE READ HERE, at the edge, so that everything below this line sees
        // an ordinary intent: a word that names a tempo or a mode has become one, and a word that
        // names neither has already been refused by name.
        return Generate(MusicCharacterWords.ApplyTo(request, Name).Clone(), cancellationToken);
    }

    private async IAsyncEnumerable<GeneratedMusicEvent> Generate(MusicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(request.InferenceThreadCount, cancellationToken)
            .ConfigureAwait(false);

        string header;
        string written;

        lock (gate)
        {
            header = rememberedHeader;
            written = rememberedText;
        }

        var prompt = MuPTPrompt.For(request, options, header, written);
        var generation = MuPTRequestMapper.ToGenerationOptions(request, options, Name);
        var pass = new MuPTSlicePath(prompt.Text, request.TicksPerQuarterNote,
            prompt.IsAlreadyHeard);

        StartOneGeneration();

        try
        {
            if (request.Seed.HasValue)
            {
                // A SEED IS A PROMISE THAT THE SAME REQUEST WRITES THE SAME MUSIC, and on this
                // road that costs one thing: the engine keeps the last prompt it evaluated in its
                // context and re-uses whatever prefix of the new one matches. A prompt evaluated
                // from a cache is not evaluated the same way as one evaluated from nothing - the
                // arithmetic is done in different batches - and the last bits of a distribution
                // decide a token now and then. So a seeded pass starts from an empty context. It
                // costs the prompt's own evaluation, which is a fraction of a second on a prompt
                // this size, and it buys a seed that means what it says.
                await loaded.ClearCacheAsync(cancellationToken).ConfigureAwait(false);
            }

            await foreach (var update in loaded
                               .GenerateAsync(prompt.Text, generation, cancellationToken)
                               .ConfigureAwait(false))
            {
                foreach (var produced in pass.Accept(update.Text))
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
            Remember(prompt.HeaderText, pass.RawText);
            EndOneGeneration();
        }
    }

    private void Remember(string header, string text)
    {
        lock (gate)
        {
            rememberedHeader = header;
            rememberedText = text;
        }
    }

    /// <summary>
    /// Whether a generation is holding this instance's model right now. A generation that has been
    /// cancelled lets go when its pass notices, on its own task and a moment later - which is what
    /// a caller about to start another one on the same instance may have to wait for.
    /// </summary>
    internal bool IsGenerating
    {
        get { lock (gate) { return generating; } }
    }

    private void StartOneGeneration()
    {
        lock (gate)
        {
            if (generating)
            {
                throw new MusicGenerationException(
                    $"The music generator '{Name}' is already writing a piece. The engine " +
                    "underneath runs one request at a time on a loaded model, so a second " +
                    "generation would wait for the first without saying so; it is refused here " +
                    "instead. A follow-up prompt does not need a second one: the engine cancels " +
                    "the generation that is running and waits for it to end before it asks for " +
                    "the new music.");
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

    private async Task<IRunningModel> LoadAsync(int? requestedThreadCount,
        CancellationToken cancellationToken)
    {
        var wanted = requestedThreadCount ?? options.InferenceThreadCount;

        lock (gate)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MuPTMusicGenerator));
            }

            if (model != null && loadedThreadCount == wanted)
            {
                return model;
            }
        }

        await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IRunningModel stale;

            lock (gate)
            {
                if (model != null && loadedThreadCount == wanted)
                {
                    return model;
                }

                // A LOADED MODEL CANNOT CHANGE ITS THREAD COUNT, so a request naming a different
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

    private async Task<IRunningModel> Open(int threadCount, CancellationToken cancellationToken)
    {
        if (!File.Exists(modelPath))
        {
            throw new MusicGenerationException(
                $"The MuPT model for the music generator '{Name}' is not at '{modelPath}'. The " +
                "generator is built with the path of a GGUF file; an application that keeps its " +
                "models in a store asks the store for the path and hands that over.");
        }

        var runner = new ModelRunnerOptions
        {
            ModelPath = modelPath,
            Threads = threadCount
        };

        if (options.ContextTokens.HasValue)
        {
            runner.ContextSize = (uint)options.ContextTokens.Value;
        }

        try
        {
            return await ModelRunner.LoadAsync(runner, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelRunnerException exception)
        {
            throw new MusicGenerationException(
                $"The MuPT model for the music generator '{Name}' could not be loaded: " +
                exception.Message, exception);
        }
    }
}
