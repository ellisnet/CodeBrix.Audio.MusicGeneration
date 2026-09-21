using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// The entry point: what a session plays, what it plays it with, what it refuses, and what it
/// says it is doing. Nothing here opens the audio device.
/// </summary>
[Collection("MusicGeneratorRegistry")]
public class MusicSessionTests
{
    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicSessionTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    [Fact]
    public void with_nothing_registered_at_all_Play_says_what_to_register()
    {
        //Arrange
        using var session = new MusicSession(SilentOptions(), TestInstrumentLibraries.EmptyLookup(),
            new ManualTimeProvider());
        Action act = () => session.Play();

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Be(
            "No instrument library is registered, so no music can be played or rendered. Register " +
            "the provided ModestSynth.GeneralMidiInstrumentLibrary - call " +
            "GeneralMidiInstrumentLibrary.Register() - or register another instrument library first.");
    }

    [Fact]
    public void the_no_library_message_names_the_class_a_consumer_already_has() =>
        MusicSession.NoInstrumentLibraryMessage.Should().Contain("GeneralMidiInstrumentLibrary.Register()");

    [Fact]
    public void the_no_library_message_is_checked_before_anything_else_in_the_options_is()
    {
        //Arrange - a generator name that is not registered, which would be an error of its own
        var options = SilentOptions();
        options.Generator = "NotRegistered";
        using var session = new MusicSession(options, TestInstrumentLibraries.EmptyLookup(),
            new ManualTimeProvider());
        Action act = () => session.Play();

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Be(MusicSession.NoInstrumentLibraryMessage);
    }

    [Fact]
    public void with_nothing_specified_the_embedded_replay_plays()
    {
        //Act
        using var session = Play(SilentOptions());

        //Assert
        session.ActiveSource.GeneratorName.Should().Be(EmbeddedReplay.Midi);
        session.ActiveSource.IsReplay.Should().BeTrue();
    }

