using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.MusicGeneration.Generation;

/// <summary>
/// What the music should be, said in musical terms rather than in a model's own notation. Every
/// part is optional, and a generator that cannot act on one says so by name rather than ignoring it.
/// </summary>
/// <remarks>
/// An intent is what a generator turns into whatever it actually reads - an ABC header for a text
/// model, an options object for an event model - so an application can ask for a waltz in A minor
/// without knowing which of those it is talking to.
/// </remarks>
public sealed class MusicIntent
{
    /// <summary>
    /// The value <see cref="DrumKit"/> takes to ask for NO PERCUSSION AT ALL, as against leaving
    /// the choice to the generator, which is what null means.
    /// </summary>
    public const int NoDrumKit = -1;

    /// <summary>The highest drum kit a request can name, as General MIDI numbers programs.</summary>
    public const int MaximumDrumKit = 127;

    private readonly List<string> characterWords = new List<string>();

    private int? drumKit;

    /// <summary>
    /// The tonic the music is in, written as a note letter with an optional accidental: "C", "F#",
    /// "Bb". Null or blank leaves the key to the generator.
    /// </summary>
    public string Key { get; set; }

    /// <summary>The mode that goes with <see cref="Key"/>, or null to leave it to the generator.</summary>
    public MusicMode? Mode { get; set; }

    /// <summary>The metre, or null to leave it to the generator.</summary>
    public MusicMeter? Meter { get; set; }

    /// <summary>
    /// The note value a bare note length means, or null to leave it to the generator. It matters to
    /// a notation-based generator - it is ABC's <c>L:</c> field - and means nothing to one that
    /// writes events directly.
    /// </summary>
    public MusicNoteLength? UnitNoteLength { get; set; }

    /// <summary>The tempo in quarter notes per minute, or null to leave it to the generator.</summary>
    public double? BeatsPerMinute { get; set; }

    /// <summary>
    /// Words describing the character wanted - "gentle", "driving", "sparse". They are hints, not
    /// instructions, and a generator that reads no prose refuses them by name.
    /// </summary>
    public IList<string> CharacterWords => characterWords;

    /// <summary>
    /// How many voices - separate musical parts - the music should have, or null to leave it to the
    /// generator.
    /// </summary>
    public int? VoiceCount { get; set; }

    /// <summary>
    /// The drum kit the percussion part is played with, as the program the percussion channel
    /// takes - 0 standard, 8 room, 16 power, 24 and 25 electronic, 32 jazz, 40 brushes, 48
    /// orchestral. <see cref="NoDrumKit"/> asks for no percussion at all; null leaves the choice
    /// to the generator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DRUM KIT IS NOT A GENERAL MIDI PROGRAM, which is why it is here rather than among
    /// <see cref="MusicRequest.InstrumentHints"/>: General MIDI puts the kit on channel 10 and
    /// numbers kits in a space of their own, so a list of programs has nowhere to say it.
    /// </para>
    /// <para>
    /// A generator that writes no percussion at all - anything built on notation, and every replay
    /// of a piece already written - refuses it by name rather than dropping it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is neither <see cref="NoDrumKit"/> nor a program from 0 to
    /// <see cref="MaximumDrumKit"/>.
    /// </exception>
    public int? DrumKit
    {
        get => drumKit;
        set
        {
            if (value.HasValue && value.Value != NoDrumKit &&
                (value.Value < 0 || value.Value > MaximumDrumKit))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"A drum kit is a program from 0 to {MaximumDrumKit}, MusicIntent.NoDrumKit " +
                    "for no percussion at all, or null to leave the choice to the generator.");
            }

            drumKit = value;
        }
    }

    /// <summary>
    /// How long the music should be, or null for as long as the generator decides. Neither of the
    /// model families plans a length, so reaching a target is a matter of generating past it and
    /// then ending the music deliberately - which is the session's job, not the generator's.
    /// </summary>
    public TimeSpan? TargetLength { get; set; }

    /// <summary>Copies the intent, so a caller can vary one from another without sharing lists.</summary>
    /// <returns>A copy that shares nothing with this one.</returns>
    public MusicIntent Clone()
    {
        var copy = new MusicIntent
        {
            Key = Key,
            Mode = Mode,
            Meter = Meter,
            UnitNoteLength = UnitNoteLength,
            BeatsPerMinute = BeatsPerMinute,
            VoiceCount = VoiceCount,
            TargetLength = TargetLength,
            drumKit = drumKit
        };

        copy.characterWords.AddRange(characterWords);

        return copy;
    }
}
