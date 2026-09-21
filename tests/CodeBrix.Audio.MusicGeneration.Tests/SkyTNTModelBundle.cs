using System;
using System.IO;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Where the tests that really load the SkyTNT model find it: AN ENVIRONMENT VARIABLE, and nothing
/// else.
/// </summary>
/// <remarks>
/// NO PATH OUTSIDE THIS REPOSITORY IS WRITTEN DOWN ANYWHERE IN IT. A model is hundreds of
/// megabytes and is not in the repository, so the tests that need one are told where it is and
/// SKIP, saying why, when they are not. When the model ships as a package these tests are
/// repointed at the package and the variable is retired.
/// </remarks>
internal static class SkyTNTModelBundle
{
    /// <summary>The variable holding the folder the SkyTNT bundle's files are in.</summary>
    public const string VariableName = "CODEBRIX_AUDIO_MUSICGEN_SKYTNT_BUNDLE";

    /// <summary>What a skipped test says instead of running.</summary>
    public const string SkipReason =
        "Set " + VariableName + " to the folder holding the SkyTNT bundle - config.json, " +
        "model_base.onnx and model_token.onnx - to run the tests that load the model.";

    private static readonly string[] Required =
    {
        "config.json", "model_base.onnx", "model_token.onnx"
    };

    /// <summary>The folder the variable names, or null when it is not set.</summary>
    public static string Directory => Environment.GetEnvironmentVariable(VariableName);

    /// <summary>Whether the variable names a folder that holds a runnable bundle.</summary>
    public static bool IsAvailable
    {
        get
        {
            var directory = Directory;

            if (string.IsNullOrWhiteSpace(directory) ||
                !System.IO.Directory.Exists(directory))
            {
                return false;
            }

            foreach (var name in Required)
            {
                if (!File.Exists(Path.Combine(directory, name)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The bundle's files as a map of the publisher's name against the path it is at.</summary>
    /// <returns>The map, for the road that takes paths rather than a folder.</returns>
    public static System.Collections.Generic.Dictionary<string, string> Files()
    {
        var directory = Directory;
        var files = new System.Collections.Generic.Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in Required)
        {
            files[name] = Path.Combine(directory, name);
        }

        return files;
    }
}