    [Fact]
    public void with_one_other_generator_registered_and_none_specified_the_embedded_replay_still_plays()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));

        //Act
        using var session = Play(SilentOptions());

        //Assert
        session.ActiveSource.GeneratorName.Should().Be(EmbeddedReplay.Midi);
    }

    [Fact]
    public void with_two_other_generators_registered_and_none_specified_the_embedded_replay_still_plays()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));
        MusicGeneratorRegistry.Register(new StubMusicGenerator("SkyTNT"));

        //Act
        using var session = Play(SilentOptions());

        //Assert
        session.ActiveSource.GeneratorName.Should().Be(EmbeddedReplay.Midi);
        session.ActiveSource.GeneratorFamily.Should().Be(ReplayMusicGenerator.ReplayFamily);
    }

    [Fact]
    public void a_generator_that_is_registered_and_named_is_the_one_that_plays()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));
        var options = SilentOptions();
        options.Generator = "MuPT";

        //Act
        using var session = Play(options);

        //Assert
        session.ActiveSource.GeneratorName.Should().Be("MuPT");
        session.ActiveSource.IsReplay.Should().BeFalse();
    }

    [Fact]
    public void a_generator_name_that_is_not_registered_is_an_error_listing_what_is()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new StubMusicGenerator("MuPT"));
        var options = SilentOptions();
        options.Generator = "SkyTNT";
        using var session = Session(options);
        Action act = () => session.Play();

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Be(
            "No music generator named 'SkyTNT' is registered. Registered generators: " +
            "EmbeddedReplayMidi, EmbeddedReplayMidiSecond, EmbeddedReplayAbc, " +
            "EmbeddedReplayAbcSecond, MuPT. Register it before asking for it by name.");
    }

    [Fact]
    public void a_rendition_name_that_is_not_registered_is_an_error_listing_what_is()
    {
        //Arrange
        var options = SilentOptions();
        options.Rendition = "Nope";
        using var session = Session(options);
        Action act = () => session.Play();

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Contain("No rendition named 'Nope' is registered");
    }

    [Fact]
    public void an_instrument_library_name_that_is_not_registered_is_CodeBrix_Audios_own_error()
    {
        //Arrange
        var options = SilentOptions();
        options.InstrumentLibrary = "NotALibrary";
        using var session = Session(options);
        Action act = () => session.Play();

        //Act
        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        //Assert
        thrown.Message.Should().Contain("No instrument library named 'NotALibrary' is registered");
        thrown.Message.Should().Contain("Registered libraries:");
    }

    [Fact]
    public void the_active_source_names_the_generator_the_library_and_the_rendition()
    {
        //Arrange
        var options = SilentOptions();
        options.InstrumentLibrary = TestInstrumentLibraries.CompleteName;
        options.Rendition = BuiltInRenditions.AmbientDuet;
        options.Generator = EmbeddedReplay.Abc;

        //Act
        using var session = Play(options);
        var source = session.ActiveSource;

        //Assert
        source.GeneratorName.Should().Be(EmbeddedReplay.Abc);
        source.GeneratorFamily.Should().Be(ReplayMusicGenerator.ReplayFamily);
        source.GeneratorDescription.Should().NotBeEmpty();
        source.InstrumentLibraryName.Should().Be(TestInstrumentLibraries.CompleteName);
        source.RenditionName.Should().Be(BuiltInRenditions.AmbientDuet);
        source.ToString().Should().Contain(EmbeddedReplay.Abc);
    }

    [Fact]
    public void the_active_source_is_null_until_the_music_starts()
    {
        //Arrange
        using var session = Session(SilentOptions());

        //Assert
        session.ActiveSource.Should().BeNull();
    }

    [Fact]
    public void a_request_the_generator_cannot_honour_is_refused_on_the_line_that_started_it()
    {
        //Arrange
        var options = SilentOptions();
        options.Request.Seed = 42;
        using var session = Session(options);
        Action act = () => session.Play();

        //Act
        var thrown = act.Should().Throw<MusicRequestNotHonouredException>().Which;

        //Assert
        thrown.Message.Should().Contain("seed");
        thrown.GeneratorName.Should().Be(EmbeddedReplay.Midi);
    }

    [Fact]
    public void a_bare_session_builds_a_request_the_embedded_replay_accepts()
    {
        //Act
        using var session = Play(SilentOptions());

        //Assert
        session.IsPlaying.Should().BeTrue();
        session.GenerationError.Should().BeNull();
    }

    [Fact]
    public void an_application_that_owns_its_audio_output_is_handed_a_renderer()
    {
        //Act
        using var session = Play(SilentOptions());

        //Assert
        session.Renderer.Should().NotBeNull();
    }

    [Fact]
    public void the_renderer_only_exists_once_the_music_has_started()
    {
        //Arrange
        using var session = Session(SilentOptions());

        //Assert
        session.Renderer.Should().BeNull();
    }

    [Fact]
    public void playing_twice_does_not_start_a_second_piece()
    {
        //Arrange
        using var session = Play(SilentOptions());
        var first = session.Stream;

        //Act
        session.Play();

        //Assert
        session.Stream.Should().BeSameAs(first);
    }

    [Fact]
    public void playing_again_after_stopping_starts_afresh_with_the_same_options()
    {
        //Arrange - a game stops its music on one screen and starts it on the next
        using var session = Play(SilentOptions());
        var first = session.Stream;
        session.Stop();

        //Act
        session.Play();

        //Assert
        session.IsPlaying.Should().BeTrue();
        session.Stream.Should().NotBeSameAs(first);
        session.Position.Should().Be(TimeSpan.Zero);
        session.ActiveSource.GeneratorName.Should().Be(EmbeddedReplay.Midi);
        session.GenerationError.Should().BeNull();
    }

    [Fact]
    public void a_disposed_session_is_finished_for_good()
    {
        //Arrange
        var session = Play(SilentOptions());
        session.Dispose();
        Action act = () => session.Play();

        //Act and Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void a_disposed_session_refuses_to_play()
    {
        //Arrange
        var session = new MusicSession(SilentOptions(), TestInstrumentLibraries.Lookup(),
            new ManualTimeProvider());
        session.Dispose();
        Action act = () => session.Play();

        //Act and Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void the_options_the_session_holds_are_a_copy_of_the_ones_it_was_given()
    {
        //Arrange
        var options = SilentOptions();

        //Act
        using var session = Session(options);
        options.Generator = "changed afterwards";

        //Assert
        session.Options.Should().NotBeSameAs(options);
        session.Options.Generator.Should().BeNull();
    }

    [Fact]
    public void options_start_where_the_plan_says_they_should()
    {
        //Arrange
        var options = new MusicGenerationOptions();

        //Assert
        options.Generator.Should().BeNull();
        options.InstrumentLibrary.Should().BeNull();
        options.Rendition.Should().BeNull();
        options.SampleRate.Should().Be(44100);
        options.Preroll.Should().Be(TimeSpan.FromSeconds(5.0));
        options.MasterVolume.Should().Be(1.0F);
        options.ApplicationOwnsAudioOutput.Should().BeFalse();
        options.Request.Should().NotBeNull();
    }

    [Fact]
    public void a_request_set_to_nothing_becomes_a_bare_request_rather_than_nothing()
    {
        //Arrange
        var options = new MusicGenerationOptions();

        //Act
        options.Request = null;

        //Assert
        options.Request.Should().NotBeNull();
        MusicGeneratorCapabilities.FeaturesUsedBy(options.Request)
            .Should().Be(MusicRequestFeatures.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void a_sample_rate_that_is_not_a_rate_is_refused(int sampleRate) =>
        ((Action)(() => new MusicGenerationOptions { SampleRate = sampleRate }))
        .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void a_negative_pre_roll_is_refused() =>
        ((Action)(() => new MusicGenerationOptions { Preroll = TimeSpan.FromSeconds(-1.0) }))
        .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void an_instrument_library_that_cannot_voice_one_part_at_a_time_is_refused()
    {
        //Arrange
        var options = SilentOptions();
        options.InstrumentLibrary = InlineOnlyLibrary.LibraryName;
        InlineOnlyLibrary.EnsureRegistered();
        using var session = Session(options);
        Action act = () => session.Play();

        //Act
        var thrown = act.Should().Throw<MusicGenerationException>().Which;

        //Assert
        thrown.Message.Should().Contain("does not make one instrument per part");
    }

    private static MusicGenerationOptions SilentOptions() =>
        new MusicGenerationOptions
        {
            ApplicationOwnsAudioOutput = true,
            InstrumentLibrary = TestInstrumentLibraries.CompleteName,
            Preroll = TimeSpan.FromSeconds(1.0)
        };

    private static MusicSession Session(MusicGenerationOptions options)
    {
        TestInstrumentLibraries.Complete();

        return new MusicSession(options, TestInstrumentLibraries.Lookup(), new ManualTimeProvider());
    }

    private static MusicSession Play(MusicGenerationOptions options)
    {
        var session = Session(options);
        session.Play();

        return session;
    }

    private sealed class InlineOnlyLibrary : IInstrumentLibrary
    {
        public const string LibraryName = "InlineOnly";

        private static readonly InlineOnlyLibrary Instance = new InlineOnlyLibrary();
        private static bool registered;

        public string Name => LibraryName;

        public string Description => "A library that only plays a whole file inline.";

        public InstrumentCoverage Coverage => InstrumentCoverage.General;

        public bool SupportsPerPart => false;

        public bool SupportsMultiTimbral => true;

        public static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            InstrumentLibraryRegistry.Register(Instance);
            registered = true;
        }

        public IMidiSynthesizer CreateSynthesizer(int program, int sampleRate) =>
            throw new NotSupportedException();

        public IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate) =>
            throw new NotSupportedException();

        public IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate) =>
            new TestSynthesizer((int)GeneralMidiProgram.Celesta, ModestSynthPresets.SubSineName,
                sampleRate);
    }
}
