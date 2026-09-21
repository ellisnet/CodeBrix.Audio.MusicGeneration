using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Replay;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A generator that plays a piece and writes down everything that happened to it: every request it
/// was asked with, how many items it was pulled for, and how many times it was loaded and released.
/// </summary>
/// <remarks>
/// <para>
/// It is a replay underneath, so the pacing, the settled ticks and the bar-rounded pass length are
/// the real ones rather than a second implementation of them. What it adds is the bookkeeping the
/// lifecycle tests ask questions of: WAS it asked again, WITH what, and how many items came out
/// before the engine stopped pulling.
/// </para>
/// <para>
/// It declares that it honours nearly everything, so that a test can put a seed or a thread count
/// in a request without the capability check refusing it.
/// </para>
/// </remarks>
internal sealed class RecordingMusicGenerator : IMusicGenerator
{
    /// <summary>What it says it honours unless a test asks for something narrower.</summary>
    public const MusicRequestFeatures Everything =
        MusicRequestFeatures.Continuation | MusicRequestFeatures.Seed |
        MusicRequestFeatures.InferenceThreadCount | MusicRequestFeatures.SamplingControls |
        MusicRequestFeatures.FreeText | MusicRequestFeatures.Primer |
        MusicRequestFeatures.InstrumentHints | MusicRequestFeatures.MaximumEvents;

    private readonly object gate = new object();
    private readonly ReplayMusicGenerator inner;
    private readonly List<MusicRequest> requests = new List<MusicRequest>();

    private int itemCount;
    private int loadCount;
    private int releaseCount;
    private bool loaded;

    /// <summary>Creates a generator over a piece.</summary>
    /// <param name="name">The name it is asked for under.</param>
    /// <param name="inner">The replay that really produces the music.</param>
    /// <param name="honours">What it declares it honours.</param>
    public RecordingMusicGenerator(string name, ReplayMusicGenerator inner,
        MusicRequestFeatures honours = Everything)
    {
        Name = name;
        this.inner = inner;
        Honours = honours;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => "Test";

    /// <inheritdoc />
    public string Description => "A test generator that records what it was asked for.";

    /// <inheritdoc />
    public MusicRequestFeatures Honours { get; }

    /// <inheritdoc />
    public bool IsLoaded
    {
        get { lock (gate) { return loaded; } }
    }

    /// <summary>The replay underneath, whose pacing rate and clock a test sets.</summary>
    public ReplayMusicGenerator Inner => inner;

    /// <summary>Every request it was asked with, in order, as it was at the moment it was asked.</summary>
    public IReadOnlyList<MusicRequest> Requests
    {
        get { lock (gate) { return requests.ToArray(); } }
    }

    /// <summary>How many passes it has been asked for.</summary>
    public int PassCount
    {
        get { lock (gate) { return requests.Count; } }
    }

    /// <summary>How many items have been pulled out of it altogether.</summary>
    public int ItemCount => Volatile.Read(ref itemCount);

    /// <summary>How many times it has been loaded.</summary>
    public int LoadCount
    {
        get { lock (gate) { return loadCount; } }
    }

    /// <summary>How many times it has been released.</summary>
    public int ReleaseCount
    {
        get { lock (gate) { return releaseCount; } }
    }

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken)
    {
        Load();

        return inner.PreloadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Release()
    {
        lock (gate)
        {
            releaseCount++;
            loaded = false;
        }

        inner.Release();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        MusicGeneratorCapabilities.EnsureHonoured(this, request);

        lock (gate)
        {
            requests.Add(request.Clone());
        }

        Load();

        // The replay underneath honours a continuation and nothing else, so only the parts of the
        // request that are its business are passed on.
        var passed = new MusicRequest
        {
            TicksPerQuarterNote = request.TicksPerQuarterNote,
            PaceInRealTime = request.PaceInRealTime,
            Continuation = request.Continuation
        };

        await foreach (var item in inner.GenerateAsync(passed, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Increment(ref itemCount);

            yield return item;
        }
    }

    private void Load()
    {
        lock (gate)
        {
            if (loaded)
            {
                return;
            }

            loadCount++;
            loaded = true;
        }
    }
}
