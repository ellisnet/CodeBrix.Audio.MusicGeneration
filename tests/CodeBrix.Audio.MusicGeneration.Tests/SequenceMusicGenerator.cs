using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Replay;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A generator that writes a DIFFERENT piece on every pass - the first, then the second, and so
/// on, staying on the last - which is how a model's fresh pieces look to the engine: each one at
/// a tempo of its own.
/// </summary>
internal sealed class SequenceMusicGenerator : IMusicGenerator
{
    private readonly IReadOnlyList<ReplayMusicGenerator> pieces;
    private readonly List<MusicRequest> requests = new List<MusicRequest>();

    /// <summary>Creates the generator.</summary>
    /// <param name="name">The name to register it under.</param>
    /// <param name="pieces">The pieces, one per pass.</param>
    /// <param name="honours">What it declares it acts on.</param>
    public SequenceMusicGenerator(string name, IReadOnlyList<ReplayMusicGenerator> pieces,
        MusicRequestFeatures honours = MusicRequestFeatures.Continuation | MusicRequestFeatures.Seed)
    {
        Name = name;
        this.pieces = pieces;
        Honours = honours;
    }

    /// <summary>
    /// How long every pass after the first takes before it writes anything, on
    /// <see cref="Clock"/> - the way a model spends seconds before its first bar.
    /// </summary>
    public TimeSpan FirstItemDelay { get; set; }

    /// <summary>The clock <see cref="FirstItemDelay"/> is measured on.</summary>
    public TimeProvider Clock { get; set; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => "Test";

    /// <inheritdoc />
    public string Description => "A test generator that writes a different piece on every pass.";

    /// <inheritdoc />
    public MusicRequestFeatures Honours { get; }

    /// <inheritdoc />
    public bool IsLoaded => true;

    /// <summary>How many passes it has been asked for.</summary>
    public int PassCount
    {
        get { lock (requests) { return requests.Count; } }
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
        int pass;

        lock (requests)
        {
            pass = requests.Count;
            requests.Add(request.Clone());
        }

        var piece = pieces[pass < pieces.Count ? pass : pieces.Count - 1];
        var passed = new MusicRequest
        {
            TicksPerQuarterNote = request.TicksPerQuarterNote,
            PaceInRealTime = request.PaceInRealTime
        };

        if (pass > 0 && FirstItemDelay > TimeSpan.Zero && Clock != null)
        {
            await Task.Delay(FirstItemDelay, Clock, cancellationToken).ConfigureAwait(false);
        }

        await foreach (var item in piece.GenerateAsync(passed, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
