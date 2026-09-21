using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Groups every test that touches process-wide state into one non-parallel collection: the music
/// generator registry, and the four built-in replay generators it hands out, are shared by the
/// whole process, so two tests configuring them at once would see each other's work.
/// </summary>
/// <remarks>
/// It covers the RENDITION table too, and the audio device. One collection rather than several,
/// because two non-parallel collections would still run alongside one another.
/// </remarks>
[CollectionDefinition("MusicGeneratorRegistry", DisableParallelization = true)]
public sealed class RegistryCollection
{
}
