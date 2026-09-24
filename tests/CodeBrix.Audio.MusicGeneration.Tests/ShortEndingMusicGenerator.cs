using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A generator whose pass ends WITHOUT its last word: its final note is never announced as
/// settled - the pass either simply stops, or throws - which is what a model's stream looks like
/// when it breaks off short.
/// </summary>
internal sealed class ShortEndingMusicGenerator : IMusicGenerator
{
    private readonly bool throwAtTheEnd;
    private int passes;

    /// <summary>Creates the generator.</summary>
    /// <param name="name">The name to register it under.</param>
    /// <param name="throwAtTheEnd">Throw instead of simply stopping.</param>
    public ShortEndingMusicGenerator(string name, bool throwAtTheEnd)
    {
        Name = name;
        this.throwAtTheEnd = throwAtTheEnd;
    }

    /// <summary>
    /// When set, only this pass (1 for the first) throws; every other pass ends normally, with its
    /// last word. When null, every pass ends as the constructor said.
    /// </summary>
    public int? OnlyPassThatThrows { get; set; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => "Test";

    /// <inheritdoc />
    public string Description => "A test generator whose pass breaks off before settling its last note.";

    /// <inheritdoc />
    public MusicRequestFeatures Honours => MusicRequestFeatures.Continuation | MusicRequestFeatures.Seed;

    /// <inheritdoc />
    public bool IsLoaded => true;

    /// <summary>How many passes it has been asked for.</summary>
    public int PassCount => Volatile.Read(ref passes);

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
        var pass = Interlocked.Increment(ref passes);
        var throws = OnlyPassThatThrows.HasValue ? pass == OnlyPassThatThrows.Value : throwAtTheEnd;
        var quarter = (long)request.TicksPerQuarterNote;

        yield return GeneratedMusicEvent.FromMidiEvent(new TempoEvent(500000, 0L), GeneratedMusicEvent.NothingSettled);

        for (var beat = 0; beat < 8; beat++)
        {
            await Task.Yield();

            // Each note settles everything before it; the LAST one is never settled at all.
            var note = new NoteOnEvent(beat * quarter, 1, 60 + beat, 100, (int)quarter);

            yield return GeneratedMusicEvent.FromMidiEvent(note,
                beat == 0 ? GeneratedMusicEvent.NothingSettled : (beat * quarter) - 1L);
        }

        if (throws)
        {
            throw new InvalidOperationException("The stream broke off.");
        }

        if (OnlyPassThatThrows.HasValue)
        {
            yield return GeneratedMusicEvent.SettledThrough(8L * quarter);
        }
    }
}
