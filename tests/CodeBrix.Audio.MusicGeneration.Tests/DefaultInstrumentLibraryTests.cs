using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.ModestSynth;
using SilverAssertions;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The two things that can only be proved against the REAL instrument-library registry: what
/// happens when nothing at all is registered, and what "the default library" means.
/// </summary>
/// <remarks>
/// <para>
/// RUN THIS BY ITSELF, IN A FRESH PROCESS. The registry is process-wide and cannot be emptied from
/// outside CodeBrix.Audio, so the empty case exists only before any other test has registered
/// anything - and registering the General MIDI library here makes it the process default, which
/// every other test is written never to depend on.
/// </para>
/// <code>
/// CODEBRIX_AUDIO_RUN_DEFAULT_INSTRUMENT_TESTS=1 \
///   dotnet run --project tests/CodeBrix.Audio.MusicGeneration.Tests \
///   -- --filter-class CodeBrix.Audio.MusicGeneration.Tests.DefaultInstrumentLibraryTests
/// </code>
/// <para>
/// Everything else in this suite reaches the same two behaviours through an internal seam, so the
/// gate is a confirmation rather than the only cover.
/// </para>
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class DefaultInstrumentLibraryTests
{
    private static readonly bool DefaultLibraryTestsEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_DEFAULT_INSTRUMENT_TESTS") == "1";

    private const string SkipReason =
        "Set CODEBRIX_AUDIO_RUN_DEFAULT_INSTRUMENT_TESTS=1, and run this class by itself, to run " +
        "tests that depend on the process-wide default instrument library.";

    [Fact]
    public void the_real_registry_gives_the_no_library_exception_and_then_the_default_library()
    {
        Assert.SkipUnless(DefaultLibraryTestsEnabled, SkipReason);

        //Arrange - nothing is registered yet, which is only true in a fresh process
        InstrumentLibraryRegistry.DefaultName.Should().BeNull();

        using var before = new MusicSession(new MusicGenerationOptions
        {
            ApplicationOwnsAudioOutput = true
        });

        Action act = () => before.Play();

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert - word for word, and it names the class the consumer already has
        thrown.Message.Should().Be(
            "No instrument library is registered, so no music can be played or rendered. Register " +
            "the provided ModestSynth.GeneralMidiInstrumentLibrary - call " +
            "GeneralMidiInstrumentLibrary.Register() - or register another instrument library first.");

        //Arrange - one line, and there is an instrument library
        GeneralMidiInstrumentLibrary.Register();

        using var after = new MusicSession(new MusicGenerationOptions
        {
            ApplicationOwnsAudioOutput = true,
            Preroll = TimeSpan.FromSeconds(0.5)
        });

        //Act - nothing is named, so the default library is the one that plays
        after.Play();

        //Assert
        after.ActiveSource.InstrumentLibraryName.Should().Be(GeneralMidiInstrumentLibrary.LibraryName);
        InstrumentLibraryRegistry.DefaultName.Should().Be(GeneralMidiInstrumentLibrary.LibraryName);
    }
}
