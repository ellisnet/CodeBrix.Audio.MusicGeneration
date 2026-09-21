using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// Turns a rendition into sound: it watches the music as it is committed, gives each part an
/// instrument the first time that part sounds, and builds the routing table the sequencer plays.
/// </summary>
/// <remarks>
/// <para>
/// TWO THREADS, AND THE LINE BETWEEN THEM IS THE POINT. <see cref="Observe"/> runs on the pump
/// thread, well ahead of the music being heard: it decides, it BUILDS synthesizers, and it leaves
/// a <see cref="PreparedVoice"/> behind. <see cref="ApplyPending"/> runs on the rendering thread
/// inside the sequencer's message hook, and does nothing but hand a prepared instrument to the
/// router. No instrument is ever built on the audio thread.
/// </para>
/// <para>
/// THE ORDER PARTS ARE VOICED IN is the order they first sound, because a rendition written before
/// the music exists cannot know which channel will carry the tune.
/// </para>
/// <para>
/// AND BECAUSE OF THAT, EACH CHANNEL'S SETTINGS ARE KEPT as they are committed - see
/// <see cref="ChannelState"/>. A part that enters at bar nine gets everything the music set for it
/// at tick 0, and a part that is re-voiced keeps the level and the position it had.
/// </para>
/// </remarks>
internal sealed class RenditionVoicer
{
    private const int Channels = 16;
    private const int NoProgram = -1;
    private const int PatchChangeCommand = 0xC0;

    private readonly object gate = new object();
    private readonly MusicRendition rendition;
    private readonly IInstrumentLibrary library;
    private readonly int sampleRate;
    private readonly int[] hints;
    private readonly RoutingSynthesizer router;
    private readonly ConcurrentQueue<PreparedVoice> assignments = new ConcurrentQueue<PreparedVoice>();
    private readonly ConcurrentQueue<PreparedVoice>[] swaps;
    private readonly ChannelState[] channelStates = new ChannelState[Channels + 1];
    private readonly int[] musicPrograms = new int[Channels + 1];
    private readonly int[] orderIndex = new int[Channels + 1];
    private readonly bool[] isVoiced = new bool[Channels + 1];
    private readonly List<PartVoicing> order = new List<PartVoicing>();
    private readonly List<string> diagnostics = new List<string>();
    private readonly HashSet<int> usedPrograms = new HashSet<int>();

    private int nextRenditionVoice;
    private int nextHint;
    private int melodicPartCount;
    private int loneLayerChannel = NoProgram;
    private bool percussionDeclined;

    /// <summary>Builds the voicer and the empty routing table it will fill.</summary>
    /// <param name="rendition">The rendition to voice by. It is used as given and never changed.</param>
    /// <param name="library">The instrument library the parts are played with.</param>
    /// <param name="sampleRate">The rate every instrument and the router render at.</param>
    /// <param name="instrumentHints">The programs the request asked for, or null for none.</param>
    /// <param name="masterVolume">The session's own volume, which multiplies the rendition's master gain.</param>
    public RenditionVoicer(MusicRendition rendition, IInstrumentLibrary library, int sampleRate,
        IEnumerable<GeneralMidiProgram> instrumentHints, float masterVolume)
    {
        this.rendition = rendition;
        this.library = library;
        this.sampleRate = sampleRate;

        var wanted = new List<int>();
        if (instrumentHints != null)
        {
            foreach (var hint in instrumentHints)
            {
                wanted.Add((int)hint);
            }
        }

        hints = wanted.ToArray();

        swaps = new ConcurrentQueue<PreparedVoice>[Channels + 1];
        for (var channel = 1; channel <= Channels; channel++)
        {
            swaps[channel] = new ConcurrentQueue<PreparedVoice>();
            channelStates[channel] = new ChannelState();
            musicPrograms[channel] = NoProgram;
        }

        router = new RoutingSynthesizer(sampleRate) { MasterVolume = rendition.MasterGain * masterVolume };
    }

    /// <summary>The routing table, which is the one synthesizer a sequencer or a player drives.</summary>
    public RoutingSynthesizer Router => router;

