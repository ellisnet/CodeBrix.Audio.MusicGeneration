using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// What a SkyTNT bundle has to hold, and the error a folder that holds nothing runnable gets.
/// </summary>
/// <remarks>
/// A MODEL IS NOT ONE FILE. This bundle is a configuration and two graphs, and they may be laid
/// out as a directory or handed over as a map of the publisher's name for each file against the
/// path it is really at - which is what a store that keeps its files under digests needs.
/// </remarks>
internal static class SkyTNTBundle
{
    /// <summary>What the bundle's configuration is called.</summary>
    public const string ConfigurationFileName = "config.json";

    /// <summary>What the graph that reads the events so far is called.</summary>
    public const string BaseGraphFileName = "model_base.onnx";

    /// <summary>What the graph that writes an event's tokens is called.</summary>
    public const string TokenGraphFileName = "model_token.onnx";

    private static readonly string[] Required =
    {
        ConfigurationFileName, BaseGraphFileName, TokenGraphFileName
    };

    /// <summary>Checks that a directory holds a bundle this library can run.</summary>
    /// <param name="directory">The directory.</param>
    /// <exception cref="MusicGenerationException">
    /// The directory is not there, or does not hold all three of the bundle's files.
    /// </exception>
    public static void RequireRunnable(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new MusicGenerationException(
                $"There is no SkyTNT model in '{directory}': the folder does not exist. " + WhatIsWanted());
        }

        var missing = new List<string>();

        foreach (var name in Required)
        {
            if (!File.Exists(Path.Combine(directory, name)))
            {
                missing.Add(name);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        throw new MusicGenerationException(
            $"There is no SkyTNT model in '{directory}': it is missing {Join(missing)}. " +
            WhatIsThere(directory) + " " + WhatIsWanted());
    }

    /// <summary>Copies a map of the bundle's files and checks that all three are named.</summary>
    /// <param name="files">The publisher's name for each file against the path it is really at.</param>
    /// <returns>A copy, so that a later edit cannot change a generator already built.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is null.</exception>
    /// <exception cref="MusicGenerationException">One of the bundle's files is not named.</exception>
    public static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string> files)
    {
        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in files)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                copy[pair.Key] = pair.Value;
            }
        }

        var missing = new List<string>();

        foreach (var name in Required)
        {
            if (!copy.ContainsKey(name))
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            throw new MusicGenerationException(
                $"The files given for a SkyTNT model are missing {Join(missing)}. " +
                $"What was given: {(copy.Count == 0 ? "nothing" : Join(new List<string>(copy.Keys)))}. " +
                WhatIsWanted());
        }

        return copy;
    }

    private static string WhatIsWanted() =>
        $"A SkyTNT bundle holds '{ConfigurationFileName}', '{BaseGraphFileName}' and " +
        $"'{TokenGraphFileName}'. Point the generator at a folder holding those three files, or " +
        "hand it a map of those names against the paths they are really at - which is what " +
        "CodeBrix.Ollama.ModelManager's ResolveAsync gives you for a model it holds, and what its " +
        "MaterializeAsync lays out as a folder.";

    private static string WhatIsThere(string directory)
    {
        string[] found;

        try
        {
            found = Directory.GetFileSystemEntries(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "What is in it could not be read: " + exception.Message + ".";
        }

        if (found.Length == 0)
        {
            return "The folder is empty.";
        }

        var names = new List<string>();

        for (var i = 0; i < found.Length && i < 12; i++)
        {
            names.Add(Path.GetFileName(found[i]));
        }

        var what = "What is in it: " + Join(names);

        return found.Length > names.Count
            ? what + $", and {found.Length - names.Count} more."
            : what + ".";
    }

    private static string Join(List<string> names)
    {
        var text = new StringBuilder();

        for (var i = 0; i < names.Count; i++)
        {
            if (i > 0)
            {
                text.Append(i == names.Count - 1 ? " and " : ", ");
            }

            text.Append('\'').Append(names[i]).Append('\'');
        }

        return text.ToString();
    }
}
