using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;
using CodeBrix.Audio.MusicGeneration.Streaming;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// THE MIXER AT A CROSSFADED SEAM, driven by hand exactly as the sequencer's message hook and render
/// call drive it: which set of instruments each message reaches, and what the two sets sound like
/// together, frame by frame.
/// </summary>
/// <remarks>
/// The levels are measurable because the constant-tone library's instruments hold ONE level while
/// they are routed, whatever they are sent - so every frame of a crossfade is the outgoing level
/// times one gain plus the incoming level times the other, and nothing else. The routing is
/// readable because the test library's instruments write down every message they are sent.
/// </remarks>
public class SeamCrossfadeMixerTests
{
    private const int SampleRate = 22050;
    private const int NoteOn = 0x90;
    private const int NoteOff = 0x80;
    private const int FadeFrames = 1000;

    [Fact]
    public void with_no_crossfade_it_renders_the_one_set_of_instruments_untouched()
    {
        //Arrange
        var first = ConstantToneVoicer();
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);

        //Act
        var mixed = Render(mixer, 256);
        var direct = Render(first, 256);

        //Assert
        mixed[0].Should().BeGreaterThan(0.01F);
        mixed.Should().Equal(direct);
        mixer.IsCrossfading.Should().BeFalse();
    }

    [Fact]
    public void the_cue_for_the_armed_plan_is_swallowed_and_starts_the_crossfade()
    {
        //Arrange
        var library = OneProgramLibrary();
        var first = Voicer(library);
        var second = Voicer(library);
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        mixer.Arm(Plan(7, second, MusicFadeCurve.EqualPower));

        //Act
        Cue(mixer, 7);

        //Assert
        mixer.CrossfadesStarted.Should().Be(1);
        mixer.IsCrossfading.Should().BeTrue();
        mixer.Current.Should().BeSameAs(second);
        library.Created.Should().HaveCount(1);
        library.Created[0].Messages.Should().NotContain(message => message.StartsWith("B0"));
    }

    [Fact]
    public void a_cue_for_some_other_plan_changes_nothing()
    {
        //Arrange
        var first = Voicer(OneProgramLibrary());
        var mixer = new SeamCrossfadeMixer(first);
        mixer.Arm(Plan(7, Voicer(OneProgramLibrary()), MusicFadeCurve.EqualPower));

        //Act
        Cue(mixer, 8);

        //Assert
        mixer.CrossfadesStarted.Should().Be(0);
        mixer.Current.Should().BeSameAs(first);
    }

    [Fact]
    public void a_disarmed_plan_never_starts()
    {
        //Arrange
        var first = Voicer(OneProgramLibrary());
        var mixer = new SeamCrossfadeMixer(first);
        var plan = Plan(3, Voicer(OneProgramLibrary()), MusicFadeCurve.EqualPower);
        mixer.Arm(plan);

        //Act
        mixer.Disarm(plan);
        Cue(mixer, 3);

        //Assert
        mixer.CrossfadesStarted.Should().Be(0);
        mixer.Current.Should().BeSameAs(first);
    }

    [Theory]
    [InlineData(MusicFadeCurve.EqualPower)]
    [InlineData(MusicFadeCurve.StraightLine)]
    [InlineData(MusicFadeCurve.EasedDecibels)]
    public void every_frame_of_the_crossfade_is_the_two_levels_mixed_along_the_curve(MusicFadeCurve curve)
    {
        //Arrange - both pieces on the same channel and key, through instruments of their own
        var first = ConstantToneVoicer();
        var second = ConstantToneVoicer();
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        var level = Render(mixer, 64)[0];

        mixer.Arm(Plan(1, second, curve));
        Cue(mixer, 1);
        Strike(mixer, second, 60);

        //Act
        var frames = Render(mixer, FadeFrames + 500);

        //Assert - the incoming level is the same as the outgoing one: same library, same part
        level.Should().BeGreaterThan(0.01F);

        for (var frame = 0; frame < FadeFrames; frame += 37)
        {
            var progress = (double)frame / FadeFrames;
            var expected = (level * MusicFade.GainAt(curve, progress)) +
                           (level * MusicFade.GainAt(curve, 1.0 - progress));

            frames[frame].Should().BeApproximately(expected, 0.00001F);
        }

        frames[FadeFrames + 100].Should().BeApproximately(level, 0.00001F);
        mixer.IsCrossfading.Should().BeFalse();
    }

    [Fact]
    public void an_equal_power_crossfade_of_two_equal_levels_peaks_at_the_square_root_of_two_half_way()
    {
        //Arrange
        var first = ConstantToneVoicer();
        var second = ConstantToneVoicer();
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        var level = Render(mixer, 64)[0];

        mixer.Arm(Plan(1, second, MusicFadeCurve.EqualPower));
        Cue(mixer, 1);
        Strike(mixer, second, 60);

        //Act
        var frames = Render(mixer, FadeFrames);

        //Assert
        frames[FadeFrames / 2].Should().BeApproximately(level * (float)Math.Sqrt(2.0), 0.0001F);
    }

    [Fact]
    public void after_the_cue_the_timeline_reaches_only_the_incoming_instruments()
    {
        //Arrange
        var library = OneProgramLibrary();
        var first = Voicer(library);
        var second = Voicer(library);
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        mixer.Arm(Plan(1, second, MusicFadeCurve.EqualPower));
        Cue(mixer, 1);

        //Act
        Strike(mixer, second, 64);
        mixer.ProcessMidiMessage(0, NoteOff, 64, 0);

        //Assert
        var outgoing = library.Created[0];
        var incoming = library.Created[1];
        outgoing.Messages.Should().Equal($"{NoteOn:X2} ch0 60 100");
        incoming.Messages.Should().Equal($"{NoteOn:X2} ch0 64 100", $"{NoteOff:X2} ch0 64 0");
    }

    [Fact]
    public void a_note_off_for_a_note_the_outgoing_piece_started_before_the_cue_goes_to_the_outgoing_instruments()
    {
        //Arrange - a note that rings across the cue
        var library = OneProgramLibrary();
        var first = Voicer(library);
        var second = Voicer(library);
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        mixer.Arm(Plan(1, second, MusicFadeCurve.EqualPower));
        Cue(mixer, 1);
        Strike(mixer, second, 64);

        //Act
        mixer.ProcessMidiMessage(0, NoteOff, 60, 0);

        //Assert
        library.Created[0].Messages.Should().Contain($"{NoteOff:X2} ch0 60 0");
        library.Created[1].Messages.Should().NotContain(message => message.StartsWith($"{NoteOff:X2}"));
    }

    [Fact]
    public void when_both_pieces_hold_the_same_key_the_older_note_takes_the_first_note_off()
    {
        //Arrange - THE SHARP EDGE: one key, one channel, sounding in both pieces at once
        var library = OneProgramLibrary();
        var first = Voicer(library);
        var second = Voicer(library);
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        mixer.Arm(Plan(1, second, MusicFadeCurve.EqualPower));
        Cue(mixer, 1);
        Strike(mixer, second, 60);

        //Act
        mixer.ProcessMidiMessage(0, NoteOff, 60, 0);
        mixer.ProcessMidiMessage(0, NoteOff, 60, 0);

        //Assert - one note-off each, the outgoing piece first
        library.Created[0].Messages.Count(message => message.StartsWith($"{NoteOff:X2}")).Should().Be(1);
        library.Created[1].Messages.Count(message => message.StartsWith($"{NoteOff:X2}")).Should().Be(1);
    }

    [Fact]
    public void a_note_off_that_arrives_after_the_outgoing_piece_was_let_go_is_swallowed()
    {
        //Arrange
        var library = OneProgramLibrary();
        var first = Voicer(library);
        var second = Voicer(library);
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        mixer.Arm(Plan(1, second, MusicFadeCurve.EqualPower));
        Cue(mixer, 1);
        Strike(mixer, second, 64);
        Render(mixer, FadeFrames + 64);

        //Act
        mixer.ProcessMidiMessage(0, NoteOff, 60, 0);

        //Assert
        mixer.IsCrossfading.Should().BeFalse();
        library.Created[0].Messages.Should().NotContain(message => message.StartsWith($"{NoteOff:X2}"));
        library.Created[1].Messages.Should().NotContain($"{NoteOff:X2} ch0 60 0");
    }

    [Fact]
    public void the_outgoing_pieces_last_moments_reach_its_own_instruments_on_their_own_frames()
    {
        //Arrange - a note 10 ms into the fade, released 20 ms in
        var library = OneProgramLibrary();
        var first = Voicer(library);
        var second = Voicer(library);
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);

        var note = new NoteOnEvent(0L, 1, 67, 90, 480);
        first.Observe(note);
        mixer.Arm(new SeamCrossfadePlan(1, 0L, 1L, second, FadeFrames, MusicFadeCurve.EqualPower,
            new[] { new TimedTailEvent(note, TimeSpan.FromMilliseconds(10.0), TimeSpan.FromMilliseconds(20.0)) }));
        Cue(mixer, 1);

        //Act - 10 ms is 220.5 frames at this rate, which rounds to 221
        Render(mixer, 220);
        var beforeItsFrame = library.Created[0].Messages.ToArray();
        Render(mixer, 400);

        //Assert
        beforeItsFrame.Should().NotContain($"{NoteOn:X2} ch0 67 90");
        library.Created[0].Messages.Should().Contain($"{NoteOn:X2} ch0 67 90");
        library.Created[0].Messages.Should().Contain(message => message.StartsWith($"{NoteOff:X2} ch0 67"));
        library.Created.Skip(1).SelectMany(synthesizer => synthesizer.Messages)
            .Should().NotContain(message => message.Contains(" 67 "));
    }

    [Fact]
    public void master_volume_scales_the_mixed_output()
    {
        //Arrange
        var first = ConstantToneVoicer();
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        var level = Render(mixer, 64)[0];

        //Act
        mixer.MasterVolume = 0.5F;
        var halved = Render(mixer, 64)[0];

        //Assert
        halved.Should().BeApproximately(level * 0.5F, 0.00001F);
    }

    [Fact]
    public void a_reset_ends_a_crossfade_in_progress()
    {
        //Arrange
        var first = ConstantToneVoicer();
        var second = ConstantToneVoicer();
        var mixer = new SeamCrossfadeMixer(first);
        mixer.Arm(Plan(1, second, MusicFadeCurve.EqualPower));
        Cue(mixer, 1);

        //Act
        mixer.Reset();

        //Assert
        mixer.IsCrossfading.Should().BeFalse();
        mixer.Current.Should().BeSameAs(second);
    }

    [Fact]
    public void the_cue_is_a_control_change_nothing_else_uses_and_is_recognised_by_tick_and_number()
    {
        //Arrange
        var cue = SeamCrossfadePlan.CueEvent(1234L, 42);

        //Act
        var control = (ControlChangeEvent)cue;

        //Assert
        control.Channel.Should().Be(16);
        ((int)control.Controller).Should().Be(119);
        control.ControllerValue.Should().Be(42);
        SeamCrossfadePlan.IsCue(cue, 1234L, 42).Should().BeTrue();
        SeamCrossfadePlan.IsCue(cue, 1234L, 43).Should().BeFalse();
        SeamCrossfadePlan.IsCue(cue, 1235L, 42).Should().BeFalse();
    }

    [Fact]
    public void a_plans_tail_is_in_time_order_with_every_note_off_in_it()
    {
        //Arrange - written out of order, with a note whose note-off lands after a later note
        var early = new NoteOnEvent(0L, 1, 60, 100, 960);
        var late = new NoteOnEvent(480L, 1, 62, 100, 120);
        var tail = new[]
        {
            new TimedTailEvent(late, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.625)),
            new TimedTailEvent(early, TimeSpan.Zero, TimeSpan.FromSeconds(1.0)),
            new TimedTailEvent(new TempoEvent(500000, 0L), TimeSpan.Zero, TimeSpan.Zero)
        };

        //Act
        var plan = new SeamCrossfadePlan(1, 0L, 1L, Voicer(OneProgramLibrary()), 10L,
            MusicFadeCurve.EqualPower, tail);

        //Assert - four channel messages, and the tempo change left out
        plan.TailCount.Should().Be(4);
        Enumerable.Range(0, plan.TailCount).Select(plan.TailFrame)
            .Should().Equal(0L, 11025L, 13781L, 22050L);
        plan.TailData1(0).Should().Be(60);
        plan.TailData1(3).Should().Be(60);
    }

    [Fact]
    public void WarmUp_runs_a_whole_practice_crossfade_without_touching_the_music()
    {
        //Arrange
        var first = ConstantToneVoicer();
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        var level = Render(mixer, 64)[0];

        //Act
        mixer.WarmUp(ConstantToneVoicer, MusicFadeCurve.EqualPower);

        //Assert - warmed, and the music it is playing is exactly as it was
        mixer.IsWarmedUp.Should().BeTrue();
        mixer.CrossfadesStarted.Should().Be(0);
        mixer.IsCrossfading.Should().BeFalse();
        mixer.Current.Should().BeSameAs(first);
        Render(mixer, 64)[0].Should().Be(level);
    }

    [Fact]
    public void after_a_warm_up_the_first_crossfade_allocates_nothing_of_its_own_on_the_rendering_thread()
    {
        //Arrange - both sets of instruments already sounding and rendered once, as the engine
        //leaves them: the incoming voicer is built and rendered on the pump thread ahead of time
        var first = ConstantToneVoicer();
        var second = ConstantToneVoicer();
        var mixer = new SeamCrossfadeMixer(first);
        Strike(mixer, first, 60);
        second.Observe(new NoteOnEvent(0L, 1, 60, 100, 480));
        second.ApplyPending(0, NoteOn, 60);
        Render(second, 64);
        mixer.WarmUp(ConstantToneVoicer, MusicFadeCurve.EqualPower);
        Render(mixer, 64);
        var plan = Plan(1, second, MusicFadeCurve.EqualPower);
        mixer.Arm(plan);
        var left = new float[64];
        var right = new float[64];
        var blocks = (FadeFrames / 64) + 2;

        // THE ROUTING SYNTHESIZER ITSELF allocates a little on every render (CodeBrix.Audio's own
        // behaviour, crossfade or not), so what is measured is that the mixer adds nothing to it:
        // at most two router renders per block, at what one router render costs.
        var perRouterRender = GC.GetAllocatedBytesForCurrentThread();
        first.Router.Render(left, right);
        perRouterRender = GC.GetAllocatedBytesForCurrentThread() - perRouterRender;

        //Act - what the audio thread does at the cue, and through the whole fade
        var before = GC.GetAllocatedBytesForCurrentThread();
        Cue(mixer, 1);

        for (var block = 0; block < blocks; block++)
        {
            mixer.Render(left, right);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        //Assert
        mixer.CrossfadesStarted.Should().Be(1);
        allocated.Should().BeLessThanOrEqualTo(perRouterRender * 2L * (blocks + 1));
    }

    // --- helpers ------------------------------------------------------------------------------

    private static TestInstrumentLibrary OneProgramLibrary() =>
        // One program and no pad to layer, so each voicer builds exactly one instrument per part.
        TestInstrumentLibrary.Covering("MixerParts", 0);

    private static RenditionVoicer Voicer(TestInstrumentLibrary library) =>
        new RenditionVoicer(MusicRenditionRegistry.Resolve(BuiltInRenditions.Automatic).Clone(),
            library, SampleRate, null, 1.0F);

    private static RenditionVoicer ConstantToneVoicer() =>
        new RenditionVoicer(MusicRenditionRegistry.Resolve(BuiltInRenditions.Automatic).Clone(),
            new ConstantToneInstrumentLibrary("MixerConstantTone"), SampleRate, null, 1.0F);

    private static SeamCrossfadePlan Plan(int cue, RenditionVoicer incoming, MusicFadeCurve curve) =>
        new SeamCrossfadePlan(cue, 0L, 1L, incoming, FadeFrames, curve, Array.Empty<TimedTailEvent>());

    private static void Cue(SeamCrossfadeMixer mixer, int cue) =>
        mixer.ProcessMidiMessage(SeamCrossfadePlan.CueChannel - 1, 0xB0, SeamCrossfadePlan.CueController,
            cue);

    private static void Strike(SeamCrossfadeMixer mixer, RenditionVoicer voicer, int key)
    {
        // What the engine does on the pump thread, then what the sequencer does on the audio thread.
        voicer.Observe(new NoteOnEvent(0L, 1, key, 100, 480));
        mixer.ProcessMidiMessage(0, NoteOn, key, 100);
    }

    private static float[] Render(SeamCrossfadeMixer mixer, int frames)
    {
        var left = new float[frames];
        var right = new float[frames];

        mixer.Render(left, right);

        return left;
    }

    private static float[] Render(RenditionVoicer voicer, int frames)
    {
        var left = new float[frames];
        var right = new float[frames];

        voicer.Router.Render(left, right);

        return left;
    }
}