    /// <summary>
    /// Takes in one event that has just been committed to the timeline, and voices whatever it
    /// reveals. CALLED ON THE PUMP THREAD, never on the audio thread.
    /// </summary>
    /// <param name="midiEvent">The event, at its tick on the timeline.</param>
    public void Observe(MidiEvent midiEvent)
    {
        if (midiEvent is PatchChangeEvent patch)
        {
            OnProgramChange(patch.Channel, patch.Patch);

            return;
        }

        if (MidiEvent.IsNoteOn(midiEvent))
        {
            OnFirstNote(midiEvent.Channel);

            return;
        }

        OnChannelState(midiEvent);
    }

    /// <summary>
    /// Applies whatever is ready, and swaps a part's instrument when the message being delivered
    /// is the program change that asked for it. CALLED ON THE RENDERING THREAD; it builds nothing.
    /// </summary>
    /// <param name="wireChannel">The channel the message carries, 0 to 15.</param>
    /// <param name="command">The message's command - 0xC0 for a program change.</param>
    /// <param name="data1">The message's first data byte, which is the program for a program change.</param>
    public void ApplyPending(int wireChannel, int command, int data1)
    {
        while (assignments.TryDequeue(out var assignment))
        {
            assignment.Apply(router);
        }

        if (command != PatchChangeCommand)
        {
            return;
        }

        var channel = wireChannel + 1;

        if (channel < 1 || channel > Channels)
        {
            return;
        }

        var queue = swaps[channel];

        if (queue.TryPeek(out var swap) && swap.Program == data1 && queue.TryDequeue(out swap))
        {
            swap.Apply(router);
        }
    }

    /// <summary>Takes a snapshot of how the music is voiced as things stand.</summary>
    /// <returns>The voicing.</returns>
    public MusicVoicing Snapshot()
    {
        lock (gate)
        {
            return new MusicVoicing(rendition.Name, library.Name, router.MasterVolume,
                order.ToArray(), diagnostics.ToArray());
        }
    }

    private void OnChannelState(MidiEvent midiEvent)
    {
        if (midiEvent is not ControlChangeEvent && midiEvent is not PitchWheelChangeEvent &&
            midiEvent is not ChannelAfterTouchEvent)
        {
            return;
        }

        var channel = midiEvent.Channel;

        if (channel < 1 || channel > Channels)
        {
            return;
        }

        lock (gate)
        {
            // Kept for every channel, voiced or not: the part that enters at bar nine needs what
            // the music set for it at tick 0, and it does not exist yet to be told.
            channelStates[channel].Observe(midiEvent);
        }
    }

    private void OnProgramChange(int channel, int program)
    {
        lock (gate)
        {
            if (channel == GeneralMidi.PercussionChannel)
            {
                // The kit is the library's, and a program change on the percussion channel picks a
                // drum kit rather than an instrument. There is nothing here to honour.
                return;
            }

            if (!isVoiced[channel])
            {
                musicPrograms[channel] = program;

                return;
            }

            var voiced = order[orderIndex[channel]];

            if (voiced.Source == VoicingSource.Rendition && !rendition.FollowsProgramChanges)
            {
                // A NAMED RENDITION'S OWN VOICES STAY AS VOICED. The setting governs the parts the
                // rendition's list covers and nothing else: a part chosen by the automatic rule was
                // never a deliberate choice to protect, so it always follows the music.
                return;
            }

            var current = voiced;

            if (current.RequestedProgram == program)
            {
                return;
            }

            var note = string.Format(CultureInfo.InvariantCulture,
                "the music changed this part to {0} part-way through", RenditionVoice.NameOf(program));

            var sounding = Covered(program, channel, ref note);
            var replacement = Build(sounding);

            if (replacement == null)
            {
                return;
            }

            usedPrograms.Add(sounding);
            swaps[channel].Enqueue(PreparedVoice.ForSwap(channel, program, replacement, current.Gain,
                channelStates[channel].Snapshot()));

            order[orderIndex[channel]] = new PartVoicing(channel, false, sounding, program,
                current.Gain, current.LayerProgram, current.LayerGain, VoicingSource.Music, note);
        }
    }

