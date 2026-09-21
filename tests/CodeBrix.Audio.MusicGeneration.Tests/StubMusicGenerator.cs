using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A generator that exists to be registered, resolved and asked what it honours. It produces one
/// note, which is enough for a test that is about the registry rather than about the music.
/// </summary>
public sealed class StubMusicGenerator : IMusicGenerator
{
    /// <summary>Creates a stub that honours nothing.</summary>
    /// <param name="name">The name to register it under.</param>
    public StubMusicGenerator(string name)
        : this(name, MusicRequestFeatures.None)
    {
    }

    /// <summary>Creates a stub that honours a chosen set of request parts.</summary>
    /// <param name="name">The name to register it under.</param>
    /// <param name="honours">What it declares it acts on.</param>
    public StubMusicGenerator(string name, MusicRequestFeatures honours)
    {
        Name = name;
        Honours = honours;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => "Test";

    /// <inheritdoc />
    public string Description => $"A generator called '{Name}', used by the tests.";

    /// <inheritdoc />
    public MusicRequestFeatures Honours { get; }

    /// <inheritdoc />
    public bool IsLoaded { get; private set; }

    /// <summary>How many times the generator has been asked to load.</summary>
    public int PreloadCount { get; private set; }

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PreloadCount++;
        IsLoaded = true;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Release() => IsLoaded = false;

    /// <inheritdoc />
    public IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        MusicGeneratorCapabilities.EnsureHonoured(this, request);

        return Generate(request.TicksPerQuarterNote);
    }

    private async IAsyncEnumerable<GeneratedMusicEvent> Generate(int ticksPerQuarterNote)
    {
        // An already-completed await: it keeps the iterator synchronous while giving the compiler
        // the await it wants from an async method.
        await Task.CompletedTask.ConfigureAwait(false);

        IsLoaded = true;

        yield return GeneratedMusicEvent.FromMidiEvent(
            new NoteOnEvent(0L, 1, 60, 100, ticksPerQuarterNote), 0L);

        yield return GeneratedMusicEvent.SettledThrough(ticksPerQuarterNote);
    }
}
