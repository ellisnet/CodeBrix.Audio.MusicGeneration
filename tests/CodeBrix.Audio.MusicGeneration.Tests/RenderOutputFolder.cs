using System;
using System.IO;
using System.Threading;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A folder for the files a render test writes, INSIDE this project's own build output, removed
/// again when the test finishes.
/// </summary>
/// <remarks>
/// <para>
/// A TEST NEVER WRITES OUTSIDE THE REPOSITORY, and it never shares a file name with another test
/// or another run: a name of its own per process and per folder is what keeps two suites running
/// at once from deleting each other's files.
/// </para>
/// </remarks>
internal sealed class RenderOutputFolder : IDisposable
{
    private static int counter;

    /// <summary>Makes the folder.</summary>
    /// <param name="name">A name for it, usually what the test is about.</param>
    public RenderOutputFolder(string name)
    {
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, "render-output",
            $"{name}-{Environment.ProcessId}-{Interlocked.Increment(ref counter)}");

        Directory.CreateDirectory(Path);
    }

    /// <summary>Where the folder is.</summary>
    public string Path { get; }

    /// <summary>A path inside the folder.</summary>
    /// <param name="fileName">The name of the file.</param>
    /// <returns>The full path.</returns>
    public string File(string fileName) => System.IO.Path.Combine(Path, fileName);

    /// <summary>Removes the folder and everything in it.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file left behind is not worth failing a test that has already passed.
        }
    }
}
