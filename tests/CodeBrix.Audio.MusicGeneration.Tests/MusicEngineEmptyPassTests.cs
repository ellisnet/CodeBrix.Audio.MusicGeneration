using System;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// AN EMPTY PASS NEVER ENDS MUSIC THAT IS MEANT TO KEEP GOING: a pass that writes nothing is
/// asked for again as a fresh piece, and only a run of them stops the music - with an error.
/// </summary>
public class MusicEngineEmptyPassTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;
    private const long PieceTicks = 2L * 4L * Resolution;

    [Fact]
    public async Task an_empty_primed_continuation_is_asked_for_again_fresh_and_the_music_carries_on()
    {
        //Arrange - primed continuations come back empty; fresh requests do not
        var generator = Generator(emptyWhenPrimed: true, alwaysEmpty: false, out var time);
        using var engine = Engine(generator, time, SegmentPriming.Primed);
        engine.Start();

        //Act
        await EnginePump.RunUntilAsync(time, () => engine.Stream.HorizonTicks >= 2L * PieceTicks);

        //Assert - the retry is fresh, joins at the bar line the music reached, and is counted
        var requests = generator.Requests;
        requests[1].Continuation.Should().NotBeNull();
        requests[2].Continuation.Should().BeNull();
        requests[2].Seed.Should().NotBe(requests[1].Seed);
        engine.EmptyPassCount.Should().BeGreaterThanOrEqualTo(1);
        engine.SegmentStartTicks.Should().Contain(PieceTicks);
        engine.Stream.IsCompleted.Should().BeFalse();
        engine.GenerationError.Should().BeNull();
    }

    [Fact]
    public async Task a_generator_that_writes_nothing_stops_after_three_tries_and_says_why()
    {
        //Arrange
        var generator = Generator(emptyWhenPrimed: false, alwaysEmpty: true, out var time);
        using var engine = Engine(generator, time, SegmentPriming.Primed);
        engine.Start();

        //Act
        await EnginePump.RunUntilAsync(time, () => engine.Stream.IsCompleted);

        //Assert
        generator.Requests.Should().HaveCount(StreamingDefaults.MaximumConsecutiveEmptyPasses);
        engine.EmptyPassCount.Should().Be(StreamingDefaults.MaximumConsecutiveEmptyPasses);
        engine.GenerationError.Should().BeOfType<MusicGenerationException>()
            .Which.Message.Should().Contain("produced no music 3 times");
    }

    [Fact]
    public async Task under_alternate_priming_with_a_crossfade_an_empty_pass_is_retried_fresh_with_a_hard_join()
    {
        //Arrange
        var generator = Generator(emptyWhenPrimed: true, alwaysEmpty: false, out var time);
        using var engine = Engine(generator, time, SegmentPriming.Alternate,
            TimeSpan.FromSeconds(1.0));
        engine.Start();

        //Act
        await EnginePump.RunUntilAsync(time, () => engine.Stream.HorizonTicks >= 2L * PieceTicks);

        //Assert - segment two (primed) came back empty and was retried fresh at the bar line
        generator.Requests[1].Continuation.Should().NotBeNull();
        generator.Requests[2].Continuation.Should().BeNull();
        engine.SegmentStartTicks.Take(3).Should().Equal(0L, PieceTicks, PieceTicks);
        engine.EmptyPassCount.Should().Be(1);
        engine.GenerationError.Should().BeNull();
    }

    private static EmptyPassMusicGenerator Generator(bool emptyWhenPrimed, bool alwaysEmpty,
        out ManualTimeProvider time)
    {
        time = new ManualTimeProvider();

        return new EmptyPassMusicGenerator("EmptyPasses",
            ReplayOnTestClock.On(ReplayMusicGenerator.FromMidiEvents("EmptyPasses",
                TestMusic.Bars(2, Resolution)), time, 8.0), emptyWhenPrimed, alwaysEmpty);
    }

    private static MusicEngine Engine(IMusicGenerator generator, ManualTimeProvider time,
        SegmentPriming priming, TimeSpan? crossfade = null)
    {
        var stream = new MidiStream(Resolution);
        var host = new FakeMusicHost(SampleRate);
        var library = TestInstrumentLibrary.Complete("EmptyPassParts");
        var rendition = MusicRenditionRegistry.Resolve(BuiltInRenditions.Automatic).Clone();
        var voicer = new RenditionVoicer(rendition, library, SampleRate, null, 1.0F);
        var mixer = new SeamCrossfadeMixer(voicer);

        host.Load(stream, _ => mixer, (synthesizer, channel, command, data1, data2) =>
            synthesizer.ProcessMidiMessage(channel, command, data1, data2));

        var engine = new MusicEngine(generator,
            new MusicRequest { TicksPerQuarterNote = Resolution, Seed = 7 }, stream, voicer, host,
            TimeSpan.FromSeconds(1.0), time, 0L)
        {
            EndOfPiece = EndOfPiecePolicy.KeepGenerating,
            GenerateAhead = TimeSpan.FromSeconds(12.0),
            SegmentPriming = priming,
            SeamCrossfade = crossfade ?? TimeSpan.Zero,
            SeamCrossfadeCurve = MusicFadeCurve.EqualPower,
            Mixer = mixer,
            CreateVoicer = () => new RenditionVoicer(rendition, library, SampleRate, null, 1.0F)
        };

        host.EndOfTheMusic = () => engine.WrittenThroughTime;
        host.FollowTheMusic();

        return engine;
    }
}
