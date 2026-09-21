using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Ollama.ModelRunner;
using AudioMidiEvent = CodeBrix.Audio.Midi.MidiEvent;
using ModelMidiEvent = CodeBrix.Ollama.ModelRunner.MidiEvent;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// Turns the music this library holds - the tail of a continuation, or a primer the application
/// supplied - into the <see cref="MidiScore"/> the model reads as the piece it is carrying on from.
/// </summary>
/// <remarks>
/// <para>
/// NO FILE ROUND TRIP. A score is built from events directly, so nothing is written to disk and
/// read back to hand a model four bars of music.
/// </para>
/// <para>
/// CHANNELS GO BACK DOWN BY ONE, which is the other half of the rule in
/// <see cref="SkyTNTEventMapper"/> - and this is the only place it happens in this direction.
/// </para>
/// <para>
/// THE TAIL IS CUT TO ITS LAST BARS, not its first. A longer prompt costs generation time and
/// context, and the bars the new music follows on from are the ones at the END - which matters,
/// because the model runner's own <c>PromptEventLimit</c> keeps events from the BEGINNING of what
/// it is given.
/// </para>
/// </remarks>
internal static class SkyTNTPrompt
{
    /// <summary>How many tracks the model reads, so a prompt is written within them.</summary>
    private const int ModelTrackLimit = 127;

    /// <summary>Builds the score a pass continues from.</summary>
    /// <param name="music">The music to continue - a continuation's tail or a primer.</param>
    /// <param name="ticksPerQuarterNote">The resolution the music's ticks are at.</param>
    /// <param name="fromTick">The first tick to take, so that a long tail is cut to its last bars.</param>
    /// <returns>The score, or null when there is no music in it.</returns>
    /// <remarks>
    /// THE SCORE KEEPS THE MUSIC'S OWN RESOLUTION. A score carries its own ticks per quarter note
    /// and the model's tokenizer reads it, so nothing is rescaled on the way in; only what comes
    /// back out is.
    /// </remarks>
    public static MidiScore Build(MidiEventCollection music, int ticksPerQuarterNote, long fromTick)
    {
        if (music == null)
        {
            return null;
        }

        var events = new List<ModelMidiEvent>();

        for (var track = 0; track < music.Tracks; track++)
        {
            var modelTrack = track < ModelTrackLimit ? track : ModelTrackLimit;

            foreach (var midiEvent in music[track])
            {
                if (midiEvent == null || midiEvent.AbsoluteTime < fromTick)
                {
                    continue;
                }

                var converted = Convert(midiEvent, midiEvent.AbsoluteTime - fromTick, modelTrack);

                if (converted != null)
                {
                    events.Add(converted);
                }
            }
        }

        return events.Count == 0 ? null : new MidiScore(ticksPerQuarterNote, events);
    }

    /// <summary>
    /// The tick a tail is cut at so that no more than a number of bars of it is handed over.
    /// </summary>
    /// <param name="tailTicks">How long the tail is.</param>
    /// <param name="ticksPerBar">How long a bar of the metre in force is.</param>
    /// <param name="maximumBars">The most bars to keep.</param>
    /// <returns>The first tick of the tail to keep, which is a bar line of the tail's own grid.</returns>
    public static long CutAt(long tailTicks, long ticksPerBar, int maximumBars)
    {
        if (tailTicks <= 0L || ticksPerBar <= 0L || maximumBars < 1)
        {
            return 0L;
        }

        var keep = ticksPerBar * maximumBars;

        return tailTicks <= keep ? 0L : tailTicks - keep;
    }

    /// <summary>How long a bar is, given what a continuation says is in force.</summary>
    /// <param name="continuation">The continuation, or null.</param>
    /// <param name="request">The request, for an intent that names a metre.</param>
    /// <param name="ticksPerQuarterNote">The resolution.</param>
    /// <returns>The length of a bar in ticks; common time when nothing says otherwise.</returns>
    public static long TicksPerBar(MusicContinuation continuation, MusicRequest request,
        int ticksPerQuarterNote)
    {
        if (continuation != null && continuation.Meter.HasValue)
        {
            return continuation.Meter.Value.TicksPerBar(ticksPerQuarterNote);
        }

        if (request != null && request.Intent != null && request.Intent.Meter.HasValue)
        {
            return request.Intent.Meter.Value.TicksPerBar(ticksPerQuarterNote);
        }

        return MusicMeter.CommonTime.TicksPerBar(ticksPerQuarterNote);
    }

    private static ModelMidiEvent Convert(AudioMidiEvent midiEvent, long tick, int track)
    {
        switch (midiEvent)
        {
            case NoteOnEvent note:
                var length = note.OffEvent == null ? 0L : note.NoteLength;

                if (length < 1L || !IsModelChannel(note.Channel) || note.Velocity < 1)
                {
                    // A NOTE WITH NO LENGTH IS NOT MUSIC THE MODEL CAN READ, and a note-on with a
                    // velocity of nought is a note-off written the old way.
                    return null;
                }

                return ModelMidiEvent.Note(tick, track, ModelChannel(note.Channel),
                    note.NoteNumber, note.Velocity, length);

            case PatchChangeEvent patch when IsModelChannel(patch.Channel):
                return ModelMidiEvent.ProgramChange(tick, track, ModelChannel(patch.Channel),
                    patch.Patch);

            case ControlChangeEvent control when IsModelChannel(control.Channel):
                var controller = (int)control.Controller;

                return controller < 0 || controller > 127 || control.ControllerValue < 0 ||
                       control.ControllerValue > 127
                    ? null
                    : ModelMidiEvent.ControlChange(tick, track, ModelChannel(control.Channel),
                        controller, control.ControllerValue);

            case TempoEvent tempo:
                return tempo.MicrosecondsPerQuarterNote < 1 ||
                       tempo.MicrosecondsPerQuarterNote > 0xFFFFFF
                    ? null
                    : ModelMidiEvent.TempoFromMicroseconds(tick, 0,
                        tempo.MicrosecondsPerQuarterNote);

            case TimeSignatureEvent signature:
                return signature.Numerator < 1 || signature.Numerator > 255 ||
                       signature.Denominator < 0 || signature.Denominator > 7
                    ? null
                    : ModelMidiEvent.TimeSignature(tick, 0, signature.Numerator,
                        1 << signature.Denominator);

            case KeySignatureEvent key:
                return key.SharpsFlats < -7 || key.SharpsFlats > 7
                    ? null
                    : ModelMidiEvent.KeySignature(tick, 0, key.SharpsFlats, key.MajorMinor == 1);

            default:
                // Everything else - note-offs, text, the carried horizon - is not music this model
                // reads, and the model runner's own reader drops the same kinds.
                return null;
        }
    }

    // THE OTHER HALF OF THE CHANNEL RULE: CodeBrix.Audio counts 1 to 16, the model counts 0 to 15.
    private static int ModelChannel(int channel) => channel - 1;

    private static bool IsModelChannel(int channel) => channel >= 1 && channel <= 16;
}
