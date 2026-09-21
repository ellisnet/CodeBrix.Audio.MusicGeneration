using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// Turns a <see cref="MusicRequest"/> into the model runner's own
/// <see cref="MidiGenerationOptions"/>, and refuses by name what this model cannot be asked for.
/// </summary>
/// <remarks>
/// <para>
/// A PIECE TO CONTINUE AND A DESCRIPTION OF A PIECE TO START ARE ALTERNATIVES, and that is the
/// model's own rule rather than one made here. So a request carrying a continuation or a primer
/// sends the MUSIC, and the instruments, tempo, metre and key are not sent again: they are already
/// in the music the prompt carries, which is the whole point of prompting with it.
/// </para>
/// <para>
/// THE SAMPLING DEFAULTS ARE THE MUSIC ONES - temperature 0.8, top-k 40, top-p 0.92 - which are
/// the request's own defaults and not the model runner's, whose defaults are tuned for text.
/// </para>
/// </remarks>
internal static class SkyTNTRequestMapper
{
    /// <summary>The fastest tempo this model's vocabulary can express.</summary>
    public const int MaximumBeatsPerMinute = 383;

    /// <summary>The slowest tempo this model's vocabulary can express.</summary>
    public const int MinimumBeatsPerMinute = 1;

    /// <summary>The percussion channel, as CodeBrix.Audio counts channels.</summary>
    public const int PercussionChannel = 10;

    private static readonly string[] MajorKeys =
    {
        "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#"
    };

    private static readonly string[] MinorKeys =
    {
        "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#", "G#", "D#", "A#"
    };

    /// <summary>Builds the model runner's options for one pass.</summary>
    /// <param name="request">The request, already checked against what the generator honours.</param>
    /// <param name="options">The generator's own load-time and default settings.</param>
    /// <param name="prompt">The music to continue, or null to start a piece from a description.</param>
    /// <param name="generatorName">The generator's name, for a refusal that names it.</param>
    /// <returns>The options to generate with.</returns>
    /// <exception cref="MusicRequestNotHonouredException">
    /// The request asks for something this model cannot do: a repetition penalty, a key it has no
    /// signature for, or a tempo or metre outside what its vocabulary can express.
    /// </exception>
    public static MidiGenerationOptions ToGenerationOptions(MusicRequest request,
        SkyTNTGeneratorOptions options, MidiScore prompt, string generatorName)
    {
        if (request.Controls.RepetitionPenalty != MusicGenerationControls.NoRepetitionPenalty)
        {
            throw Refuse(generatorName, MusicRequestFeatures.SamplingControls,
                "this model applies no repetition penalty at all - music repeats on purpose - so " +
                "a penalty would be quietly ignored. Leave Controls.RepetitionPenalty at " +
                "MusicGenerationControls.NoRepetitionPenalty.");
        }

        var generation = new MidiGenerationOptions
        {
            MaximumEvents = request.Controls.MaximumEvents ?? options.MaximumEventsPerPass,
            Temperature = request.Controls.Temperature,
            TopP = request.Controls.TopP,

            // TOP-K OF NOUGHT MEANS "NO TOP-K" IN A REQUEST AND IS NOT A NUMBER THIS MODEL TAKES;
            // the whole vocabulary is what it means, and that is what the vocabulary's size says.
            TopK = request.Controls.TopK < 1 ? int.MaxValue : request.Controls.TopK,
            AllowControlChange = options.AllowControlChange,

            // THE SETUP THE MODEL IS PRIMED WITH IS MUSIC AND HAS TO COME OUT; THE TAIL IT IS
            // CONTINUING IS NOT AND MUST NOT.
            //
            // The model reads both as "prompt rows". When a piece is DESCRIBED - instruments,
            // tempo, metre, key - those rows ARE the piece's instrument changes and its tempo, and
            // naming instruments also stops the model writing an instrument change of its own, so
            // suppressing them would leave every part playing on the default program at the
            // default speed. When a piece is CONTINUED, the rows are bars that have already been
            // played, and re-emitting them would make the segment start by repeating them.
            IncludePromptEvents = prompt == null
        };

        if (request.Seed.HasValue)
        {
            generation.Seed = request.Seed.Value;
        }

        if (prompt != null)
        {
            generation.Prompt = prompt;

            return generation;
        }

        Describe(request, options, generation, generatorName);

        return generation;
    }

    /// <summary>The General MIDI programs a request's instrument hints name, in order.</summary>
    /// <param name="hints">The hints, or null.</param>
    /// <returns>The programs, without the percussion channel's - a kit is asked for separately.</returns>
    public static List<int> InstrumentsOf(IList<GeneralMidiProgram> hints)
    {
        var instruments = new List<int>();

        if (hints == null)
        {
            return instruments;
        }

        for (var i = 0; i < hints.Count; i++)
        {
            var program = (int)hints[i];

            if (program >= 0 && program <= 127)
            {
                instruments.Add(program);
            }
        }

        return instruments;
    }

