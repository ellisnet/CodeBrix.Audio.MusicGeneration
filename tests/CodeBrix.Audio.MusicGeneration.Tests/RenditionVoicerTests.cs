using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Rendition;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// What every part is REALLY played with: the named renditions against the instrument and the
/// gain they were rated at, and the automatic rule against what it promises - honour the music,
/// honour the request, and never leave a part on the piano at unity gain by accident.
/// </summary>
public class RenditionVoicerTests
{
    private const int Resolution = 480;
    private const int SampleRate = 44100;

    [Fact]
    public void AmbientDuet_voices_two_parts_as_a_celeste_over_a_mixed_chorus()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.AmbientDuet);

        //Act
        var parts = Play(voicer, 1, 2);

        //Assert
        parts.Should().HaveCount(2);
        Expect(parts[0], 1, (int)GeneralMidiProgram.Celesta, 0.85F, VoicingSource.Rendition);
        Expect(parts[1], 2, (int)GeneralMidiProgram.ChoirAahs, 0.55F, VoicingSource.Rendition);
    }

    [Fact]
    public void MelodyOverPad_voices_two_parts_as_keys_in_front_of_a_warm_pad()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.MelodyOverPad);

        //Act
        var parts = Play(voicer, 3, 4);

        //Assert
        Expect(parts[0], 3, (int)GeneralMidiProgram.ElectricPiano2, 0.85F, VoicingSource.Rendition);
        Expect(parts[1], 4, (int)GeneralMidiProgram.Pad2Warm, 0.45F, VoicingSource.Rendition);
    }

    [Fact]
    public void VibesAndStrings_voices_two_parts_as_a_vibraphone_over_sustained_strings()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.VibesAndStrings);

        //Act
        var parts = Play(voicer, 1, 2);

        //Assert
        Expect(parts[0], 1, (int)GeneralMidiProgram.Vibraphone, 0.85F, VoicingSource.Rendition);
        Expect(parts[1], 2, (int)GeneralMidiProgram.StringEnsemble1, 0.5F, VoicingSource.Rendition);
    }

    [Fact]
    public void HarpAndCello_voices_two_parts_at_the_same_level()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.HarpAndCello);

        //Act
        var parts = Play(voicer, 1, 2);

        //Assert
        Expect(parts[0], 1, (int)GeneralMidiProgram.OrchestralHarp, 0.8F, VoicingSource.Rendition);
        Expect(parts[1], 2, (int)GeneralMidiProgram.Cello, 0.8F, VoicingSource.Rendition);
    }

    [Fact]
    public void Neutral_voices_five_parts_with_five_distinct_instruments()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Neutral);

        //Act
        var parts = Play(voicer, 1, 2, 3, 4, 5);

        //Assert
        parts.Select(part => part.Program).Should().Equal(
            (int)GeneralMidiProgram.ElectricPiano2, (int)GeneralMidiProgram.StringEnsemble1,
            (int)GeneralMidiProgram.Vibraphone, (int)GeneralMidiProgram.AcousticGuitarNylon,
            (int)GeneralMidiProgram.ChoirAahs);
        parts.Select(part => part.Gain).Should().Equal(0.8F, 0.5F, 0.75F, 0.65F, 0.45F);
    }

    [Fact]
    public void voices_go_to_the_parts_in_the_order_the_parts_first_sound()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.AmbientDuet);

        //Act - the piece puts its first part on channel 9 and its second on channel 3
        var parts = Play(voicer, 9, 3);

        //Assert
        Expect(parts[0], 9, (int)GeneralMidiProgram.Celesta, 0.85F, VoicingSource.Rendition);
        Expect(parts[1], 3, (int)GeneralMidiProgram.ChoirAahs, 0.55F, VoicingSource.Rendition);
    }

    [Fact]
    public void a_part_beyond_the_renditions_own_voices_is_voiced_automatically()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.AmbientDuet);

        //Act
        var parts = Play(voicer, 1, 2, 3);

        //Assert
        parts.Should().HaveCount(3);
        parts[2].Source.Should().Be(VoicingSource.TasteTable);
        parts[2].Program.Should().Be((int)GeneralMidiProgram.Vibraphone);
    }

    [Fact]
    public void the_automatic_rendition_honours_a_program_change_the_music_carries()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic);

        //Act
        voicer.Observe(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.Flute));
        voicer.Observe(Note(0L, 1));
        var parts = voicer.Snapshot().Parts;

        //Assert
        Expect(parts[0], 1, (int)GeneralMidiProgram.Flute, 0.65F, VoicingSource.Music);
    }

    [Fact]
    public void an_explicit_acoustic_grand_piano_is_a_choice_and_is_honoured()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic);

        //Act
        voicer.Observe(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.AcousticGrandPiano));
        voicer.Observe(Note(0L, 1));
        var part = voicer.Snapshot().Parts[0];

        //Assert
        part.Program.Should().Be((int)GeneralMidiProgram.AcousticGrandPiano);
        part.Source.Should().Be(VoicingSource.Music);
        part.Gain.Should().BeLessThan(1.0F);
    }

    [Fact]
    public void the_automatic_rendition_honours_the_requests_instrument_hints()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic, GeneralMidiProgram.Marimba,
            GeneralMidiProgram.Contrabass);

        //Act
        var parts = Play(voicer, 1, 2);

        //Assert
        parts[0].Program.Should().Be((int)GeneralMidiProgram.Marimba);
        parts[0].Source.Should().Be(VoicingSource.InstrumentHint);
        parts[1].Program.Should().Be((int)GeneralMidiProgram.Contrabass);
        parts[1].Source.Should().Be(VoicingSource.InstrumentHint);
    }

    [Fact]
    public void the_music_is_honoured_before_the_requests_hints_are_reached()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic, GeneralMidiProgram.Marimba);

        //Act
        voicer.Observe(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.Flute));
        voicer.Observe(Note(0L, 1));
        voicer.Observe(Note(0L, 2));
        var parts = voicer.Snapshot().Parts;

        //Assert
        parts[0].Program.Should().Be((int)GeneralMidiProgram.Flute);
        parts[1].Program.Should().Be((int)GeneralMidiProgram.Marimba);
    }

    [Fact]
    public void every_gap_is_filled_with_a_distinct_voice_and_none_of_them_is_the_piano()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic);

        //Act
        var parts = Play(voicer, 1, 2, 3, 4, 5, 6, 7, 8);

        //Assert
        parts.Select(part => part.Program).Should().OnlyHaveUniqueItems();
        parts.Should().AllSatisfy(part =>
        {
            part.Program.Should().NotBe((int)GeneralMidiProgram.AcousticGrandPiano);
            part.Gain.Should().BeLessThan(1.0F);
            part.Note.Should().NotBeEmpty();
        });
    }

    [Fact]
    public void the_gaps_are_filled_in_the_order_the_taste_table_lists_them()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic);

        //Act
        var parts = Play(voicer, 1, 2, 3, 4);

        //Assert
        parts.Select(part => part.Program).Should().Equal(
            (int)GeneralMidiProgram.Celesta, (int)GeneralMidiProgram.ChoirAahs,
            (int)GeneralMidiProgram.Vibraphone, (int)GeneralMidiProgram.StringEnsemble1);
        parts[0].Note.Should().Contain("9.7");
    }

    [Fact]
    public void a_part_playing_on_its_own_is_layered_over_a_pad()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic);

        //Act
        var parts = Play(voicer, 1);

        //Assert
        parts[0].HasLayer.Should().BeTrue();
        parts[0].LayerProgram.Should().Be((int)GeneralMidiProgram.Pad2Warm);
        parts[0].LayerGain.Should().Be(0.5F);
    }

    [Fact]
    public void a_second_part_takes_the_layer_off_the_first_one_again()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic);

        //Act
        var parts = Play(voicer, 1, 2);
        var voicing = voicer.Snapshot();

        //Assert
        parts[0].HasLayer.Should().BeFalse();
        parts[1].HasLayer.Should().BeFalse();
        voicing.Diagnostics.Should().Contain(note => note.Contains("was taken off again"));
    }

    [Fact]
    public void a_named_rendition_never_layers_a_lone_part_of_its_own_accord()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.AmbientDuet);

        //Act
        var parts = Play(voicer, 1);

        //Assert
        parts[0].HasLayer.Should().BeFalse();
    }

    [Fact]
    public void the_percussion_channel_is_the_instrument_librarys_own_kit()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic);

        //Act
        voicer.Observe(Note(0L, GeneralMidi.PercussionChannel));
        var part = voicer.Snapshot().Parts[0];

        //Assert
        part.IsPercussion.Should().BeTrue();
        part.Channel.Should().Be(GeneralMidi.PercussionChannel);
        part.Gain.Should().Be(MusicRendition.DefaultPercussionGain);
        part.Source.Should().Be(VoicingSource.Percussion);
    }

    [Fact]
    public void the_percussion_part_does_not_use_up_one_of_the_renditions_voices()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.AmbientDuet);

        //Act
        voicer.Observe(Note(0L, GeneralMidi.PercussionChannel));
        voicer.Observe(Note(0L, 1));
        var parts = voicer.Snapshot().Parts;

        //Assert
        parts[1].Program.Should().Be((int)GeneralMidiProgram.Celesta);
    }

    [Fact]
    public void a_program_the_instrument_library_cannot_play_falls_to_one_it_can()
    {
        //Arrange
        var library = TestInstrumentLibraries.Covering((int)GeneralMidiProgram.Vibraphone,
            (int)GeneralMidiProgram.StringEnsemble1);
        var voicer = Voicer(BuiltInRenditions.AmbientDuet, library);

        //Act
        var parts = Play(voicer, 1);
        var voicing = voicer.Snapshot();

        //Assert
        parts[0].RequestedProgram.Should().Be((int)GeneralMidiProgram.Celesta);
        parts[0].Program.Should().Be((int)GeneralMidiProgram.Vibraphone);
        parts[0].WasReplacedForCoverage.Should().BeTrue();
        parts[0].Gain.Should().Be(0.85F);
        voicing.Diagnostics.Should().Contain(note =>
            note.Contains("does not cover") && note.Contains("Celesta") && note.Contains("Vibraphone"));
    }

    [Fact]
    public void the_taste_table_never_asks_for_something_the_library_does_not_cover()
    {
        //Arrange
        var library = TestInstrumentLibraries.Covering((int)GeneralMidiProgram.Marimba,
            (int)GeneralMidiProgram.Xylophone);
        var voicer = Voicer(BuiltInRenditions.Automatic, library);

        //Act
        var parts = Play(voicer, 1, 2);

        //Assert
        parts.Select(part => part.Program).Should().Equal((int)GeneralMidiProgram.Marimba,
            (int)GeneralMidiProgram.Xylophone);
        voicer.Snapshot().Diagnostics.Should().NotContain(note => note.Contains("does not cover"));
    }

    [Fact]
    public void a_library_with_no_kit_leaves_the_percussion_part_silent_and_says_so()
    {
        //Arrange
        var library = TestInstrumentLibraries.Covering((int)GeneralMidiProgram.Vibraphone);
        var voicer = Voicer(BuiltInRenditions.Automatic, library);

        //Act
        voicer.Observe(Note(0L, GeneralMidi.PercussionChannel));
        var voicing = voicer.Snapshot();

        //Assert
        voicing.Parts.Should().BeEmpty();
        voicing.Diagnostics.Should().Contain(note => note.Contains("covers no percussion notes"));
    }

    [Fact]
    public void a_consumers_own_rendition_is_used_exactly_as_it_was_written()
    {
        //Arrange
        var mine = new MusicRendition("MyGameVoicing", "The voicing my game ships with")
        {
            MasterGain = 0.7F
        };
        mine.Voices.Add(new RenditionVoice(GeneralMidiProgram.Kalimba, 0.6F,
            GeneralMidiProgram.Pad1NewAge, 0.3F));
        var voicer = new RenditionVoicer(mine, TestInstrumentLibraries.Complete(), SampleRate, null,
            1.0F);

        //Act
        var parts = Play(voicer, 4);

        //Assert
        Expect(parts[0], 4, (int)GeneralMidiProgram.Kalimba, 0.6F, VoicingSource.Rendition);
        parts[0].LayerProgram.Should().Be((int)GeneralMidiProgram.Pad1NewAge);
        parts[0].LayerGain.Should().Be(0.3F);
        voicer.Router.MasterVolume.Should().Be(0.7F);
    }

    [Fact]
    public void the_sessions_own_volume_multiplies_the_renditions_master_gain()
    {
        //Arrange
        var mine = new MusicRendition("HalfVolume", "Half as loud") { MasterGain = 0.5F };

        //Act
        var voicer = new RenditionVoicer(mine, TestInstrumentLibraries.Complete(), SampleRate, null,
            0.5F);

        //Assert
        voicer.Router.MasterVolume.Should().Be(0.25F);
    }

    [Fact]
    public void a_program_change_before_a_parts_first_note_is_not_a_re_voicing()
    {
        //Arrange
        var voicer = Voicer(BuiltInRenditions.Automatic);

        //Act
        voicer.Observe(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.Flute));
        voicer.Observe(new PatchChangeEvent(0L, 1, (int)GeneralMidiProgram.Violin));
        voicer.Observe(Note(0L, 1));
        var voicing = voicer.Snapshot();

        //Assert
        voicing.Parts.Should().HaveCount(1);
        voicing.Parts[0].Program.Should().Be((int)GeneralMidiProgram.Violin);
    }

    [Fact]
    public void a_renditions_own_voice_stays_as_voiced_when_the_music_changes_program()
    {
        //Arrange - the default: a named rendition's voicing is a decision, not a suggestion
        var mine = new MusicRendition("Pinned", "Voiced by me, whatever the music says");
        mine.Voices.Add(new RenditionVoice(GeneralMidiProgram.Celesta, 0.85F));
        var voicer = new RenditionVoicer(mine, TestInstrumentLibraries.Complete(), SampleRate, null,
            1.0F);

        //Act
        voicer.Observe(Note(0L, 1));
        voicer.Observe(new PatchChangeEvent(1920L, 1, (int)GeneralMidiProgram.Trumpet));
        var part = voicer.Snapshot().Parts[0];

        //Assert
        part.Program.Should().Be((int)GeneralMidiProgram.Celesta);
        part.Source.Should().Be(VoicingSource.Rendition);
    }

    [Fact]
    public void a_rendition_that_asks_to_follow_the_music_re_voices_its_own_voice()
    {
        //Arrange
        var mine = new MusicRendition("Follows", "Voiced by me until the music says otherwise")
        {
            FollowsProgramChanges = true
        };
        mine.Voices.Add(new RenditionVoice(GeneralMidiProgram.Celesta, 0.85F));
        var voicer = new RenditionVoicer(mine, TestInstrumentLibraries.Complete(), SampleRate, null,
            1.0F);

        //Act
        voicer.Observe(Note(0L, 1));
        voicer.Observe(new PatchChangeEvent(1920L, 1, (int)GeneralMidiProgram.Trumpet));
        var part = voicer.Snapshot().Parts[0];

        //Assert
        part.Program.Should().Be((int)GeneralMidiProgram.Trumpet);
    }

    [Fact]
    public void a_part_beyond_a_renditions_voices_follows_the_music_whatever_the_rendition_says()
    {
        //Arrange - one voice, two parts: the second is voiced by the automatic rule
        var mine = new MusicRendition("OneVoice", "One voice and then the automatic rule");
        mine.Voices.Add(new RenditionVoice(GeneralMidiProgram.Celesta, 0.85F));
        var voicer = new RenditionVoicer(mine, TestInstrumentLibraries.Complete(), SampleRate, null,
            1.0F);

        //Act
        voicer.Observe(Note(0L, 1));
        voicer.Observe(Note(0L, 2));
        voicer.Observe(new PatchChangeEvent(1920L, 1, (int)GeneralMidiProgram.Trumpet));
        voicer.Observe(new PatchChangeEvent(1920L, 2, (int)GeneralMidiProgram.Flute));
        var parts = voicer.Snapshot().Parts;

        //Assert - the rendition's own part stands; the automatic one follows the music
        parts[0].Program.Should().Be((int)GeneralMidiProgram.Celesta);
        parts[1].Source.Should().NotBe(VoicingSource.Rendition);
        parts[1].Program.Should().Be((int)GeneralMidiProgram.Flute);
    }

    private static PartVoicing[] Play(RenditionVoicer voicer, params int[] channels)
    {
        var tick = 0L;

        foreach (var channel in channels)
        {
            voicer.Observe(Note(tick, channel));
            tick += Resolution;
        }

        return voicer.Snapshot().Parts.ToArray();
    }

    private static NoteOnEvent Note(long tick, int channel) =>
        new NoteOnEvent(tick, channel, 60, 100, Resolution);

    private static RenditionVoicer Voicer(string renditionName,
        params GeneralMidiProgram[] instrumentHints) =>
        Voicer(renditionName, TestInstrumentLibraries.Complete(), instrumentHints);

    private static RenditionVoicer Voicer(string renditionName, IInstrumentLibrary library,
        params GeneralMidiProgram[] instrumentHints) =>
        new RenditionVoicer(MusicRenditionRegistry.Resolve(renditionName).Clone(), library,
            SampleRate, (IEnumerable<GeneralMidiProgram>)instrumentHints, 1.0F);

    private static void Expect(PartVoicing part, int channel, int program, float gain,
        VoicingSource source)
    {
        part.Channel.Should().Be(channel);
        part.Program.Should().Be(program);
        part.Gain.Should().Be(gain);
        part.Source.Should().Be(source);
    }
}
