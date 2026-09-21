using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;

namespace CodeBrix.Audio.MusicGeneration.Replay;

/// <summary>
/// A generator that plays a piece it already has, released in real time as though it were being
/// written. It is how music plays with no model, no download and nothing to configure - and it is
/// how the streaming machinery is exercised without one.
/// </summary>
/// <remarks>
/// <para>
/// FOUR OF THESE ARE BUILT IN, over pieces that ship inside this library, and are always resolvable
/// by the names on <see cref="EmbeddedReplay"/>. A consumer builds their own over their own music
/// with <see cref="FromMidiFile"/>, <see cref="FromMidiStream"/>, <see cref="FromMidiEvents"/> or
/// <see cref="FromAbc"/>, registers it under a name of their own, and asks for it by that name.
/// </para>
/// <para>
/// ONE CALL IS ONE PASS of the piece. The pass is as long as the music, rounded UP TO A WHOLE BAR,
/// and its last item says that the whole of that length has settled - trailing silence included -
/// so the pass after it starts on a bar line. LOOPING IS NOT DONE HERE: asking again, with a
/// continuation, is the next pass, and deciding to ask again is the caller's business.
/// </para>
/// <para>
/// IT PACES ITSELF. By default the music is released <see cref="DefaultPacingRate"/> times faster
/// than it plays: fast enough that a consumer's demo loop never runs dry, slow enough that it is
/// still a producer writing into a timeline rather than a file being poured into one. A rate BELOW
/// 1 is deliberately slower than the music, which is what makes this the test asset for starvation
/// and pre-roll. A request can switch pacing off altogether with
/// <see cref="MusicRequest.PaceInRealTime"/>, which is what an offline render does.
/// </para>
/// <para>
/// TIME IS INJECTABLE through <see cref="TimeProvider"/>, so a test drives the pacing from a clock
/// it controls and never sleeps.
/// </para>
/// </remarks>
public sealed class ReplayMusicGenerator : IMusicGenerator
{
    /// <summary>The family every replay generator belongs to.</summary>
    public const string ReplayFamily = "Replay";

    /// <summary>
    /// How much faster than real time a replay releases its music unless a caller changes it. It
    /// fills a few seconds of pre-roll in well under a second and half a minute of lookahead in a
    /// few, so nothing waits on the demo music - while still releasing it a note at a time.
    /// </summary>
    public const double DefaultPacingRate = 8.0;

    private readonly object gate = new object();
    private readonly Func<int, ReplayPiece> build;

    private ReplayPiece piece;
    private double pacingRate = DefaultPacingRate;
    private TimeProvider timeProvider = TimeProvider.System;
    private string description;

    private ReplayMusicGenerator(string name, string description, Func<int, ReplayPiece> build)
    {
        Name = name;
        this.description = description;
        this.build = build;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => ReplayFamily;

    /// <summary>
    /// A sentence a developer can read to know what this generator plays. It can be set, so a
    /// consumer's own replay can say what their music is.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public string Description
    {
        get => description;
        set => description = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// What a replay acts on: a continuation, and nothing else. A continuation is honoured by
    /// playing the next pass of the piece - which is what continuing a replay means - and every
    /// other part of a request is refused by name, because a piece that is already written cannot
    /// be made to be in A minor or to last four minutes.
    /// </summary>
    public MusicRequestFeatures Honours => MusicRequestFeatures.Continuation;

    /// <summary>
    /// Whether the piece has been read and prepared. A replay prepares its music for one tick
    /// resolution at a time: asking for a different one prepares it again.
    /// </summary>
    public bool IsLoaded
    {
        get { lock (gate) { return piece != null; } }
    }

    /// <summary>
    /// How much faster than real time the music is released. 1 is real time, 2 is twice as fast,
    /// and 0.25 is four times slower than the music actually plays.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive, finite number.</exception>
    public double PacingRate
    {
        get { lock (gate) { return pacingRate; } }
        set
        {
            if (!(value > 0.0) || double.IsInfinity(value) || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A pacing rate is a positive multiple of real time: 1 is real time, 0.25 is four " +
                    "times slower. To release the music as fast as it can be pulled, set " +
                    "MusicRequest.PaceInRealTime to false instead.");
            }

            lock (gate) { pacingRate = value; }
        }
    }

    /// <summary>
    /// The clock the pacing is measured against. It is <see cref="System.TimeProvider.System"/>
    /// unless a test replaces it, and it is read when a generation starts.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public TimeProvider TimeProvider
    {
        get { lock (gate) { return timeProvider; } }
        set
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (gate) { timeProvider = value; }
        }
    }

