using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration;

/// <summary>
/// Music being generated and played. It is the whole entry point of this library: an options object
/// goes in, music comes out, and there is no builder and no host to configure.
/// </summary>
/// <remarks>
/// <para>
/// THE SHORTEST THING THAT WORKS is two lines, one of which is the application's own decision
/// about instruments:
/// </para>
/// <code>
/// GeneralMidiInstrumentLibrary.Register();      // CodeBrix.Audio.ModestSynth - the consumer's job
///
/// using var music = new MusicSession();
/// music.Play();
/// </code>
/// <para>
/// WHAT THAT PLAYS is a piece of MIDI embedded in this library, voiced automatically through the
/// registered instruments, looping at a bar line when it reaches the end. It is a RECORDING of what
/// a model once wrote, not a model, and it plays the same piece every time - which is exactly what
/// <see cref="ActiveSource"/> says, so nobody ever ships an application believing a model is
/// playing when it is not. Naming a generator in the options is what plays a model.
/// </para>
/// <para>
/// IT REGISTERS NOTHING. Not an instrument library, not a generator, not a rendition. With no
/// instrument library registered <see cref="Play"/> throws and the message says what to call.
/// </para>
/// <para>
/// TWO HOSTS. By default <see cref="Play"/> opens the audio device and plays. With
/// <see cref="MusicGenerationOptions.ApplicationOwnsAudioOutput"/> set it opens no device at all
/// and hands back <see cref="Renderer"/> for the application's own mixer to pull from; the same
/// engine sits behind both.
/// </para>
/// <para>
/// IT KEEPS GOING, AND IT CAN BE RE-PROMPTED. The music carries on past the end of a generator's
/// pass unless the options say otherwise; <see cref="FollowUp(MusicRequest)"/> asks for something
/// different and it takes over at the next bar line, a few seconds later, however far ahead the
/// engine was running. <see cref="Stop"/> ends the music and <see cref="Play"/> starts it afresh -
/// a game stops its music on one screen and starts it on the next.
/// </para>
/// <para>
/// THE APPLICATION COMES FIRST AND THE MUSIC WAITS. Silence while music buffers is an accepted
/// outcome and never a failure. <see cref="Diagnostics"/> is how an application sees what the music
/// is doing, cheaply enough to read every frame.
/// </para>
/// </remarks>
public sealed class MusicSession : IDisposable
{
    /// <summary>
    /// What is thrown when no instrument library has been registered, word for word. It names the
    /// one call that settles it, because a consumer of this library already has that package.
    /// </summary>
    internal const string NoInstrumentLibraryMessage =
        "No instrument library is registered, so no music can be played or rendered. Register the " +
        "provided ModestSynth.GeneralMidiInstrumentLibrary - call " +
        "GeneralMidiInstrumentLibrary.Register() - or register another instrument library first.";

    private readonly object gate = new object();
    private readonly MusicGenerationOptions options;
    private readonly IInstrumentLibraryLookup libraries;
    private readonly TimeProvider timeProvider;

    private IMusicGenerator generator;
    private MusicRendition rendition;
    private IInstrumentLibrary library;
    private RenditionVoicer voicer;
    private IMusicHost host;
    private MusicEngine engine;
    private bool playing;
    private bool disposed;

    /// <summary>
    /// Creates a session with nothing specified: the embedded MIDI replay, the default instrument
    /// library, and the music voiced automatically.
    /// </summary>
    public MusicSession()
        : this(new MusicGenerationOptions())
    {
    }

    /// <summary>Creates a session from options.</summary>
    /// <param name="options">
    /// What to play, what to play it with and how. It is copied, so later changes to it do not
    /// reach the session.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public MusicSession(MusicGenerationOptions options)
        : this(options, InstrumentLibraries.Registered, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates a session over a substituted instrument-library lookup and clock. For tests: it is
    /// how "no instrument library is registered" is provable without emptying a registry that
    /// cannot be emptied, and how the pump is driven without sleeping.
    /// </summary>
    /// <param name="options">What to play, what to play it with and how.</param>
    /// <param name="libraries">Where instrument libraries are found.</param>
    /// <param name="timeProvider">The clock the background pump runs on.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal MusicSession(MusicGenerationOptions options, IInstrumentLibraryLookup libraries,
        TimeProvider timeProvider)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (libraries == null)
        {
            throw new ArgumentNullException(nameof(libraries));
        }

        if (timeProvider == null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }

