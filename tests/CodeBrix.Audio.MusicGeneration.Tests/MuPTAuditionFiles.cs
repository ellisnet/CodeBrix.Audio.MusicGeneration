using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The thirty-two tunes the MuPT model wrote for the listening sessions, each with the model's raw
/// text beside it - the fence the incremental path is held to.
/// </summary>
/// <remarks>
/// They ship with this test project and are copied beside the test assembly, so nothing here ever
/// reaches outside the repository. THIRD-PARTY-NOTICES.txt records where they came from.
/// </remarks>
public static class MuPTAuditionFiles
{
    private static readonly string[] Tags = { "q4_k_m", "q8_0" };

    /// <summary>The folder the files are in, beside the test assembly.</summary>
    public static string Folder =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "mupt-audition");

    /// <summary>Every audition file, in a stable order.</summary>
    /// <returns>One entry per tune.</returns>
    public static IReadOnlyList<MuPTAuditionFile> All()
    {
        var files = new List<MuPTAuditionFile>();
        var names = Directory.GetFiles(Folder, "*.raw.txt");

        Array.Sort(names, StringComparer.Ordinal);

        foreach (var raw in names)
        {
            var stem = Path.GetFileName(raw);
            stem = stem.Substring(0, stem.Length - ".raw.txt".Length);

            files.Add(new MuPTAuditionFile(stem, File.ReadAllText(raw),
                File.ReadAllText(Path.Combine(Folder, stem + ".abc")), TitleOf(stem)));
        }

        return files;
    }

    // The title the scratch generator wrote into each tune: "<piece> (MuPT 190M <tag>, seed <n>)".
    private static string TitleOf(string stem)
    {
        var rest = stem.Substring("mupt-".Length);
        var tag = string.Empty;

        foreach (var candidate in Tags)
        {
            if (rest.StartsWith(candidate + "-", StringComparison.Ordinal))
            {
                tag = candidate;
                rest = rest.Substring(candidate.Length + 1);

                break;
            }
        }

        var seedAt = rest.LastIndexOf("-seed", StringComparison.Ordinal);
        var piece = rest.Substring(0, seedAt);
        var seed = rest.Substring(seedAt + "-seed".Length);

        return piece + " (MuPT 190M " + tag + ", seed " + seed + ")";
    }
}

/// <summary>One audition tune: what the model was given and wrote, and what it was turned into.</summary>
public sealed class MuPTAuditionFile
{
    /// <summary>Creates the entry.</summary>
    /// <param name="name">The file's stem, which names the piece, the quantization and the seed.</param>
    /// <param name="rawText">The model's own text: the prompt and everything it wrote.</param>
    /// <param name="standardAbc">The standard ABC notation that text was turned into.</param>
    /// <param name="title">The title the tune was given.</param>
    public MuPTAuditionFile(string name, string rawText, string standardAbc, string title)
    {
        Name = name;
        RawText = rawText;
        StandardAbc = standardAbc;
        Title = title;
    }

    /// <summary>The file's stem.</summary>
    public string Name { get; }

    /// <summary>The model's own text.</summary>
    public string RawText { get; }

    /// <summary>The standard ABC notation it was turned into.</summary>
    public string StandardAbc { get; }

    /// <summary>The title the tune was given.</summary>
    public string Title { get; }

    /// <summary>Names the tune, so a failure says which one it was.</summary>
    /// <returns>The file's stem.</returns>
    public override string ToString() => Name;
}
