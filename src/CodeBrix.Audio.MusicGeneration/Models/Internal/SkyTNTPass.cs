using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Internal;
using CodeBrix.Ollama.ModelRunner;
using AudioMidiEvent = CodeBrix.Audio.Midi.MidiEvent;
using ModelMidiEvent = CodeBrix.Ollama.ModelRunner.MidiEvent;

namespace CodeBrix.Audio.MusicGeneration.Models.Internal;

/// <summary>
/// One pass of the SkyTNT model: where the prompt ends, where the segment's ticks start, where the
/// bar lines fall, and which events have settled far enough to be let out.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE MODEL PROMISES IS A HORIZON, NOT AN ORDER. Every event the model runner yields carries
/// <c>HorizonTicks</c>, which means "no event yielded after this one will have a tick BELOW this
/// number" - so an event AT that tick can still arrive. This library's settled tick is a stronger
/// promise - nothing AT or before it will change or be added - so the horizon becomes a settled
/// tick of one less, and never more than that.
/// </para>
/// <para>
/// A PASS ENDS ON A BAR LINE. Events are held here until the horizon has passed the END of the bar
/// they sit in, so the bar they belong to is complete before any of it is let out; when the model
/// stops, whatever is left is the RAGGED LAST BAR and it is dropped. The cost is one bar of
/// latency and the events of one bar per pass; what it buys is a seam that always falls on a bar
/// line.
/// </para>
/// <para>
/// A PASS THAT DID NOT REACH ONE WHOLE BAR IS ROUNDED UP TO ONE rather than yielding nothing, so
/// that a small event cap still moves the music forward instead of ending the piece.
/// </para>
/// <para>
/// THE SEGMENT'S TICKS START AT THE PROMPT'S END. The model counts from the start of the prompt it
/// was given, and a segment's ticks start at 0, so the prompt's length is taken off every tick.
/// The model may place its first events inside the prompt's LAST BAR - it carries on from the last
/// event it was shown, not from the bar line after it - and those are moved to the seam rather
/// than dropped, because the music they belong to has not been played yet.
/// </para>
/// </remarks>
internal sealed class SkyTNTPass
{
    private readonly List<MeterChange> meterChanges = new List<MeterChange>();
    private readonly List<Pending> pending = new List<Pending>();
    private readonly int ticksPerQuarterNote;
    private readonly int modelTicksPerQuarterNote;
    private readonly long originModelTicks;

    private long horizonTick;
    private long releasedThroughBar;
    private long highestTick;

    private SkyTNTPass(MidiScore prompt, int ticksPerQuarterNote, int modelTicksPerQuarterNote,
        long originModelTicks, MusicMeter meter)
    {
        Prompt = prompt;
        this.ticksPerQuarterNote = ticksPerQuarterNote;
        this.modelTicksPerQuarterNote = modelTicksPerQuarterNote;
        this.originModelTicks = originModelTicks;

        meterChanges.Add(new MeterChange(0L, meter));
    }

    /// <summary>The music the model is asked to continue, or null when it starts a piece.</summary>
    public MidiScore Prompt { get; }

    /// <summary>Where the segment's tick 0 sits in the model's own ticks.</summary>
    public long OriginModelTicks => originModelTicks;

    /// <summary>How many events were dropped as the ragged last bar.</summary>
    public int DroppedEventCount { get; private set; }

    /// <summary>How many events the pass has let out.</summary>
    public int ReleasedEventCount { get; private set; }

    /// <summary>The bar line the pass has settled through.</summary>
    public long SettledThroughBar => releasedThroughBar;