    /// <summary>Builds a replay of a MIDI file on disk. The file is read when the generator loads.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="path">The path of the MIDI file.</param>
    /// <returns>The generator.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="path"/> is blank.</exception>
    public static ReplayMusicGenerator FromMidiFile(string name, string path)
    {
        RequireName(name);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path to a MIDI file is required.", nameof(path));
        }

        var what = $"The MIDI file '{path}'";

        return new ReplayMusicGenerator(name,
            $"Replays the MIDI file '{path}'.",
            ticks => ReplayPieceBuilder.FromMidiBytes(ReadFile(path, what), ticks, what));
    }

    /// <summary>Builds a replay of MIDI read from a stream.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="stream">The stream to read the MIDI from.</param>
    /// <returns>The generator.</returns>
    /// <remarks>
    /// THE STREAM IS READ NOW, into memory, because a stream cannot be read again later and the
    /// generator may be loaded, released and loaded again. Parsing it still waits until the
    /// generator loads. The stream is left open and is not disposed.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public static ReplayMusicGenerator FromMidiStream(string name, Stream stream)
    {
        RequireName(name);

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        return new ReplayMusicGenerator(name,
            "Replays a piece of MIDI supplied by the application.",
            ticks => ReplayPieceBuilder.FromMidiBytes(bytes, ticks,
                $"The MIDI supplied to the replay generator '{name}'"));
    }

    /// <summary>Builds a replay of a MIDI event collection the application already holds.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="events">The music to replay.</param>
    /// <returns>The generator.</returns>
    /// <remarks>
    /// The collection is READ, never changed, and the events the generator releases are copies -
    /// so an application can keep using its own collection.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public static ReplayMusicGenerator FromMidiEvents(string name, MidiEventCollection events)
    {
        RequireName(name);

        if (events == null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        return new ReplayMusicGenerator(name,
            "Replays a piece of MIDI supplied by the application.",
            ticks => ReplayPieceBuilder.FromMidiEvents(events, ticks,
                $"The MIDI supplied to the replay generator '{name}'"));
    }

    /// <summary>Builds a replay of a tune written in ABC notation.</summary>
    /// <param name="name">The name to register and ask for it under.</param>
    /// <param name="abcText">The ABC text. The first tune in it is the one that plays.</param>
    /// <returns>The generator.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="abcText"/> is blank.
    /// </exception>
    public static ReplayMusicGenerator FromAbc(string name, string abcText)
    {
        RequireName(name);

        if (string.IsNullOrWhiteSpace(abcText))
        {
            throw new ArgumentException("Some ABC text is required.", nameof(abcText));
        }

        return new ReplayMusicGenerator(name,
            "Replays a tune written in ABC notation, supplied by the application.",
            ticks => ReplayPieceBuilder.FromAbcText(abcText, ticks,
                $"The ABC supplied to the replay generator '{name}'"));
    }

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => Load(MusicRequest.DefaultTicksPerQuarterNote, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public void Release()
    {
        lock (gate)
        {
            piece = null;
        }
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

        return Generate(request.TicksPerQuarterNote, request.PaceInRealTime, cancellationToken);
    }

    /// <summary>
    /// Builds a replay of a piece that ships inside this library. Internal: the built-in replays
    /// are reached through <see cref="MusicGeneratorRegistry"/>, never built by a consumer.
    /// </summary>
    /// <param name="name">The reserved name of the built-in generator.</param>
    /// <param name="description">What it plays.</param>
    /// <param name="resourceName">The manifest name of the embedded piece.</param>
    /// <param name="isAbc">Whether the piece is ABC notation rather than a MIDI file.</param>
    /// <returns>The generator.</returns>
    internal static ReplayMusicGenerator FromEmbeddedMusic(string name, string description,
        string resourceName, bool isAbc)
    {
        var what = $"The music embedded in this library as '{resourceName}'";

        return new ReplayMusicGenerator(name, description, ticks => isAbc
            ? ReplayPieceBuilder.FromAbcText(ReadAbcResource(resourceName), ticks, what)
            : ReplayPieceBuilder.FromMidiBytes(EmbeddedMusicResources.Read(resourceName), ticks, what));
    }

    /// <summary>How long one pass lasts at a tick resolution, at the music's own tempo.</summary>
    /// <param name="ticksPerQuarterNote">The resolution the pass would be generated at.</param>
    /// <returns>The length of one pass.</returns>
    /// <remarks>Loads the piece if it is not loaded already.</remarks>
    internal TimeSpan PassDuration(int ticksPerQuarterNote) =>
        Load(ticksPerQuarterNote, CancellationToken.None).Duration;

    /// <summary>How long one pass is in ticks, rounded up to a whole bar.</summary>
    /// <param name="ticksPerQuarterNote">The resolution the pass would be generated at.</param>
    /// <returns>The length of one pass, in ticks.</returns>
    /// <remarks>Loads the piece if it is not loaded already.</remarks>
    internal long PassTicks(int ticksPerQuarterNote) =>
        Load(ticksPerQuarterNote, CancellationToken.None).TotalTicks;

    private async IAsyncEnumerable<GeneratedMusicEvent> Generate(int ticksPerQuarterNote, bool pace,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var prepared = Load(ticksPerQuarterNote, cancellationToken);

        double rate;
        TimeProvider clock;
        lock (gate)
        {
            rate = pacingRate;
            clock = timeProvider;
        }

        var started = clock.GetUtcNow();

        foreach (var item in prepared.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pace)
            {
                var due = started +
                          TimeSpan.FromTicks((long)(prepared.TimeOfTick(item.Tick).Ticks / rate));
                var wait = due - clock.GetUtcNow();

                if (wait > TimeSpan.Zero)
                {
                    // Task.Delay works in whole milliseconds and TRUNCATES, so a wait of 62.5 ms
                    // would release the item at 62 ms - early. Rounding the wait UP keeps the
                    // promise that nothing is ever released before its musical moment, at the cost
                    // of at most a millisecond of lateness. The due time is absolute, so the
                    // rounding does not accumulate.
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds)),
                        clock, cancellationToken).ConfigureAwait(false);
                }
            }

            yield return item.ToGeneratedEvent();
        }
    }

    private ReplayPiece Load(int ticksPerQuarterNote, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (piece != null && piece.TicksPerQuarterNote == ticksPerQuarterNote)
            {
                return piece;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Built outside the lock: reading and converting a piece is the slow part, and a generator
        // is asked for one segment at a time.
        var built = build(ticksPerQuarterNote);

        lock (gate)
        {
            piece = built;
        }

        return built;
    }

    private static string ReadAbcResource(string resourceName)
    {
        var bytes = EmbeddedMusicResources.Read(resourceName);

        return Encoding.UTF8.GetString(bytes);
    }

    private static byte[] ReadFile(string path, string what)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            throw new MusicGenerationException($"{what} could not be read: {exception.Message}",
                exception);
        }
    }

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A music generator must have a name: it is how a consumer asks for it.", nameof(name));
        }
    }
}