    /// <summary>The drum kit a pass writes percussion with, or null for none.</summary>
    /// <param name="request">The request.</param>
    /// <param name="options">The generator's own settings.</param>
    /// <returns>The program the percussion channel takes.</returns>
    /// <remarks>
    /// <para>
    /// A DRUM KIT IS NOT A GENERAL MIDI PROGRAM AND SO IT IS NOT AN INSTRUMENT HINT. General MIDI
    /// puts the kit on CHANNEL 10 and numbers kits in their own space - 0 standard, 8 room, 16
    /// power, 24 and 25 electronic, 32 jazz, 40 brushes, 48 orchestral - so a request names one
    /// through <see cref="MusicIntent.DrumKit"/> instead.
    /// </para>
    /// <para>
    /// THREE PLACES CAN SAY IT, and they are read in this order: WHAT THE REQUEST ASKS FOR wins,
    /// because a request that names a kit is a request that names a kit; then what the music has
    /// been doing, through the continuation's own program for channel 10; then the generator's own
    /// default. <see cref="MusicIntent.NoDrumKit"/> asks for no percussion and wins in the same
    /// way, so a preset can turn the generator's kit off for one piece.
    /// </para>
    /// </remarks>
    public static int? DrumKitOf(MusicRequest request, SkyTNTGeneratorOptions options)
    {
        var intent = request == null ? null : request.Intent;

        if (intent != null && intent.DrumKit.HasValue)
        {
            var asked = intent.DrumKit.Value;

            return asked == MusicIntent.NoDrumKit ? (int?)null : asked;
        }

        if (request != null && request.Continuation != null &&
            request.Continuation.ChannelPrograms.TryGetValue(PercussionChannel, out var carried))
        {
            return (int)carried;
        }

        return options.DrumKit;
    }

    /// <summary>The number of sharps or flats a key and mode name.</summary>
    /// <param name="key">The tonic, written as <see cref="MusicIntent.Key"/> writes it.</param>
    /// <param name="mode">The mode, or null.</param>
    /// <param name="isMinor">Whether the signature is a minor one.</param>
    /// <returns>The signature, -7 to 7, or null when the key names none this model can write.</returns>
    public static int? SignatureOf(string key, MusicMode? mode, out bool isMinor)
    {
        isMinor = mode.HasValue && IsMinorMode(mode.Value);

        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var tonic = Tonic(key);
        var names = isMinor ? MinorKeys : MajorKeys;

        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], tonic, StringComparison.OrdinalIgnoreCase))
            {
                return i - 7;
            }
        }

        return null;
    }

    private static void Describe(MusicRequest request, SkyTNTGeneratorOptions options,
        MidiGenerationOptions generation, string generatorName)
    {
        var instruments = InstrumentsOf(request.InstrumentHints);

        if (instruments.Count > 0)
        {
            generation.Instruments = instruments;
        }

        generation.DrumKit = DrumKitOf(request, options);

        var intent = request.Intent;

        if (intent == null)
        {
            return;
        }

        if (intent.BeatsPerMinute.HasValue)
        {
            var tempo = (int)Math.Round(intent.BeatsPerMinute.Value, MidpointRounding.AwayFromZero);

            if (tempo < MinimumBeatsPerMinute || tempo > MaximumBeatsPerMinute)
            {
                throw Refuse(generatorName, MusicRequestFeatures.Tempo,
                    $"this model writes a speed of {MinimumBeatsPerMinute} to " +
                    $"{MaximumBeatsPerMinute} quarter notes per minute, and {tempo} is outside it.");
            }

            generation.BeatsPerMinute = tempo;
        }

        if (intent.Meter.HasValue)
        {
            var meter = intent.Meter.Value;

            if (meter.BeatsPerBar < 1 || meter.BeatsPerBar > 16 || !IsWritableBeatNote(meter))
            {
                throw Refuse(generatorName, MusicRequestFeatures.Meter,
                    "this model writes a metre whose upper number is 1 to 16 and whose lower " +
                    $"number is 2, 4, 8 or 16, and {meter} is not one.");
            }

            generation.TimeSignatureNumerator = meter.BeatsPerBar;
            generation.TimeSignatureDenominator = meter.BeatNoteValue;
        }

        if (string.IsNullOrWhiteSpace(intent.Key) && !intent.Mode.HasValue)
        {
            return;
        }

        var signature = SignatureOf(intent.Key, intent.Mode, out var isMinor);

        if (!signature.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(intent.Key))
            {
                throw Refuse(generatorName, MusicRequestFeatures.Key,
                    $"this model writes a key signature of -7 to 7 sharps or flats and '{intent.Key}' " +
                    "names none of them. Write the tonic the way a key signature is named - C, G, " +
                    "F#, Bb - and say whether it is major or minor.");
            }

            // A MODE WITH NO TONIC NAMES NO SIGNATURE: "minor" on its own is not a key.
            return;
        }

        generation.KeySignatureSharpsOrFlats = signature.Value;
        generation.KeySignatureIsMinor = isMinor;
    }

    private static bool IsWritableBeatNote(MusicMeter meter) =>
        meter.BeatNoteValue == 2 || meter.BeatNoteValue == 4 || meter.BeatNoteValue == 8 ||
        meter.BeatNoteValue == 16;

    private static bool IsMinorMode(MusicMode mode) =>
        mode == MusicMode.Minor || mode == MusicMode.Aeolian || mode == MusicMode.Dorian ||
        mode == MusicMode.Phrygian || mode == MusicMode.Locrian;

    private static string Tonic(string key)
    {
        var trimmed = key.Trim();

        // "Bb major" and "F# minor" both name their tonic first; the mode travels separately.
        var space = trimmed.IndexOf(' ');

        return space < 0 ? trimmed : trimmed.Substring(0, space);
    }

    private static MusicRequestNotHonouredException Refuse(string generatorName,
        MusicRequestFeatures feature, string why) =>
        new MusicRequestNotHonouredException(
            $"The music generator '{generatorName}' does not honour this request: {why}",
            generatorName, feature);
}
