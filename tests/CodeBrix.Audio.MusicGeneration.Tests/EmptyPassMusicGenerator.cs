using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Replay;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A replay that answers some requests with NOTHING - no events and no settled tick, the way a
/// model does when it decides the piece is already over.
/// </summary>
internal sealed class EmptyPassMusicGenerator : IMusicGenerator
{
    private readonly ReplayMusicGenerator inner;
    private readonly bool emptyWhenPrimed;
    private readonly bool alwaysEmpty;
    private readonly List<MusicRequest> requests = new List<MusicRequest>();

    /// <summary>Wraps a replay.</summary>
    /// <param name="name">The name to register it under.</param>
    /// <param name="inner">The replay underneath.</param>
    /// <param name="emptyWhenPrimed">Answer every request carrying a continuation with nothing.</param>
    /// <param name="alwaysEmpty">Answer every request with nothing.</param>
    public EmptyPassMusicGenerator(string name, ReplayMusicGenerator inner, bool emptyWhenPrimed,
        bool alwaysEmpty)
    {
        Name = name;
        this.inner = inner;
        this.emptyWhenPrimed = emptyWhenPrimed;
        this.alwaysEmpty = alwaysEmpty;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => "Test";

    /// <inheritdoc />
    public string Description => "A test replay that sometimes writes nothing at all.";

    /// <inheritdoc />
    public MusicRequestFeatures Honours => MusicRequestFeatures.Continuation | MusicRequestFeatures.Seed;

    /// <inheritdoc />
    public bool IsLoaded => true;

    /// <summary>Every request it was asked with, in order.</summary>
    public IReadOnlyList<MusicRequest> Requests
    {
        get { lock (requests) { return requests.ToArray(); } }
    }

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Release()
    {
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lock (requests)
        {
            requests.Add(request.Clone());
        }

        if (alwaysEmpty || (emptyWhenPrimed && request.Continuation != null))
        {
            yield break;
        }

        var passed = new MusicRequest
        {
            TicksPerQuarterNote = request.TicksPerQuarterNote,
            PaceInRealTime = request.PaceInRealTime
        };

        await foreach (var item in inner.GenerateAsync(passed, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
