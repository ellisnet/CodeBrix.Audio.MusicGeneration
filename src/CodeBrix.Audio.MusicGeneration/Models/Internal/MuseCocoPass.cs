using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;
using ModelMidiEvent = CodeBrix.Ollama.ModelRunner.MidiEvent;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>Converts MuseCoco's ordered stream and exclusive horizon to Audio's settled timeline.</summary>
internal sealed class MuseCocoPass
{
    private readonly int ticksPerQuarterNote;
    private readonly List<MeterChange> meters = new List<MeterChange>();
    private long lastTick = -1;
    private long lastHorizon;
    private long endTick;

    public MuseCocoPass(int ticksPerQuarterNote)
    {
        this.ticksPerQuarterNote = ticksPerQuarterNote;
    }

    public GeneratedMusicEvent Accept(ModelMidiEvent item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (item.Tick < lastTick || item.HorizonTicks < lastHorizon || item.HorizonTicks > item.Tick)
            throw new MusicGenerationException("MuseCoco returned an event outside its ordered streaming contract.");

        lastTick = item.Tick;
        lastHorizon = item.HorizonTicks;
        var tick = SkyTNTEventMapper.Rescale(item.Tick, 480, ticksPerQuarterNote);
        var mapped = SkyTNTEventMapper.ToMidiEvent(item, tick, 480, ticksPerQuarterNote);
        if (mapped == null)
            throw new MusicGenerationException("MuseCoco returned an unsupported MIDI event kind: " + item.Kind);

        // Subtract AFTER rescaling: a future event at the exclusive horizon may round to this tick.
        var settled = SkyTNTEventMapper.Rescale(item.HorizonTicks, 480, ticksPerQuarterNote) - 1L;
        endTick = Math.Max(endTick, tick + 1L);
        if (mapped is NoteOnEvent note) endTick = Math.Max(endTick, tick + note.NoteLength);

        var meter = SkyTNTEventMapper.MeterOf(mapped);
        if (meter.HasValue)
        {
            if (meters.Count > 0 && meters[meters.Count - 1].Tick == tick)
                meters[meters.Count - 1] = new MeterChange(tick, meter.Value);
            else
                meters.Add(new MeterChange(tick, meter.Value));
        }

        return GeneratedMusicEvent.FromMidiEvent(mapped, settled);
    }

    public GeneratedMusicEvent Finish()
    {
        // Only normal completion settles the buffered tail. Cancellation never calls Finish.
        return lastTick < 0 ? null : GeneratedMusicEvent.SettledThrough(
            BarGrid.RoundUpToBar(meters, ticksPerQuarterNote, endTick));
    }

    public GeneratedMusicEvent SettleBefore(long modelTick)
    {
        if (modelTick < lastHorizon)
            throw new MusicGenerationException("MuseCoco's continuation horizon moved backwards.");
        lastHorizon = modelTick;
        var tick = SkyTNTEventMapper.Rescale(modelTick, 480, ticksPerQuarterNote);
        endTick = Math.Max(endTick, tick);
        return tick > 0 ? GeneratedMusicEvent.SettledThrough(tick - 1L) : null;
    }
}