    /// <summary>Works out everything one pass needs before the model is asked for anything.</summary>
    /// <param name="request">The request.</param>
    /// <param name="options">The generator's own settings.</param>
    /// <param name="modelTicksPerQuarterNote">The resolution the model writes at.</param>
    /// <returns>The pass.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static SkyTNTPass For(MusicRequest request, SkyTNTGeneratorOptions options,
        int modelTicksPerQuarterNote)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var ticks = request.TicksPerQuarterNote;
        var meter = MeterInForce(request);
        var music = MusicToContinue(request, out var isTail);

        if (music == null)
        {
            return new SkyTNTPass(null, ticks, modelTicksPerQuarterNote, 0L, meter);
        }

        var promptTicks = music.DeltaTicksPerQuarterNote;
        var span = isTail && request.Continuation.TailTicks > 0L
            ? request.Continuation.TailTicks
            : EndOfTheMusic(music);

        var ticksPerBar = meter.TicksPerBar(promptTicks);
        var cut = SkyTNTPrompt.CutAt(span, ticksPerBar, options.MaximumPromptBars);
        var prompt = SkyTNTPrompt.Build(music, promptTicks, cut);

        var origin = prompt == null
            ? 0L
            : SkyTNTEventMapper.Rescale(span - cut, promptTicks, modelTicksPerQuarterNote);

        return new SkyTNTPass(prompt, ticks, modelTicksPerQuarterNote, origin, meter);
    }

    /// <summary>Takes in one of the model runner's events and lets out whatever has settled.</summary>
    /// <param name="item">The event.</param>
    /// <returns>What to yield, which is often nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public IReadOnlyList<GeneratedMusicEvent> Accept(ModelMidiEvent item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        var tick = ToSegmentTick(item.Tick);
        var midiEvent = SkyTNTEventMapper.ToMidiEvent(item, tick, modelTicksPerQuarterNote,
            ticksPerQuarterNote);

        if (midiEvent != null)
        {
            Observe(midiEvent, tick);
            pending.Add(new Pending(tick, midiEvent));

            if (tick > highestTick)
            {
                highestTick = tick;
            }
        }

        var horizon = ToSegmentTick(item.HorizonTicks);

        if (horizon > horizonTick)
        {
            horizonTick = horizon;
        }

        return ReleaseCompleteBars();
    }

    /// <summary>Ends the pass: the ragged last bar is dropped and the bar line is settled.</summary>
    /// <returns>What to yield last, which may be nothing at all.</returns>
    public IReadOnlyList<GeneratedMusicEvent> Finish()
    {
        if (releasedThroughBar > 0L)
        {
            DroppedEventCount += pending.Count;
            pending.Clear();

            // THE LAST WORD IS THE BAR LINE ITSELF: the segment after this one starts there.
            return new[] { GeneratedMusicEvent.SettledThrough(releasedThroughBar) };
        }

        if (pending.Count == 0)
        {
            return Array.Empty<GeneratedMusicEvent>();
        }

        // LESS THAN ONE WHOLE BAR WAS WRITTEN, so the pass is rounded UP to a bar rather than
        // yielding nothing at all - a pass that produced no music would end the piece.
        var bar = BarGrid.RoundUpToBar(meterChanges, ticksPerQuarterNote, highestTick + 1L);

        if (bar <= 0L)
        {
            bar = meterChanges[0].Meter.TicksPerBar(ticksPerQuarterNote);
        }

        releasedThroughBar = bar;

        return TakeThrough(bar);
    }

    private static MusicMeter MeterInForce(MusicRequest request)
    {
        if (request.Continuation != null && request.Continuation.Meter.HasValue)
        {
            return request.Continuation.Meter.Value;
        }

        if (request.Intent != null && request.Intent.Meter.HasValue)
        {
            return request.Intent.Meter.Value;
        }

        return MusicMeter.CommonTime;
    }

    private static MidiEventCollection MusicToContinue(MusicRequest request, out bool isTail)
    {
        // A CONTINUATION WINS OVER A PRIMER. The tail is what the music has just been doing; a
        // primer is where the piece began, and a piece cannot begin twice.
        if (request.Continuation != null && request.Continuation.Tail != null)
        {
            isTail = true;

            return request.Continuation.Tail;
        }

        isTail = false;

        return request.Primer;
    }

    private static long EndOfTheMusic(MidiEventCollection music)
    {
        var end = 0L;

        for (var track = 0; track < music.Tracks; track++)
        {
            foreach (var midiEvent in music[track])
            {
                if (midiEvent == null)
                {
                    continue;
                }

                var at = midiEvent.AbsoluteTime +
                         (midiEvent is NoteOnEvent note && note.OffEvent != null
                             ? note.NoteLength
                             : 0L);

                if (at > end)
                {
                    end = at;
                }
            }
        }

        return end;
    }

    private long ToSegmentTick(long modelTick)
    {
        var moved = modelTick - originModelTicks;

        // THE MODEL CARRIES ON FROM THE LAST EVENT IT WAS SHOWN, which may be a beat or two before
        // the prompt's end, so its first events can land inside the bar already played. They are
        // moved to the seam rather than dropped: the music they belong to is still to be heard.
        if (moved < 0L)
        {
            moved = 0L;
        }

        return SkyTNTEventMapper.Rescale(moved, modelTicksPerQuarterNote, ticksPerQuarterNote);
    }

    private void Observe(AudioMidiEvent midiEvent, long tick)
    {
        var meter = SkyTNTEventMapper.MeterOf(midiEvent);

        if (!meter.HasValue)
        {
            return;
        }

        // A METRE CHANGE STARTS A BAR, and one at a tick a later change has already been recorded
        // at cannot happen: the model's own ticks never go backwards by a whole beat.
        if (meterChanges[meterChanges.Count - 1].Tick == tick)
        {
            meterChanges[meterChanges.Count - 1] = new MeterChange(tick, meter.Value);
        }
        else if (tick > meterChanges[meterChanges.Count - 1].Tick)
        {
            meterChanges.Add(new MeterChange(tick, meter.Value));
        }
    }

    private IReadOnlyList<GeneratedMusicEvent> ReleaseCompleteBars()
    {
        var bar = BarGrid.RoundDownToBar(meterChanges, ticksPerQuarterNote, horizonTick);

        if (bar <= releasedThroughBar)
        {
            return Array.Empty<GeneratedMusicEvent>();
        }

        releasedThroughBar = bar;

        // THE BAR LINE ITSELF IS NOT SETTLED YET: the bar that starts there is still being
        // written, so the promise stops one tick short of it.
        return TakeThrough(bar - 1L);
    }

    // A SETTLED STRETCH WITH NOTHING IN IT IS STILL WORTH SAYING: a bar of rest has to move the
    // settled tick on, or a long silence would look exactly like a generator that had stalled.
    private IReadOnlyList<GeneratedMusicEvent> TakeThrough(long settledThroughTick)
    {
        var taken = new List<GeneratedMusicEvent>();
        var kept = new List<Pending>();

        for (var i = 0; i < pending.Count; i++)
        {
            if (pending[i].Tick <= settledThroughTick)
            {
                taken.Add(GeneratedMusicEvent.FromMidiEvent(pending[i].Event,
                    GeneratedMusicEvent.NothingSettled));
            }
            else
            {
                kept.Add(pending[i]);
            }
        }

        pending.Clear();
        pending.AddRange(kept);

        if (taken.Count == 0)
        {
            return new[] { GeneratedMusicEvent.SettledThrough(settledThroughTick) };
        }

        // ONLY THE LAST ITEM SAYS THE TICK HAS SETTLED. An earlier one saying so would let the
        // caller play it before the rest of the group had arrived.
        taken[taken.Count - 1] = GeneratedMusicEvent.FromMidiEvent(
            taken[taken.Count - 1].Event, settledThroughTick);

        ReleasedEventCount += taken.Count;

        return taken;
    }

    private readonly struct Pending
    {
        public Pending(long tick, AudioMidiEvent midiEvent)
        {
            Tick = tick;
            Event = midiEvent;
        }

        public long Tick { get; }

        public AudioMidiEvent Event { get; }
    }
}
