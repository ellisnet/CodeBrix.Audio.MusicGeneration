using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Replay;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A replay that takes a real, fixed time over every item it yields, whatever the request says
/// about pacing - which is what a model looks like to an OFFLINE render: a render writes audio far
/// faster than real time, so a generator that is "slow" by that standard is always behind it.
/// </summary>
internal sealed class SlowMusicGenerator : IMusicGenerator
{
    private readonly ReplayMusicGenerator inner;
    private readonly TimeSpan perItem;

    /// <summary>Wraps a replay.</summary>
    /// <param name="name">The name to register it under.</param>
    /// <param name="inner">The replay underneath.</param>
    /// <param name="perItem">How long each item takes.</param>
    public SlowMusicGenerator(string name, ReplayMusicGenerator inner, TimeSpan perItem)
    {
        Name = name;
        this.inner = inner;
        this.perItem = perItem;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => "Test";

    /// <inheritdoc />
    public string Description => "A test replay that takes its time over every item.";

    /// <inheritdoc />
    public MusicRequestFeatures Honours => inner.Honours;

    /// <inheritdoc />
    public bool IsLoaded => true;

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
        await foreach (var item in inner.GenerateAsync(request, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await Task.Delay(perItem, cancellationToken).ConfigureAwait(false);

            yield return item;
        }
    }
}
