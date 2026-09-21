using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.MusicGeneration.Streaming;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE TWO SWITCHES A RENDER SETS, and what each of them turns off. A rendered file must be tick
/// for tick what the generator wrote, however slow the machine is: no rests inserted to keep music
/// ahead of a play head that is not there, and no falling back to a whole segment at a time.
/// </summary>
/// <remarks>
/// Everything here runs on a clock the test moves by hand - the engine's pump AND the generator's
/// pacing - so a deliberately slow generator costs no real time at all.
/// </remarks>
public class MusicEngineOfflineTests
{
    private const int Resolution = 480;
    private const int SampleRate = 22050;
    private const long TicksPerBar = 4L * Resolution;

    [Fact]
    public async Task a_render_never_falls_back_to_a_segment_at_a_time_however_slow_the_generator_is()
    {
        //Arrange - a generator at a quarter of real time, which is well under the mark the
        //fallback engages at
        using var rig = Build(fallsBack: false);

        //Act - run until the engine has measured a rate it would have fallen back on
        await rig.RunUntil(() => rig.Engine.RealTimeFactor > 0.0 && rig.Engine.RealTimeFactor < 0.8);

        //Assert - it measured the slow rate, and it carried on streaming anyway
        rig.Engine.RealTimeFactor.Should().BeLessThan(0.8);
        rig.Engine.Mode.Should().Be(MusicDeliveryMode.Streaming);
        rig.Engine.HoldCount.Should().Be(0);
        rig.Engine.CommittedThroughTick.Should().BeGreaterThan(0L);
    }

    [Fact]
    public async Task the_fallback_is_still_there_when_it_is_left_on()
    {
        //Arrange - the same rig, with the switch at its default
        using var rig = Build(fallsBack: true);

        //Act
        await rig.RunUntil(() => rig.Engine.Mode == MusicDeliveryMode.SegmentAtATime);

        //Assert - which is what makes the other test's result the switch's doing and not luck
        rig.Engine.Mode.Should().Be(MusicDeliveryMode.SegmentAtATime);
        rig.Engine.RealTimeFactor.Should().BeLessThan(0.8);
    }

    [Fact]
    public async Task a_slow_render_writes_the_music_at_the_ticks_it_was_written_at()
    {
        //Arrange
        using var rig = Build(fallsBack: false);

        //Act - four bars of a one-bar piece, played over and over
        await rig.RunUntil(() => CommittedNoteTicks(rig.Stream).Count >= 16);

        //Assert - a rest anywhere would have moved every note after it
        CommittedNoteTicks(rig.Stream).Take(16)
            .Should().Equal(Enumerable.Range(0, 16).Select(note => note * (long)Resolution));
        rig.Engine.SegmentCount.Should().BeGreaterThanOrEqualTo(4);
        rig.Engine.HoldCount.Should().Be(0);
        rig.Engine.HeldBarCount.Should().Be(0);
        rig.Stream.LateEventCount.Should().Be(0);
    }

    [Fact]
    public async Task the_seams_of_a_render_are_reported_and_every_one_of_them_is_a_bar_line()
    {
        //Arrange
        using var rig = Build(fallsBack: false);

        //Act
        await rig.RunUntil(() => rig.Engine.SegmentCount >= 3);

        //Assert
        var seams = rig.Engine.SegmentStartTicks;

        seams.Should().HaveCount(rig.Engine.SegmentCount);
        seams[0].Should().Be(0L);

        for (var index = 0; index < seams.Count; index++)
        {
            (seams[index] % TicksPerBar).Should().Be(0L);
        }
    }

    private static List<long> CommittedNoteTicks(MidiStream stream)
    {
        var ticks = new List<long>();
        var recording = stream.ToMidiEventCollection();

        for (var track = 0; track < recording.Tracks; track++)
        {
            foreach (var midiEvent in recording.GetTrackEvents(track))
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

    private static OfflineRig Build(bool fallsBack)
    {
        var time = new ManualTimeProvider();
        var generator = ReplayOnTestClock.On(
            ReplayMusicGenerator.FromMidiEvents("OfflineSwitches", TestMusic.Bars(1, Resolution)),
            time, pacingRate: 0.25);

        var stream = new MidiStream(Resolution);
        var host = new FakeMusicHost(SampleRate);
        var voicer = new RenditionVoicer(new MusicRendition("OfflineRig", "A rig."),
            new ConstantToneInstrumentLibrary("OfflineRigParts"), SampleRate, null, 1.0F);

        host.Load(stream, _ => voicer.Router, (synthesizer, channel, command, data1, data2) =>
            synthesizer.ProcessMidiMessage(channel, command, data1, data2));

        var engine = new MusicEngine(generator, new MusicRequest { TicksPerQuarterNote = Resolution },
            stream, voicer, host, TimeSpan.FromSeconds(1.0), time, 0L)
        {
            EndOfPiece = EndOfPiecePolicy.KeepGenerating,
            GenerateAhead = TimeSpan.FromSeconds(4.0),

            // THE TWO SWITCHES A RENDER SETS, together, the way the renderer sets them.
            KeepsMusicAheadOfTheHead = false,
            FallsBackToSegmentAtATime = fallsBack
        };

        // A RENDERER TAKES WHAT IT IS GIVEN AS SOON AS IT IS GIVEN IT, so its head sits right up
        // against the music - which is exactly the condition the streaming rules would react to.
        host.EndOfTheMusic = () => engine.WrittenThroughTime;
        host.FollowTheMusic();

        engine.Start();

        return new OfflineRig(time, stream, engine);
    }

    private sealed class OfflineRig : IDisposable
    {
        public OfflineRig(ManualTimeProvider time, MidiStream stream, MusicEngine engine)
        {
            Time = time;
            Stream = stream;
            Engine = engine;
        }

        public ManualTimeProvider Time { get; }

        public MidiStream Stream { get; }

        public MusicEngine Engine { get; }

        public Task RunUntil(Func<bool> until) => EnginePump.RunUntilAsync(Time, until);

        public void Dispose() => Engine.Dispose();
    }
}
