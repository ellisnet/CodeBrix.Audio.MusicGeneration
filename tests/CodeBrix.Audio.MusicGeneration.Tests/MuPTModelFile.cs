using System;
using System.IO;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Where the tests that really load the MuPT model find it: AN ENVIRONMENT VARIABLE, and nothing
/// else.
/// </summary>
/// <remarks>
/// NO PATH OUTSIDE THIS REPOSITORY IS WRITTEN DOWN ANYWHERE IN IT. A model is a hundred megabytes
/// and more and is not in the repository, so the tests that need one are told where it is and
/// SKIP, saying why, when they are not. When the model ships as a package these tests are
/// repointed at the package and the variable is retired.
/// </remarks>
internal static class MuPTModelFile
{
    /// <summary>The variable holding the path of the MuPT model's GGUF file.</summary>
    public const string VariableName = "CODEBRIX_AUDIO_MUSICGEN_MUPT_MODEL";

    /// <summary>What a skipped test says instead of running.</summary>
    public const string SkipReason =
        "Set " + VariableName + " to the path of a MuPT GGUF file - the tokenizer is inside it, " +
        "so one path is the whole of it - to run the tests that load the model.";

    /// <summary>The path the variable names, or null when it is not set.</summary>
    public static string Path => Environment.GetEnvironmentVariable(VariableName);

    /// <summary>Whether the variable names a file that is really there.</summary>
    public static bool IsAvailable
    {
        get
        {
            var path = Path;

            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
    }
}
