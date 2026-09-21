using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE SECOND WAY TO USE THIS LIBRARY: the same generation and the same rendition, rendered to a
/// file instead of played - to a path or to a caller's stream, in any format a registered writer
/// covers, optionally to an exact length with a chosen ending.
/// </summary>
/// <remarks>
/// Nothing here opens an audio device and nothing here sleeps: a render is driven by the render
/// itself, as fast as the machine allows, and the pieces are small so that the whole class costs
/// a second or two.
/// </remarks>
[Collection("MusicGeneratorRegistry")]
public class MusicRenderTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;
    private const long TicksPerBar = 4L * Resolution;
    private const string Piece = "RenderPiece";

    private static readonly TimeSpan Bar = TimeSpan.FromSeconds(2.0);

    /// <summary>Starts every test from freshly built built-ins, because both tables are process-wide.</summary>
    public MusicRenderTests()
    {
        MusicGeneratorRegistry.ResetForTesting();
        MusicRenditionRegistry.ResetForTesting();
    }

    // --- THE EXACT LENGTH, AND THE THREE ENDINGS ----------------------------------------------

    [Fact]
    public async Task a_faded_render_is_exactly_the_target_length_to_the_frame()
    {
        //Arrange - five and a half seconds of a piece whose bars are two seconds long, so the
        //target is deliberately NOT a whole number of bars
        using var folder = new RenderOutputFolder("fade-length");
        using var session = Session(TwoBars());

        var path = folder.File("faded.wav");

        //Act
        var result = await session.RenderToFileAsync(path, new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(5.5),
            Ending = RenderEnding.Fade,
            FadeLength = TimeSpan.FromSeconds(2.0)
        }, TestContext.Current.CancellationToken);

        //Assert
        result.FrameCount.Should().Be((long)(5.5 * SampleRate));
        result.Duration.Should().Be(TimeSpan.FromSeconds(5.5));
        result.ReachedTargetLength.Should().BeTrue();
        result.FadeLength.Should().Be(TimeSpan.FromSeconds(2.0));
        FrameCountOf(path).Should().Be((long)(5.5 * SampleRate));
    }

    [Fact]
    public async Task a_hard_cut_render_is_exactly_the_target_length_to_the_frame()
    {
        //Arrange
        using var folder = new RenderOutputFolder("cut-length");
        using var session = Session(TwoBars());

        var path = folder.File("cut.wav");

        //Act
        var result = await session.RenderToFileAsync(path, new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(5.5),
            Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken);

        //Assert
        result.FrameCount.Should().Be((long)(5.5 * SampleRate));
        result.FadeLength.Should().Be(TimeSpan.Zero);
        FrameCountOf(path).Should().Be((long)(5.5 * SampleRate));
    }

    [Fact]
    public async Task a_natural_stop_ends_when_the_music_does_and_lets_it_ring_out()
    {
        //Arrange - real instruments, so that the last notes really do ring on after the music ends
        using var folder = new RenderOutputFolder("natural-stop");
        using var session = Session(TwoBars(), TestInstrumentLibraries.Complete().Name);

        var path = folder.File("natural.wav");

        //Act
        var result = await session.RenderToFileAsync(path, new MusicRenderOptions
        {
            Ending = RenderEnding.NaturalStop,
            RingOut = TimeSpan.FromSeconds(2.0)
        }, TestContext.Current.CancellationToken);

        //Assert - one pass is two bars, and the file is that plus whatever was still sounding
        result.Ending.Should().Be(RenderEnding.NaturalStop);
        result.SegmentCount.Should().Be(1);
        result.ReachedTargetLength.Should().BeTrue();
        result.Duration.Should().BeGreaterThan(Bar + Bar);
        result.Duration.Should().BeLessThanOrEqualTo(Bar + Bar + TimeSpan.FromSeconds(2.1));
    }

    [Fact]
    public async Task a_render_with_nothing_said_at_all_is_one_pass_of_the_generator()
    {
        //Arrange
        using var folder = new RenderOutputFolder("nothing-said");
        using var session = Session(TwoBars());

        //Act - no render options whatsoever
        var result = await session.RenderToFileAsync(folder.File("plain.wav"),
            TestContext.Current.CancellationToken);

        //Assert
        result.Ending.Should().Be(RenderEnding.NaturalStop);
        result.TargetLength.Should().BeNull();
        result.SegmentCount.Should().Be(1);
        result.Duration.Should().BeCloseTo(Bar + Bar, TimeSpan.FromMilliseconds(10.0));
    }

    [Theory]
    [InlineData(RenderEnding.Fade)]
    [InlineData(RenderEnding.HardCut)]
    public async Task an_exact_ending_without_a_target_length_is_refused(RenderEnding ending)
    {
        //Arrange
        using var folder = new RenderOutputFolder("no-target");
        using var session = Session(TwoBars());

        var path = folder.File("refused.wav");

        Func<Task> act = () => session.RenderToFileAsync(path,
            new MusicRenderOptions { Ending = ending }, TestContext.Current.CancellationToken);

        //Act
        var thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;

        //Assert
        thrown.Message.Should().Contain("target length");
        File.Exists(path).Should().BeFalse();
    }

    // --- CONTINUING TO REACH THE TARGET -------------------------------------------------------

    [Fact]
    public async Task a_generator_that_ends_early_is_continued_until_the_target_is_reached()
    {
        //Arrange - a pass is two bars, which is four seconds, and the file is to be nine
        using var folder = new RenderOutputFolder("continued");
        using var session = Session(TwoBars());

        //Act
        var result = await session.RenderToFileAsync(folder.File("continued.wav"),
            new MusicRenderOptions
            {
                TargetLength = TimeSpan.FromSeconds(9.0),
                Ending = RenderEnding.HardCut
            }, TestContext.Current.CancellationToken);

        //Assert - every seam is a bar line, and there are enough of them to have got there
        result.FrameCount.Should().Be(9L * SampleRate);
        result.SegmentCount.Should().BeGreaterThanOrEqualTo(3);
        result.SeamTicks.Should().HaveCount(result.SegmentCount);
        result.SeamTicks[0].Should().Be(0L);
        result.SeamTicks.Should().OnlyContain(tick => tick % TicksPerBar == 0L);
        result.SeamTicks.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task a_render_writes_the_music_tick_for_tick_and_never_holds_any_of_it_back()
    {
        //Arrange - what the generator itself produces, for one pass
        using var folder = new RenderOutputFolder("tick-for-tick");
        using var session = Session(TwoBars());

        var expected = await OnePassAsync(MusicGeneratorRegistry.Resolve(Piece));

        //Act - one pass, rendered
        var result = await session.RenderToFileAsync(folder.File("ticks.wav"),
            new MusicRenderOptions { Ending = RenderEnding.NaturalStop },
            TestContext.Current.CancellationToken);

        //Assert - a rest inserted anywhere would move every note after it
        NoteTicks(result.Music).Should().Equal(expected);
    }

    // --- THE FORMAT IS THE FILE NAME'S --------------------------------------------------------

    [Fact]
    public async Task a_wav_is_32_bit_float_unless_16_bit_pcm_is_asked_for()
    {
        //Arrange
        using var folder = new RenderOutputFolder("bit-depth");
        using var session = Session(TwoBars());

        var options = new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(1.0),
            Ending = RenderEnding.HardCut
        };

        var floatPath = folder.File("float.wav");
        var pcmPath = folder.File("pcm.wav");

        //Act
        await session.RenderToFileAsync(floatPath, options, TestContext.Current.CancellationToken);

        options.BitsPerSample = 16;

        await session.RenderToFileAsync(pcmPath, options, TestContext.Current.CancellationToken);

        //Assert
        using var asFloat = new WaveFileReader(floatPath);
        using var asPcm = new WaveFileReader(pcmPath);

        asFloat.WaveFormat.BitsPerSample.Should().Be(32);
        asFloat.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        asFloat.WaveFormat.SampleRate.Should().Be(SampleRate);
        asFloat.WaveFormat.Channels.Should().Be(2);
        asPcm.WaveFormat.BitsPerSample.Should().Be(16);
        asPcm.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.Pcm);
        asPcm.SampleCount.Should().Be(asFloat.SampleCount);
    }

    [Fact]
    public async Task an_aiff_is_written_by_its_extension_alone()
    {
        //Arrange
        using var folder = new RenderOutputFolder("aiff");
        using var session = Session(TwoBars());

        var path = folder.File("music.aiff");

        //Act
        var result = await session.RenderToFileAsync(path, new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(1.5),
            Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken);

        //Assert - AIFF is 16-bit PCM by default, and the file really is one
        using var reader = new AiffFileReader(path);

        result.Format.Should().Be(".aiff");
        result.FrameCount.Should().Be((long)(1.5 * SampleRate));
        reader.WaveFormat.BitsPerSample.Should().Be(16);
        reader.WaveFormat.SampleRate.Should().Be(SampleRate);
        reader.TotalTime.Should().BeCloseTo(TimeSpan.FromSeconds(1.5), TimeSpan.FromMilliseconds(1.0));
    }

    [Fact]
    public async Task a_caller_s_stream_gets_exactly_what_a_file_would_have_got()
    {
        //Arrange
        using var folder = new RenderOutputFolder("stream");
        using var session = Session(TwoBars());

        var options = new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(1.0),
            Ending = RenderEnding.Fade,
            FadeLength = TimeSpan.FromSeconds(0.5)
        };

        var path = folder.File("both.wav");

        //Act
        await session.RenderToFileAsync(path, options, TestContext.Current.CancellationToken);

        using var memory = new MemoryStream();

        var result = await session.RenderToStreamAsync(memory, ".wav", options,
            TestContext.Current.CancellationToken);

        //Assert
        result.Path.Should().BeNull();
        result.Format.Should().Be(".wav");
        memory.ToArray().Should().Equal(File.ReadAllBytes(path));
        memory.CanWrite.Should().BeTrue("the stream is the caller's and is never closed");
    }

    [Fact]
    public async Task a_wav_needs_a_stream_it_can_seek_back_through()
    {
        //Arrange
        using var session = Session(TwoBars());
        using var forwardOnly = new ForwardOnlyStream();

        Func<Task> act = () => session.RenderToStreamAsync(forwardOnly, "music.wav",
            new MusicRenderOptions { Ending = RenderEnding.NaturalStop },
            TestContext.Current.CancellationToken);

        //Act
        var thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;

        //Assert - CodeBrix.Audio's own words, and nothing was written
        thrown.Message.Should().Contain("seek");
        forwardOnly.BytesWritten.Should().Be(0L);
    }

    [Fact]
    public async Task an_unknown_extension_is_refused_by_name_before_anything_is_generated()
    {
        //Arrange
        using var folder = new RenderOutputFolder("unknown-format");

        var generator = new RecordingMusicGenerator("WatchfulGenerator",
            ReplayMusicGenerator.FromMidiEvents("WatchfulGenerator", TestMusic.Bars(2, Resolution)));

        MusicGeneratorRegistry.Register(generator);

        using var session = Session("WatchfulGenerator");

        var path = folder.File("music.notaformat");

        Func<Task> act = () => session.RenderToFileAsync(path,
            new MusicRenderOptions { Ending = RenderEnding.NaturalStop },
            TestContext.Current.CancellationToken);

        //Act
        var thrown = (await act.Should().ThrowAsync<NotSupportedException>()).Which;

        //Assert - it names the extension, lists what IS registered, and nothing was asked of the
        //generator and nothing was left on disk
        thrown.Message.Should().Contain(".notaformat");
        thrown.Message.Should().Contain("Registered formats");
        thrown.Message.Should().Contain(".wav");
        generator.PassCount.Should().Be(0);
        generator.ItemCount.Should().Be(0);
        generator.IsLoaded.Should().BeFalse();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task rendering_with_no_instrument_library_says_what_to_register()
    {
        //Arrange - the internal seam, because the real registry cannot be emptied
        using var folder = new RenderOutputFolder("no-library");

        var options = new MusicGenerationOptions { SampleRate = SampleRate };

        using var session = new MusicSession(options, TestInstrumentLibraries.EmptyLookup(),
            TimeProvider.System);

        var path = folder.File("silent.wav");

        Func<Task> act = () => session.RenderToFileAsync(path,
            TestContext.Current.CancellationToken);

        //Act
        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;

        //Assert - word for word, and it names the class the consumer already has
        thrown.Message.Should().Be(MusicSession.NoInstrumentLibraryMessage);
        thrown.Message.Should().Contain("GeneralMidiInstrumentLibrary.Register()");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task an_unknown_generator_is_refused_before_the_file_is_created()
    {
        //Arrange
        using var folder = new RenderOutputFolder("unknown-generator");
        using var session = Session("NoSuchGenerator");

        var path = folder.File("music.wav");

        Func<Task> act = () => session.RenderToFileAsync(path,
            TestContext.Current.CancellationToken);

        //Act
        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;

        //Assert
        thrown.Message.Should().Contain("NoSuchGenerator");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task an_unknown_rendition_is_refused_before_the_file_is_created()
    {
        //Arrange
        using var folder = new RenderOutputFolder("unknown-rendition");
        using var session = Session(TwoBars(), TestInstrumentLibraries.ConstantTone().Name,
            "NoSuchRendition");

        var path = folder.File("music.wav");

        Func<Task> act = () => session.RenderToFileAsync(path,
            TestContext.Current.CancellationToken);

        //Act
        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;

        //Assert
        thrown.Message.Should().Contain("NoSuchRendition");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task a_request_the_generator_will_not_honour_is_refused_before_anything_is_generated()
    {
        //Arrange - a replay honours a continuation and nothing else
        using var folder = new RenderOutputFolder("not-honoured");

        MusicGeneratorRegistry.Register(
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(2, Resolution)));

        var options = new MusicGenerationOptions
        {
            Generator = Piece,
            InstrumentLibrary = TestInstrumentLibraries.ConstantTone().Name,
            SampleRate = SampleRate,
            Request = new MusicRequest { Seed = 20260920 }
        };

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        var path = folder.File("refused.wav");

        Func<Task> act = () => session.RenderToFileAsync(path,
            TestContext.Current.CancellationToken);

        //Act
        var thrown = (await act.Should().ThrowAsync<MusicRequestNotHonouredException>()).Which;

        //Assert
        thrown.UnhonouredFeatures.Should().Be(MusicRequestFeatures.Seed);
        File.Exists(path).Should().BeFalse();
    }

    // --- PROGRESS, CANCELLATION AND WHAT COMES BACK -------------------------------------------

    [Fact]
    public async Task progress_never_goes_backwards_and_finishes()
    {
        //Arrange
        using var folder = new RenderOutputFolder("progress");
        using var session = Session(TwoBars());

        var reports = new List<MusicRenderProgress>();
        var progress = new CollectingProgress(reports);

        //Act
        var result = await session.RenderToFileAsync(folder.File("progress.wav"),
            new MusicRenderOptions
            {
                TargetLength = TimeSpan.FromSeconds(6.0),
                Ending = RenderEnding.Fade
            }, progress, TestContext.Current.CancellationToken);

        //Assert
        reports.Should().NotBeEmpty();
        reports.Select(report => report.Music).Should().BeInAscendingOrder();
        reports.Select(report => report.Fraction).Should().BeInAscendingOrder();
        reports.Should().Contain(report => report.Stage == MusicRenderStage.Rendering);
        reports[reports.Count - 1].Stage.Should().Be(MusicRenderStage.Finished);
        reports[reports.Count - 1].Fraction.Should().Be(1.0);
        reports[reports.Count - 1].Music.Should().Be(result.Duration);
        reports.Should().OnlyContain(report => report.TargetLength == TimeSpan.FromSeconds(6.0));
        reports.Should().OnlyContain(report => report.SegmentCount >= 1);
    }

    [Fact]
    public async Task cancelling_a_render_stops_it_and_leaves_no_file_behind()
    {
        //Arrange - a target long enough that it cannot have finished before the first report
        using var folder = new RenderOutputFolder("cancelled");
        using var session = Session(TwoBars());
        using var cancellation = new CancellationTokenSource();

        var path = folder.File("cancelled.wav");
        var progress = new CancellingProgress(cancellation);

        Func<Task> act = () => session.RenderToFileAsync(path, new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromMinutes(20.0),
            Ending = RenderEnding.HardCut
        }, progress, cancellation.Token);

        //Act
        await act.Should().ThrowAsync<OperationCanceledException>();

        //Assert - what was written is not finished off and the file this render created is gone
        File.Exists(path).Should().BeFalse();
        progress.Reported.Should().BeTrue();
    }

    [Fact]
    public async Task a_cancelled_render_to_a_stream_leaves_it_open_and_unfinished()
    {
        //Arrange
        using var session = Session(TwoBars());
        using var cancellation = new CancellationTokenSource();
        using var memory = new MemoryStream();

        var progress = new CancellingProgress(cancellation);

        Func<Task> act = () => session.RenderToStreamAsync(memory, ".wav", new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromMinutes(20.0),
            Ending = RenderEnding.HardCut
        }, progress, cancellation.Token);

        //Act
        await act.Should().ThrowAsync<OperationCanceledException>();

        //Assert - the header was never patched, so the bytes cannot be mistaken for a whole file
        memory.CanWrite.Should().BeTrue();
        DataChunkLength(memory.ToArray()).Should().Be(0);
    }

    [Fact]
    public async Task the_result_says_what_really_made_the_music()
    {
        //Arrange
        using var folder = new RenderOutputFolder("result");
        using var session = Session(TwoBars(), TestInstrumentLibraries.Complete().Name,
            BuiltInRenditions.AmbientDuet);

        //Act
        var result = await session.RenderToFileAsync(folder.File("result.wav"),
            new MusicRenderOptions
            {
                TargetLength = TimeSpan.FromSeconds(3.0),
                Ending = RenderEnding.HardCut
            }, TestContext.Current.CancellationToken);

        //Assert
        result.Source.GeneratorName.Should().Be(Piece);
        result.Source.GeneratorFamily.Should().Be(ReplayMusicGenerator.ReplayFamily);
        result.Source.IsReplay.Should().BeTrue();
        result.Source.InstrumentLibraryName.Should().Be(TestInstrumentLibraries.CompleteName);
        result.Source.RenditionName.Should().Be(BuiltInRenditions.AmbientDuet);
        result.Source.Voicing.Parts.Should().NotBeEmpty();
        result.SampleRate.Should().Be(SampleRate);
        result.Channels.Should().Be(2);
        result.Path.Should().Be(folder.File("result.wav"));
    }

    [Fact]
    public async Task the_music_that_was_rendered_can_be_written_out_as_a_midi_file()
    {
        //Arrange
        using var folder = new RenderOutputFolder("midi-too");
        using var session = Session(TwoBars());

        var result = await session.RenderToFileAsync(folder.File("both.wav"),
            new MusicRenderOptions { Ending = RenderEnding.NaturalStop },
            TestContext.Current.CancellationToken);

        var midiPath = folder.File("both.mid");

        //Act - the line an application writes beside the audio
        MidiFile.Export(midiPath, result.Music);

        //Assert - and it reads back as the music that was rendered
        var written = new MidiFile(midiPath);
        var notes = 0;

        for (var track = 0; track < written.Tracks; track++)
        {
            notes += written.Events[track].Count(MidiEvent.IsNoteOn);
        }

        notes.Should().Be(8, "two bars of four quarter notes");
    }

    // --- DETERMINISM, AND KEEPING OUT OF THE WAY ----------------------------------------------

    [Fact]
    public async Task two_renders_of_the_same_music_in_the_same_run_are_identical()
    {
        //Arrange - the same machine, the same run: never a digest pinned across machines
        using var folder = new RenderOutputFolder("identical");
        using var session = Session(TwoBars(), TestInstrumentLibraries.Complete().Name);

        var options = new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(3.0),
            Ending = RenderEnding.Fade,
            FadeLength = TimeSpan.FromSeconds(1.0)
        };

        var first = folder.File("first.wav");
        var second = folder.File("second.wav");

        //Act
        var one = await session.RenderToFileAsync(first, options, TestContext.Current.CancellationToken);
        var two = await session.RenderToFileAsync(second, options, TestContext.Current.CancellationToken);

        //Assert
        two.FrameCount.Should().Be(one.FrameCount);
        File.ReadAllBytes(second).Should().Equal(File.ReadAllBytes(first));
    }

    [Fact]
    public async Task a_render_does_not_disturb_music_that_is_already_playing()
    {
        //Arrange - a session playing into its own renderer, with no audio device anywhere
        using var folder = new RenderOutputFolder("while-playing");

        MusicGeneratorRegistry.Register(
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(8, Resolution)));

        var options = new MusicGenerationOptions
        {
            Generator = Piece,
            InstrumentLibrary = TestInstrumentLibraries.ConstantTone().Name,
            SampleRate = SampleRate,
            ApplicationOwnsAudioOutput = true,
            Preroll = TimeSpan.FromSeconds(0.25)
        };

        using var session = new MusicSession(options, TestInstrumentLibraries.Lookup(),
            TimeProvider.System);

        session.Play();
        session.Pump();

        var playingStream = session.Stream;
        var before = session.ActiveSource.GeneratorName;

        //Act - render a different length of the same music while it plays
        var result = await session.RenderToFileAsync(folder.File("aside.wav"),
            new MusicRenderOptions
            {
                TargetLength = TimeSpan.FromSeconds(2.0),
                Ending = RenderEnding.HardCut
            }, TestContext.Current.CancellationToken);

        session.Pump();

        //Assert - the session is untouched: same timeline, still playing, nothing failed
        result.FrameCount.Should().Be(2L * SampleRate);
        session.IsPlaying.Should().BeTrue();
        session.Stream.Should().BeSameAs(playingStream);
        session.ActiveSource.GeneratorName.Should().Be(before);
        session.GenerationError.Should().BeNull();
        session.Stream.LateEventCount.Should().Be(0);
    }

    [Fact]
    public async Task a_render_makes_real_sound_through_the_General_MIDI_library()
    {
        //Arrange - the library a consumer of this package already has
        using var folder = new RenderOutputFolder("general-midi");
        using var session = Session(TwoBars(), TestInstrumentLibraries.GeneralMidi().Name);

        var path = folder.File("gm.wav");

        //Act
        var result = await session.RenderToFileAsync(path, new MusicRenderOptions
        {
            TargetLength = TimeSpan.FromSeconds(2.0),
            Ending = RenderEnding.HardCut
        }, TestContext.Current.CancellationToken);

        //Assert
        result.Source.InstrumentLibraryName.Should().Be(TestInstrumentLibraries.GeneralMidiName);
        PeakOf(path).Should().BeGreaterThan(0.001F);
    }

    // --- HELPERS ------------------------------------------------------------------------------

    private static string TwoBars()
    {
        MusicGeneratorRegistry.Register(
            ReplayMusicGenerator.FromMidiEvents(Piece, TestMusic.Bars(2, Resolution)));

        return Piece;
    }

    private static MusicSession Session(string generatorName) =>
        Session(generatorName, TestInstrumentLibraries.ConstantTone().Name);

    private static MusicSession Session(string generatorName, string libraryName) =>
        Session(generatorName, libraryName, null);

    private static MusicSession Session(string generatorName, string libraryName,
        string renditionName)
    {
        var options = new MusicGenerationOptions
        {
            Generator = generatorName,
            InstrumentLibrary = libraryName,
            Rendition = renditionName,
            SampleRate = SampleRate
        };

        return new MusicSession(options, TestInstrumentLibraries.Lookup(), TimeProvider.System);
    }

    private static async Task<IReadOnlyList<long>> OnePassAsync(IMusicGenerator generator)
    {
        var ticks = new List<long>();
        var request = new MusicRequest { PaceInRealTime = false, TicksPerQuarterNote = Resolution };

        await foreach (var item in generator.GenerateAsync(request,
            TestContext.Current.CancellationToken))
        {
            if (item.HasEvent && MidiEvent.IsNoteOn(item.Event))
            {
                ticks.Add(item.Event.AbsoluteTime);
            }
        }

        ticks.Sort();

        return ticks;
    }

    private static IReadOnlyList<long> NoteTicks(MidiEventCollection music)
    {
        var ticks = new List<long>();

        for (var track = 0; track < music.Tracks; track++)
        {
            foreach (var midiEvent in music.GetTrackEvents(track))
            {
                if (midiEvent != null && MidiEvent.IsNoteOn(midiEvent))
                {
                    ticks.Add(midiEvent.AbsoluteTime);
                }
            }
        }

        ticks.Sort();

        return ticks;
    }

    private static long FrameCountOf(string path)
    {
        using var reader = new WaveFileReader(path);

        return reader.SampleCount;
    }

    private static float PeakOf(string path)
    {
        using var reader = new WaveFileReader(path);

        var peak = 0.0F;
        var frame = reader.ReadNextSampleFrame();

        while (frame != null)
        {
            foreach (var sample in frame)
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }

            frame = reader.ReadNextSampleFrame();
        }

        return peak;
    }

    // The 'data' chunk's length field of a WAV file, which is only written when the file is
    // finished off - so a render that did not finish leaves it at zero.
    private static int DataChunkLength(byte[] wav)
    {
        for (var index = 12; index + 8 <= wav.Length; )
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, index, 4);
            var size = BitConverter.ToInt32(wav, index + 4);

            if (id == "data")
            {
                return size;
            }

            index += 8 + size + (size & 1);

            if (size <= 0)
            {
                break;
            }
        }

        return 0;
    }

    /// <summary>Keeps every report a render made.</summary>
    private sealed class CollectingProgress : IProgress<MusicRenderProgress>
    {
        private readonly List<MusicRenderProgress> reports;

        public CollectingProgress(List<MusicRenderProgress> reports)
        {
            this.reports = reports;
        }

        public void Report(MusicRenderProgress value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
    }

    /// <summary>Stops the render the moment it says it has started one.</summary>
    private sealed class CancellingProgress : IProgress<MusicRenderProgress>
    {
        private readonly CancellationTokenSource cancellation;

        public CancellingProgress(CancellationTokenSource cancellation)
        {
            this.cancellation = cancellation;
        }

        public bool Reported { get; private set; }

        public void Report(MusicRenderProgress value)
        {
            Reported = true;
            cancellation.Cancel();
        }
    }
}
