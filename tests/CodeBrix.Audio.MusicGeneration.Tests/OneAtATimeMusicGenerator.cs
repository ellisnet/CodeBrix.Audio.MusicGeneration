using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A generator that behaves the way a real model does: it writes ONE piece at a time, and a second
/// generation asked for while the first is still running is REFUSED.
/// </summary>
/// <remarks>
/// It exists for the follow-up prompt. A test generator that happily runs twice over would say a
/// follow-up over one generator works when, against a model, it would not - so this one counts how
/// many generations are running at once and remembers the highest number it ever saw.
/// </remarks>
internal sealed class OneAtATimeMusicGenerator : IMusicGenerator
{
    private readonly object gate = new object();
    private readonly IMusicGenerator inner;

    private int running;
    private int highestConcurrent;
    private int refusalCount;
    private int startCount;

    /// <summary>Wraps a generator so that it refuses a second concurrent generation.</summary>
    /// <param name="inner">The generator that really produces the music.</param>
    public OneAtATimeMusicGenerator(IMusicGenerator inner) => this.inner = inner;

    /// <inheritdoc />
    public string Name => inner.Name;

    /// <inheritdoc />
    public string Family => inner.Family;

    /// <inheritdoc />
    public string Description => inner.Description;

    /// <inheritdoc />
    public MusicRequestFeatures Honours => inner.Honours;

    /// <inheritdoc />
    public bool IsLoaded => inner.IsLoaded;

    /// <summary>The generator underneath, for a test that wants to ask it what it was asked for.</summary>
    public IMusicGenerator Inner => inner;

    /// <summary>The most generations that were ever running on this instance at one moment.</summary>
    public int HighestConcurrentGenerations
    {
        get { lock (gate) { return highestConcurrent; } }
    }

    /// <summary>How many times a second concurrent generation was refused.</summary>
    public int RefusalCount
    {
        get { lock (gate) { return refusalCount; } }
    }

    /// <summary>How many generations have been started.</summary>
    public int StartCount
    {
        get { lock (gate) { return startCount; } }
    }

    /// <summary>Whether a generation is running on this instance at this moment.</summary>
    public bool IsGenerating
    {
        get { lock (gate) { return running > 0; } }
    }

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken) =>
        inner.PreloadAsync(cancellationToken);

    /// <inheritdoc />
    public void Release() => inner.Release();

    /// <inheritdoc />
    public IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
        CancellationToken cancellationToken) => Generate(request, cancellationToken);

    private async IAsyncEnumerable<GeneratedMusicEvent> Generate(MusicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Start();

        try
        {
            await foreach (var item in inner.GenerateAsync(request, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            End();
        }
    }

    private void Start()
    {
        lock (gate)
        {
            if (running > 0)
            {
                refusalCount++;

                throw new MusicGenerationException(
                    $"The music generator '{Name}' is already writing a piece.");
            }

            running++;
            startCount++;

            if (running > highestConcurrent)
            {
                highestConcurrent = running;
            }
        }
    }

    private void End()
    {
        lock (gate)
        {
            running--;
        }
    }
}