        this.options = options.Clone();
        this.libraries = libraries;
        this.timeProvider = timeProvider;
    }

    /// <summary>The options the session is running on: a copy, taken when it was created.</summary>
    public MusicGenerationOptions Options => options;

    /// <summary>
    /// What the application pulls audio from when it owns its own audio output. It is null until
    /// <see cref="Play"/> has been called, and null for a session that opens the audio device.
    /// </summary>
    public IAudioRenderer Renderer
    {
        get { lock (gate) { return host == null ? null : host.Renderer; } }
    }

    /// <summary>Whether the session is playing. It stays true while the music is buffering.</summary>
    public bool IsPlaying
    {
        get { lock (gate) { return playing; } }
    }

    /// <summary>
    /// Where the play head has got to - how much music has been heard, not how much has been
    /// generated. It is zero until playing really starts, which is once a pre-roll has been
    /// written ahead of it.
    /// </summary>
    /// <remarks>
    /// WHEN THE APPLICATION OWNS THE AUDIO OUTPUT IT MOVES ONLY AS AUDIO IS PULLED from
    /// <see cref="Renderer"/>, so a mixer that stops pulling stops this - which is exactly what a
    /// paused game wants. When the session opens the audio device, it moves as the device consumes
    /// the audio.
    /// </remarks>
    public TimeSpan Position
    {
        get { lock (gate) { return host == null ? TimeSpan.Zero : host.Position; } }
    }

    /// <summary>
    /// Whether the play head has caught up with the music and is playing silence while more is
    /// generated. It is an accepted outcome and never a failure: the application comes first and
    /// the music waits.
    /// </summary>
    public bool IsStarved
    {
        get { lock (gate) { return host != null && host.IsStarved; } }
    }

    /// <summary>
    /// Whether the music has been played to its end. Music that keeps generating never reaches it.
    /// </summary>
    public bool IsFinished
    {
        get { lock (gate) { return host != null && host.IsFinished; } }
    }

    /// <summary>
    /// What went wrong inside the generator, or null. The music stops rather than waiting in
    /// silence for ever when a generator fails part-way through a piece - except when it was a
    /// FOLLOW-UP that failed, which is reported here and otherwise ignored, because music that is
    /// playing perfectly well should not stop for it.
    /// </summary>
    public Exception GenerationError
    {
        get { lock (gate) { return engine == null ? null : engine.GenerationError; } }
    }

    /// <summary>
    /// What is really making the music, as it stands: the generator, the instrument library, the
    /// rendition, and what every part that has sounded is being played with. It is null until
    /// <see cref="Play"/> has been called.
    /// </summary>
    /// <remarks>
    /// AFTER A FOLLOW-UP PROMPT IT CHANGES AT THE SWITCH, not when the prompt was made: until the
    /// new music takes over, what is playing is still the old music, and this says what is playing.
    /// </remarks>
    public ActiveMusicSource ActiveSource
    {
        get
        {
            lock (gate)
            {
                if (generator == null || voicer == null)
                {
                    return null;
                }

                var playingGenerator = engine == null ? generator : engine.ActiveGenerator;

                return new ActiveMusicSource(playingGenerator.Name, playingGenerator.Family,
                    playingGenerator.Description, library.Name, rendition.Name, voicer.Snapshot());
            }
        }
    }

    /// <summary>
    /// How the music is doing: starvation gaps, the measured real-time factor, which delivery mode
    /// the engine is in, the current lead, how many segments have been generated, late events, how
    /// often the music has been held back to a later bar line, and the voicing. Cheap enough to
    /// read from a game loop, and never null.
    /// </summary>
    /// <remarks>
    /// Before <see cref="Play"/> it reads as nothing having happened, which is true.
    /// </remarks>
    public MusicDiagnostics Diagnostics
    {
        get
        {
            lock (gate)
            {
                if (engine == null)
                {
                    return new MusicDiagnostics(0, null, MusicDeliveryMode.Streaming, TimeSpan.Zero,
                        0, 0, 0, 0, null);
                }

                return new MusicDiagnostics(engine.StarvationGapCount, engine.RealTimeFactor,
                    engine.Mode, engine.Lead, engine.SegmentCount, engine.Stream.LateEventCount,
                    engine.HoldCount, engine.HeldBarCount,
                    voicer == null ? null : voicer.Snapshot());
            }
        }
    }

    /// <summary>
    /// Loads the generator the options name, so that the delay of loading a model is taken NOW -
    /// during a loading screen, say - rather than when the music is asked for.
    /// </summary>
    /// <param name="cancellationToken">Stops the loading.</param>
    /// <returns>A task that completes once the generator is loaded.</returns>
    /// <remarks>
    /// IT IS OPTIONAL. A generator loads by itself the first time it is used; this only moves when
    /// that happens. Once loaded it STAYS LOADED - it lives in the registry, not in this session -
    /// and serves every later piece, every follow-up prompt and every other session.
    /// <see cref="Dispose"/> releases NOTHING.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The named generator is not registered.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public Task PreloadAsync(CancellationToken cancellationToken)
    {
        IMusicGenerator resolved;

        lock (gate)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MusicSession));
            }

            resolved = MusicGeneratorRegistry.Resolve(options.Generator);
        }

        return resolved.PreloadAsync(cancellationToken);
    }

    /// <summary>
    /// Gives back the memory the generator the options name is holding. It stays REGISTERED, and
    /// asking for it again loads it again.
    /// </summary>
    /// <remarks>
    /// Releasing a generator that is generating is not a way to stop the music - call
    /// <see cref="Stop"/> for that.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The named generator is not registered.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public void Release()
    {
        IMusicGenerator resolved;

        lock (gate)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MusicSession));
            }

            resolved = MusicGeneratorRegistry.Resolve(options.Generator);
        }

        resolved.Release();
    }

    /// <summary>
    /// Starts the music: resolves what was named, opens the audio device unless the application
    /// owns its output, and begins generating.
    /// </summary>
    /// <remarks>
    /// PLAYING AFTER <see cref="Stop"/> STARTS AFRESH - a new timeline, a new engine, the same
    /// options - because a game stops its music on one screen and starts it on the next. Calling it
    /// while the music is already playing does nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No instrument library is registered; or a named generator, instrument library or rendition
    /// is not registered, in which case the message lists what is.
    /// </exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for something the named generator does not honour. Nothing is stripped out
    /// of a request quietly.
    /// </exception>
    /// <exception cref="MusicGenerationException">
    /// The named instrument library cannot play one part at a time, which is what a rendition needs.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public void Play()
    {
        lock (gate)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MusicSession));
            }

            if (playing)
            {
                return;
            }

            // FIRST, and before anything else is looked at: with no instruments there is no music,
            // whatever else the options say.
            if (!libraries.HasAny)
            {
                throw new InvalidOperationException(NoInstrumentLibraryMessage);
            }

            var resolvedGenerator = MusicGeneratorRegistry.Resolve(options.Generator);
            var resolvedLibrary = libraries.Resolve(options.InstrumentLibrary);
            var resolvedRendition = MusicRenditionRegistry.Resolve(options.Rendition).Clone();

            if (!resolvedLibrary.SupportsPerPart)
            {
                throw new MusicGenerationException(
                    $"The instrument library '{resolvedLibrary.Name}' does not make one instrument " +
                    "per part, which is how a rendition gives each part its own voice and its own " +
                    "level. Name a library that does.");
            }

            var request = BuildRequest(options.Request);

            // Refused here rather than deep inside the generation, so that a request the generator
            // cannot honour is an error on the line that started the music.
            MusicGeneratorCapabilities.EnsureHonoured(resolvedGenerator, request);

            // Nothing has thrown, so the last run - if there was one - can be let go of.
            LetGoOfTheLastRun();

            generator = resolvedGenerator;
            library = resolvedLibrary;
            rendition = resolvedRendition;

            var stream = new MidiStream(request.TicksPerQuarterNote);

            host = options.ApplicationOwnsAudioOutput
                ? new RendererMusicHost(options.SampleRate)
                : (IMusicHost)new DeviceMusicHost();

            host.Load(stream, rate =>
            {
                voicer = new RenditionVoicer(rendition, library, rate, request.InstrumentHints,
                    options.MasterVolume);

                return voicer.Router;
            }, DeliverMessage);

            engine = new MusicEngine(generator, request, stream, voicer, host, options.Preroll,
                timeProvider, 0L)
            {
                GenerateAhead = options.GenerateAhead,
                EndOfPiece = options.EndOfPiece
            };

            engine.Start();
            host.Play();
            playing = true;
        }
    }

    /// <summary>
    /// Asks for something different. The new music starts generating AT ONCE and takes over at the
    /// next bar line once it has its own pre-roll buffered; what was already committed plays up to
    /// that bar line and the rest of the old lookahead is thrown away.
    /// </summary>
    /// <param name="request">What the music should be from now on.</param>
    /// <remarks>
    /// <para>
    /// THE LATENCY IS A FEW SECONDS whatever <see cref="MusicGenerationOptions.GenerateAhead"/> is,
    /// because the lookahead lives in memory rather than on the timeline. A second follow-up made
    /// before the first has taken over REPLACES it.
    /// </para>
    /// <para>
    /// WHAT IS KEPT ACROSS THE SEAM, and what is not. The tempo, the metre, the key and each part's
    /// program and controllers carry across, and the seam emits nothing that has not changed. Notes
    /// still sounding keep their full length - EXCEPT on a part whose INSTRUMENT really changes,
    /// where the new instrument replaces the old one and whatever that part was holding stops at
    /// that moment. Only parts whose instrument actually changes are re-voiced; every part the new
    /// music leaves on the same instrument, and every part a named rendition voiced, is untouched,
    /// as is every loop and every continuation of the same music.
    /// </para>
    /// <para>
    /// The tick resolution of the timeline that is already playing is used, whatever the request
    /// says: several segments from several generators land on ONE timeline, and it has one
    /// resolution.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Nothing is playing.</exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for something the generator does not honour. THE MUSIC CARRIES ON: nothing
    /// has been changed when this is thrown.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public void FollowUp(MusicRequest request) => FollowUp(request, null);

    /// <summary>
    /// Asks a NAMED generator for something different, the same way
    /// <see cref="FollowUp(MusicRequest)"/> does.
    /// </summary>
    /// <param name="request">What the music should be from now on.</param>
    /// <param name="generatorName">
    /// The generator to ask. Null, empty or blank means the one that is already playing - which is
    /// not the same thing as the rule for <see cref="MusicGenerationOptions.Generator"/>, where
    /// nothing named means the embedded replay.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Nothing is playing, or no generator of that name is registered - in which case the message
    /// lists what is, and THE MUSIC CARRIES ON.
    /// </exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for something that generator does not honour. THE MUSIC CARRIES ON.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public void FollowUp(MusicRequest request, string generatorName)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        lock (gate)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MusicSession));
            }

            if (engine == null || !playing)
            {
                throw new InvalidOperationException(
                    "There is no music to follow up: call Play() first. A follow-up prompt changes " +
                    "music that is playing; it does not start any.");
            }

            // Everything that can be refused is refused BEFORE anything is touched, so that a
            // follow-up that cannot be honoured leaves the music exactly as it was.
            var next = string.IsNullOrWhiteSpace(generatorName)
                ? engine.ActiveGenerator
                : MusicGeneratorRegistry.Resolve(generatorName);

            var followUp = BuildRequest(request);

            followUp.TicksPerQuarterNote = engine.Stream.TicksPerQuarterNote;

            MusicGeneratorCapabilities.EnsureHonoured(next, followUp);

            engine.FollowUp(next, followUp);
        }
    }

    /// <summary>
    /// Renders the music to an audio FILE, with no audio device and as fast as the machine
    /// allows. The format is chosen by the file's extension.
    /// </summary>
    /// <param name="path">The file to write. It is overwritten if it exists.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>What the render was, once the file is written.</returns>
    /// <remarks>
    /// With no render options it is ONE PASS of the generator, ending at the generator's own
    /// ending. Pass <see cref="MusicRenderOptions"/> for a length and an ending of your own.
    /// </remarks>
    public Task<MusicRenderResult> RenderToFileAsync(string path,
        CancellationToken cancellationToken) =>
        RenderToFileAsync(path, null, null, cancellationToken);

    /// <summary>
    /// Renders the music to an audio FILE of a chosen length, with a chosen ending. The format is
    /// chosen by the file's extension.
    /// </summary>
    /// <param name="path">The file to write. It is overwritten if it exists.</param>
    /// <param name="renderOptions">How long the file should be, how it ends and how it is stored.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>What the render was, once the file is written.</returns>
    public Task<MusicRenderResult> RenderToFileAsync(string path, MusicRenderOptions renderOptions,
        CancellationToken cancellationToken) =>
        RenderToFileAsync(path, renderOptions, null, cancellationToken);

    /// <summary>
    /// Renders the music to an audio FILE of a chosen length, with a chosen ending, reporting
    /// progress as it goes.
    /// </summary>
    /// <param name="path">The file to write. It is overwritten if it exists.</param>
    /// <param name="renderOptions">How long the file should be, how it ends and how it is stored.</param>
    /// <param name="progress">Where progress is reported, or null for none.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>What the render was, once the file is written.</returns>
    /// <remarks>
    /// <para>
    /// IT IS THE SAME MUSIC <see cref="Play"/> WOULD PLAY, generated by the same generator through
    /// the same engine and voiced by the same rendition - the session's own
    /// <see cref="MusicGenerationOptions"/> settle all of that, and the render options settle only
    /// the FILE. What differs is that nothing is paced: a render generates flat out, and takes as
    /// long as the machine needs rather than as long as the music.
    /// </para>
    /// <para>
    /// IT NEITHER NEEDS NOR DISTURBS MUSIC THAT IS PLAYING. A render builds its own timeline, its
    /// own engine and its own instruments, so a session can render one piece while it plays
    /// another, and a session that has never been played can render.
    /// </para>
    /// <para>
    /// EVERYTHING THAT CAN BE REFUSED IS REFUSED FIRST, before a single event is generated: an
    /// extension no writer is registered for, a stream that cannot seek when the format needs it,
    /// no instrument library registered, an unknown generator, library or rendition, a request the
    /// generator will not honour, and a fade or a hard cut with no target length.
    /// </para>
    /// <para>
    /// A CANCELLED OR FAILED RENDER LEAVES NO FILE. What was written is not finished off - a WAV
    /// or an AIFF is only valid once its header has been patched - and the file this render
    /// created is deleted.
    /// </para>
    /// <code>
    /// GeneralMidiInstrumentLibrary.Register();
    ///
    /// using var music = new MusicSession();
    ///
    /// var result = await music.RenderToFileAsync("theme.wav",
    ///     new MusicRenderOptions { TargetLength = TimeSpan.FromMinutes(6.0) },
    ///     cancellationToken);
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is blank, or the render options ask for an ending that needs a
    /// target length without one, or the format needs a stream it can seek.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No instrument library is registered; or a named generator, instrument library or rendition
    /// is not registered, in which case the message lists what is.
    /// </exception>
    /// <exception cref="MusicGenerationException">
    /// The named instrument library cannot play one part at a time, or the generator failed.
    /// </exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for something the named generator does not honour.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// No audio writer is registered for the file's extension. The message names it and lists what
    /// is registered.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public Task<MusicRenderResult> RenderToFileAsync(string path, MusicRenderOptions renderOptions,
        IProgress<MusicRenderProgress> progress, CancellationToken cancellationToken)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path to write the audio to is required.", nameof(path));
        }

        var render = PrepareRender(renderOptions, path, null, path);

        return Task.Run(() => render.RunAsync(progress, cancellationToken));
    }

    /// <summary>
    /// Renders the music to a STREAM the caller owns, in the format the file name or extension
    /// names.
    /// </summary>
    /// <param name="output">The stream to write to. It is never closed.</param>
    /// <param name="fileNameOrExtension">What the format is chosen by: "theme.wav" or ".wav".</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>What the render was, once the audio is written.</returns>
    public Task<MusicRenderResult> RenderToStreamAsync(Stream output, string fileNameOrExtension,
        CancellationToken cancellationToken) =>
        RenderToStreamAsync(output, fileNameOrExtension, null, null, cancellationToken);

    /// <summary>
    /// Renders the music to a STREAM the caller owns, of a chosen length and with a chosen ending.
    /// </summary>
    /// <param name="output">The stream to write to. It is never closed.</param>
    /// <param name="fileNameOrExtension">What the format is chosen by: "theme.wav" or ".wav".</param>
    /// <param name="renderOptions">How long it should be, how it ends and how it is stored.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>What the render was, once the audio is written.</returns>
    public Task<MusicRenderResult> RenderToStreamAsync(Stream output, string fileNameOrExtension,
        MusicRenderOptions renderOptions, CancellationToken cancellationToken) =>
        RenderToStreamAsync(output, fileNameOrExtension, renderOptions, null, cancellationToken);

    /// <summary>
    /// Renders the music to a STREAM the caller owns, of a chosen length and with a chosen ending,
    /// reporting progress as it goes.
    /// </summary>
    /// <param name="output">The stream to write to. It is never closed.</param>
    /// <param name="fileNameOrExtension">What the format is chosen by: "theme.wav" or ".wav".</param>
    /// <param name="renderOptions">How long it should be, how it ends and how it is stored.</param>
    /// <param name="progress">Where progress is reported, or null for none.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>What the render was, once the audio is written.</returns>
    /// <remarks>
    /// <para>
    /// THE STREAM MUST BE SEEKABLE for a format that patches its header once the length of the
    /// audio is known, which WAV and AIFF both do; a format written strictly forwards - Ogg and
    /// Opus - takes any writable stream, a pipe or a socket included. Which is which is the
    /// writer's own declaration, and the refusal is CodeBrix.Audio's own words.
    /// </para>
    /// <para>
    /// THE STREAM IS NEVER CLOSED, whether the render finishes, fails or is cancelled. Finishing
    /// the audio and closing the stream are separate jobs and the stream is the caller's
    /// throughout. A render that does not finish does not finish the FILE either, so a partial
    /// WAV never looks like a complete one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileNameOrExtension"/> is blank, the stream cannot be written to, the
    /// format needs a stream it can seek, or the render options ask for an ending that needs a
    /// target length without one.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No instrument library is registered; or a named generator, instrument library or rendition
    /// is not registered.
    /// </exception>
    /// <exception cref="MusicGenerationException">
    /// The named instrument library cannot play one part at a time, or the generator failed.
    /// </exception>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for something the named generator does not honour.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// No audio writer is registered for the extension. The message names it and lists what is.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public Task<MusicRenderResult> RenderToStreamAsync(Stream output, string fileNameOrExtension,
        MusicRenderOptions renderOptions, IProgress<MusicRenderProgress> progress,
        CancellationToken cancellationToken)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            throw new ArgumentException(
                "A file name or an extension is required: it is what the audio format is chosen by.",
                nameof(fileNameOrExtension));
        }

        var render = PrepareRender(renderOptions, null, output, fileNameOrExtension);

        return Task.Run(() => render.RunAsync(progress, cancellationToken));
    }

    /// <summary>
    /// Stops the music and stops generating. <see cref="Play"/> starts again from the beginning,
    /// with the same options and a new timeline.
    /// </summary>
    public void Stop()
    {
        lock (gate)
        {
            playing = false;

            if (engine != null)
            {
                engine.Stop();
            }

            if (host != null)
            {
                host.Stop();
            }
        }
    }

    /// <summary>
    /// Stops the music, closes the audio device if one was opened, and gives up everything held.
    /// IT RELEASES NO GENERATOR: a loaded model belongs to the application, not to one session.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            playing = false;

            LetGoOfTheLastRun();
        }
    }

    /// <summary>
    /// Moves settled music onto the timeline by hand, instead of waiting for the background pump.
    /// For tests, which drive the engine on a clock they control.
    /// </summary>
    internal void Pump()
    {
        MusicEngine current;

        lock (gate)
        {
            current = engine;
        }

        if (current != null)
        {
            current.Pump();
        }
    }

    /// <summary>The engine behind the session. For tests, which look at the commit window.</summary>
    internal MusicEngine Engine
    {
        get { lock (gate) { return engine; } }
    }

    /// <summary>The timeline the music is being written to. For tests.</summary>
    internal MidiStream Stream
    {
        get { lock (gate) { return engine == null ? null : engine.Stream; } }
    }

    // A RENDER TAKES NOTHING FROM THE SESSION BUT ITS OPTIONS. It reads what it needs under the
    // lock and builds everything else of its own, so a render never touches - and never waits on -
    // music that is playing.
    private OfflineRender PrepareRender(MusicRenderOptions renderOptions, string path, Stream output,
        string fileNameOrExtension)
    {
        MusicGenerationOptions renderWith;
        IInstrumentLibraryLookup lookup;
        TimeProvider clock;

        lock (gate)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MusicSession));
            }

            renderWith = options;
            lookup = libraries;
            clock = timeProvider;
        }

        return OfflineRender.Prepare(renderWith, renderOptions, lookup, clock, path, output,
            fileNameOrExtension);
    }

    private MusicRequest BuildRequest(MusicRequest source)
    {
        var copy = source.Clone();

        if (options.InferenceThreadCount.HasValue)
        {
            copy.InferenceThreadCount = options.InferenceThreadCount;
        }

        return copy;
    }

    private void LetGoOfTheLastRun()
    {
        if (engine != null)
        {
            engine.Dispose();
            engine = null;
        }

        if (host != null)
        {
            host.Dispose();
            host = null;
        }

        voicer = null;
    }

    // RUNS ON THE RENDERING THREAD, for every message the sequencer delivers. It does two things
    // and nothing else: it hands the routing table whatever the pump thread has already built, and
    // it delivers the message. The hook REPLACES delivery, so a hook that forgets the second line
    // silences the music completely.
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
}
