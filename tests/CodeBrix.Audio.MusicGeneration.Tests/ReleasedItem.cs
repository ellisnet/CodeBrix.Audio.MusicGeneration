using System;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>One item a generator released, and the moment on the test clock it was released at.</summary>
public sealed class ReleasedItem
{
    /// <summary>Records an item and when it arrived.</summary>
    /// <param name="item">The item.</param>
    /// <param name="at">The moment on the test clock it arrived at.</param>
    public ReleasedItem(GeneratedMusicEvent item, DateTimeOffset at)
    {
        Item = item;
        At = at;
    }

    /// <summary>The item.</summary>
    public GeneratedMusicEvent Item { get; }

    /// <summary>The moment on the test clock it arrived at.</summary>
    public DateTimeOffset At { get; }
}
