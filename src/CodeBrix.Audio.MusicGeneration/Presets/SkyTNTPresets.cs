using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Models;

namespace CodeBrix.Audio.MusicGeneration.Presets;

/// <summary>
/// Three electronica starting points for the SkyTNT model, accepted after listening to two seeds
/// of each preset on 2026-09-21.
/// </summary>
/// <remarks>
/// <para>
/// The listening sessions accepted all three presets. They produce steady, often repetitive
/// arrangements; acceptance does not promise the musical development of a finished composition.
/// The default remains 1,000 events per pass because shorter passes varied in quality by seed.
/// </para>
/// <para>
/// WHY THESE INSTRUMENTS. Synthesized bass, sawtooth leads and warm pads are exactly the General
/// MIDI families a synthesized bank is best at - there is no recorded instrument to fall short of
/// - and the listening sessions used these families in the synthesized instrument library.
/// </para>
/// <para>
/// A DRUM KIT IS ASKED FOR PER REQUEST, through <see cref="MusicIntent.DrumKit"/>, and the ambient
/// preset asks for <see cref="MusicIntent.NoDrumKit"/> - no percussion at all - rather than
/// leaving it to the generator's own default.
/// </para>
/// </remarks>
public static class SkyTNTPresets
{
    /// <summary>The drum kit these presets ask for: General MIDI's electronic kit.</summary>
    public const int ElectronicDrumKit = 24;

    private static readonly MusicPreset[] AllPresets;

    static SkyTNTPresets()
    {
        FourOnTheFloor = Preset("FourOnTheFloor",
            "A kick on every beat under a synthesized bass, in four at 126.",
            request =>
            {
                request.InstrumentHints.Add(GeneralMidiProgram.SynthBass1);
                request.Intent = Intent(126.0, ElectronicDrumKit);
            });

        ClubArrangement = Preset("ClubArrangement",
            "Drums, a synthesized bass, a sawtooth lead and a warm pad, in four at 126.",
            request =>
            {
                request.InstrumentHints.Add(GeneralMidiProgram.SynthBass1);
                request.InstrumentHints.Add(GeneralMidiProgram.Lead2Sawtooth);
                request.InstrumentHints.Add(GeneralMidiProgram.Pad2Warm);
                request.Intent = Intent(126.0, ElectronicDrumKit);
            });

        AmbientElectronica = Preset("AmbientElectronica",
            "Two pads and a bell-like lead, no percussion at all, in four at 84.",
            request =>
            {
                request.InstrumentHints.Add(GeneralMidiProgram.Pad1NewAge);
                request.InstrumentHints.Add(GeneralMidiProgram.Pad2Warm);
                request.InstrumentHints.Add(GeneralMidiProgram.Celesta);
                request.Intent = Intent(84.0, MusicIntent.NoDrumKit);
            });

        AllPresets = new[] { FourOnTheFloor, ClubArrangement, AmbientElectronica };
    }

    /// <summary>A kick on every beat under a synthesized bass.</summary>
    public static MusicPreset FourOnTheFloor { get; }

    /// <summary>Drums, bass, a sawtooth lead and a warm pad.</summary>
    public static MusicPreset ClubArrangement { get; }

    /// <summary>Pads and a bell-like lead, with no percussion.</summary>
    public static MusicPreset AmbientElectronica { get; }

    /// <summary>Every accepted SkyTNT preset.</summary>
    public static IReadOnlyList<MusicPreset> All => AllPresets;

    /// <summary>Finds a preset by name.</summary>
    /// <param name="name">The name, matched without regard to case.</param>
    /// <returns>The preset, or null when no preset has that name.</returns>
    public static MusicPreset Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        for (var index = 0; index < AllPresets.Length; index++)
        {
            if (string.Equals(AllPresets[index].Name, name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return AllPresets[index];
            }
        }

        return null;
    }

    private static MusicIntent Intent(double beatsPerMinute, int drumKit) =>
        new MusicIntent
        {
            Meter = MusicMeter.CommonTime,
            BeatsPerMinute = beatsPerMinute,
            DrumKit = drumKit
        };

    private static MusicPreset Preset(string name, string description, Action<MusicRequest> fill) =>
        new MusicPreset(name, SkyTNTMusicGenerator.SkyTNTFamily, description, null, false,
            () =>
            {
                var request = new MusicRequest();

                fill(request);

                return request;
            });
}