    private void OnFirstNote(int channel)
    {
        lock (gate)
        {
            if (isVoiced[channel])
            {
                return;
            }

            if (channel == GeneralMidi.PercussionChannel)
            {
                VoicePercussion(channel);

                return;
            }

            VoiceMelodic(channel);
        }
    }

    private void VoicePercussion(int channel)
    {
        if (percussionDeclined)
        {
            return;
        }

        if (library.Coverage.PercussionNotes.Count == 0)
        {
            percussionDeclined = true;
            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                "The music has a percussion part on channel {0}, but the instrument library '{1}' " +
                "covers no percussion notes, so that part is silent.", channel, library.Name));

            return;
        }

        var kit = library.CreatePercussionSynthesizer(sampleRate);

        isVoiced[channel] = true;
        orderIndex[channel] = order.Count;
        order.Add(new PartVoicing(channel, true, NoProgram, NoProgram, rendition.PercussionGain,
            null, 0.0F, VoicingSource.Percussion,
            "the percussion channel is always the instrument library's own kit"));

        assignments.Enqueue(PreparedVoice.ForPart(channel, kit, rendition.PercussionGain, null, 0.0F,
            channelStates[channel].Snapshot()));
    }

    private void VoiceMelodic(int channel)
    {
        DropLoneLayerIfNeeded();

        var voice = NextRenditionVoice();

        int requested;
        float gain;
        int? layerProgram = null;
        var layerGain = RenditionVoice.DefaultGain;
        VoicingSource source;
        string note;

        if (voice != null)
        {
            requested = voice.Program;
            gain = voice.Gain;
            layerProgram = voice.LayerProgram;
            layerGain = voice.LayerGain;
            source = VoicingSource.Rendition;
            note = string.Format(CultureInfo.InvariantCulture,
                "the rendition '{0}' voices its part {1} this way", rendition.Name, nextRenditionVoice);
        }
        else if (musicPrograms[channel] >= 0)
        {
            requested = musicPrograms[channel];
            gain = RenditionTaste.GainFor(requested);
            source = VoicingSource.Music;
            note = "the music asked for it with a program change before the part's first note";
        }
        else if (nextHint < hints.Length)
        {
            requested = hints[nextHint++];
            gain = RenditionTaste.GainFor(requested);
            source = VoicingSource.InstrumentHint;
            note = "the request's instrument hints asked for it";
        }
        else
        {
            source = VoicingSource.TasteTable;
            requested = NextTasteProgram(out gain, out note);
        }

        var sounding = source == VoicingSource.TasteTable ? requested : Covered(requested, channel, ref note);
        var synthesizer = Build(sounding);

        if (synthesizer == null)
        {
            return;
        }

        IMidiSynthesizer layer = null;

        if (!layerProgram.HasValue && rendition.Voices.Count == 0 && melodicPartCount == 0 &&
            sounding != RenditionTaste.LoneLayerProgram &&
            library.Coverage.CoversProgram(RenditionTaste.LoneLayerProgram))
        {
            // A part playing on its own is the one case layering was rated well for, so the
            // automatic rendition puts a pad under it - and takes it away again if the music turns
            // out to have a second part. See DropLoneLayerIfNeeded.
            layerProgram = RenditionTaste.LoneLayerProgram;
            layerGain = RenditionTaste.LoneLayerGain;
            loneLayerChannel = channel;
            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                "Channel {0} is the only part so far, so {1} was layered under it at {2} - {3}.",
                channel, RenditionVoice.NameOf(RenditionTaste.LoneLayerProgram),
                RenditionTaste.LoneLayerGain, RenditionTaste.LoneLayerRating));
        }

        if (layerProgram.HasValue)
        {
            layer = Build(layerProgram.Value);

            if (layer == null)
            {
                layerProgram = null;
                loneLayerChannel = NoProgram;
            }
        }

        usedPrograms.Add(sounding);

        if (layerProgram.HasValue)
        {
            usedPrograms.Add(layerProgram.Value);
        }

        melodicPartCount++;
        isVoiced[channel] = true;
        orderIndex[channel] = order.Count;
        order.Add(new PartVoicing(channel, false, sounding, requested, gain, layerProgram, layerGain,
            source, note));

        assignments.Enqueue(PreparedVoice.ForPart(channel, synthesizer, gain, layer, layerGain,
            channelStates[channel].Snapshot()));
    }

    private void DropLoneLayerIfNeeded()
    {
        if (melodicPartCount != 1 || loneLayerChannel == NoProgram)
        {
            return;
        }

        var channel = loneLayerChannel;
        var lone = order[orderIndex[channel]];

        loneLayerChannel = NoProgram;
        assignments.Enqueue(PreparedVoice.ForLayerRemoval(channel));

        order[orderIndex[channel]] = new PartVoicing(channel, false, lone.Program,
            lone.RequestedProgram, lone.Gain, null, 0.0F, lone.Source, lone.Note);

        diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
            "A second part arrived, so the pad layered under channel {0} was taken off again: " +
            "layering two instruments on every part of a piece was rated well below giving each " +
            "part one distinct voice.", channel));
    }

    private RenditionVoice NextRenditionVoice()
    {
        while (nextRenditionVoice < rendition.Voices.Count)
        {
            var voice = rendition.Voices[nextRenditionVoice++];

            if (voice != null)
            {
                return voice;
            }
        }

        return null;
    }

    private int NextTasteProgram(out float gain, out string note)
    {
        foreach (var entry in RenditionTaste.Entries)
        {
            if (usedPrograms.Contains(entry.Program) || !library.Coverage.CoversProgram(entry.Program))
            {
                continue;
            }

            gain = entry.Gain;
            note = entry.Rating;

            return entry.Program;
        }

        // Sixteen parts and a table of fifteen: whatever is covered and not yet used will do, as
        // long as it is a DISTINCT voice and not left at unity gain.
        foreach (var program in library.Coverage.Programs)
        {
            if (usedPrograms.Contains(program))
            {
                continue;
            }

            gain = RenditionTaste.ChosenElsewhereGain;
            note = "the taste table was used up, so this is the first instrument the library " +
                   "covers that no other part is already playing";

            return program;
        }

        gain = RenditionTaste.ChosenElsewhereGain;
        note = "the instrument library covers nothing that no other part is already playing";

        return 0;
    }

    private int Covered(int requested, int channel, ref string note)
    {
        if (library.Coverage.CoversProgram(requested))
        {
            return requested;
        }

        var replacement = FirstCoveredTasteProgram();

        if (replacement == NoProgram)
        {
            return requested;
        }

        diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
            "Channel {0} asked for {1}, which the instrument library '{2}' does not cover, so it " +
            "is playing {3} instead.", channel, RenditionVoice.NameOf(requested), library.Name,
            RenditionVoice.NameOf(replacement)));

        note = string.Format(CultureInfo.InvariantCulture,
            "{0} was asked for, but '{1}' does not cover it", RenditionVoice.NameOf(requested),
            library.Name);

        return replacement;
    }

    private int FirstCoveredTasteProgram()
    {
        foreach (var entry in RenditionTaste.Entries)
        {
            if (!usedPrograms.Contains(entry.Program) && library.Coverage.CoversProgram(entry.Program))
            {
                return entry.Program;
            }
        }

        foreach (var program in library.Coverage.Programs)
        {
            if (!usedPrograms.Contains(program))
            {
                return program;
            }
        }

        return NoProgram;
    }

    private IMidiSynthesizer Build(int program)
    {
        var synthesizer = library.CreateSynthesizer(program, sampleRate);

        if (synthesizer == null)
        {
            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                "The instrument library '{0}' returned nothing for {1}, so that part is silent.",
                library.Name, RenditionVoice.NameOf(program)));

            return null;
        }

        if (synthesizer.SampleRate != sampleRate)
        {
            // Caught here rather than in the routing table, because the routing table is changed on
            // the rendering thread and an exception thrown there stops the music.
            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                "The instrument library '{0}' was asked for {1} at {2} Hz and built an instrument " +
                "that renders at {3} Hz, so that part is silent.", library.Name,
                RenditionVoice.NameOf(program), sampleRate, synthesizer.SampleRate));

            return null;
        }

        return synthesizer;
    }
}
