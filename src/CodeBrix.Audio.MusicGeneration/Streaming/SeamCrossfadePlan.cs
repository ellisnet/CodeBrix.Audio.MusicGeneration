using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Rendering;
using CodeBrix.Audio.MusicGeneration.Rendition;

namespace CodeBrix.Audio.MusicGeneration.Streaming;

/// <summary>
/// ONE CROSSFADE AT A FRESH SEAM, worked out in full on the pump thread and handed to the
/// <see cref="SeamCrossfadeMixer"/> to carry out on the rendering thread.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IT HOLDS is everything the rendering thread needs and nothing it would have to build: the
/// instruments the INCOMING piece will play through (a voicer of its own, over the same instrument
/// library), the OUTGOING piece's last moments as ready-made channel messages timed in frames from
/// the start of the fade, and how long the fade is and what shape it follows.
/// </para>
/// <para>
/// THE CUE is how the rendering thread knows the moment has come. The timeline is one stream of
/// channel messages, played by one sequencer, and the only thing that can tell the mixer "the
/// incoming piece starts HERE" at exactly the right block is a message on that timeline. So the
/// engine writes one: a control change on a controller the MIDI specification leaves undefined,
/// carrying this plan's number, one tick before the incoming piece's first tick. The mixer
/// swallows it - no instrument ever sees it - and switches.
/// </para>
/// <para>
/// NOTHING HERE CHANGES ONCE IT IS HANDED OVER, except the two ticks, which only the engine reads
/// and which move with the music when it is held back to a later bar line.
/// </para>
/// </remarks>
internal sealed class SeamCrossfadePlan
{
    /// <summary>The channel the cue is written on, 1 to 16: the last one.</summary>
    public const int CueChannel = 16;

    /// <summary>
    /// The controller the cue is written on: 119, which the MIDI specification leaves undefined, so
    /// that nothing a synthesizer understands is ever mistaken for it.
    /// </summary>
    public const int CueController = 119;

    private readonly long[] frames;
    private readonly int[] channels;
    private readonly int[] commands;
    private readonly int[] firstData;
    private readonly int[] secondData;

    private int disarmed;

    /// <summary>Works out a crossfade.</summary>
    /// <param name="cue">The plan's number, 1 to 127, which the cue on the timeline carries.</param>
    /// <param name="cueTick">The tick the cue is written at: one before <paramref name="startTick"/>.</param>
    /// <param name="startTick">The tick the incoming piece starts at on the timeline.</param>
    /// <param name="incoming">The voicer - and so the instruments - the incoming piece plays through.</param>
    /// <param name="fadeFrames">How long the fade is, in frames at the mixer's rate.</param>
    /// <param name="curve">The shape the fade follows.</param>
    /// <param name="tail">
    /// The outgoing piece's last moments: each event with the time it falls at, measured from the
    /// cue. Anything that is not a channel message is left out, because only channel messages are
    /// ever sent to an instrument.
    /// </param>
    public SeamCrossfadePlan(int cue, long cueTick, long startTick, RenditionVoicer incoming,
        long fadeFrames, MusicFadeCurve curve, IReadOnlyList<TimedTailEvent> tail)
    {
        Cue = cue;
        CueTick = cueTick;
        StartTick = startTick;
        Incoming = incoming;
        FadeFrames = fadeFrames < 1L ? 1L : fadeFrames;
        Curve = curve;

        var messages = new List<TailMessage>();
        var rate = incoming.Router.SampleRate;

        for (var i = 0; i < tail.Count; i++)
        {
            var timed = tail[i];

            Add(messages, timed.Event, timed.At, rate);

            if (timed.Event is NoteOnEvent note && note.OffEvent != null)
            {
                Add(messages, note.OffEvent, timed.OffAt, rate);
            }
        }

        // IN TIME ORDER, and in the order they were written at any one moment: a stable sort, so a
        // note-off and a note-on of the same key at the same frame keep their meaning.
        var ordered = new TailMessage[messages.Count];
        messages.CopyTo(ordered);
        Array.Sort(ordered, (first, second) => first.Frame != second.Frame
            ? first.Frame.CompareTo(second.Frame)
            : first.Sequence.CompareTo(second.Sequence));

        frames = new long[ordered.Length];
        channels = new int[ordered.Length];
        commands = new int[ordered.Length];
        firstData = new int[ordered.Length];
        secondData = new int[ordered.Length];

        for (var i = 0; i < ordered.Length; i++)
        {
            frames[i] = ordered[i].Frame;
            channels[i] = ordered[i].Channel;
            commands[i] = ordered[i].Command;
            firstData[i] = ordered[i].Data1;
            secondData[i] = ordered[i].Data2;
        }
    }

    /// <summary>The plan's number, 1 to 127, which the cue on the timeline carries.</summary>
    public int Cue { get; }

    /// <summary>
    /// The tick the cue is written at. It is one tick before the incoming piece starts, so that at
    /// the incoming piece's first tick every message - whatever channel it is on - already comes
    /// after the switch.
    /// </summary>
    public long CueTick { get; set; }

    /// <summary>The tick the incoming piece starts at on the timeline.</summary>
    public long StartTick { get; set; }

    /// <summary>The voicer, and so the instruments, the incoming piece plays through.</summary>
    public RenditionVoicer Incoming { get; }

    /// <summary>How long the fade is, in frames at the mixer's rate. Never less than one.</summary>
    public long FadeFrames { get; }

    /// <summary>The shape the fade follows.</summary>
    public MusicFadeCurve Curve { get; }

    /// <summary>Whether the engine took the plan back before its cue was written.</summary>
    public bool IsDisarmed => Volatile.Read(ref disarmed) != 0;

    /// <summary>How many channel messages the outgoing piece still has to play.</summary>
    public int TailCount => frames.Length;

    /// <summary>The frame, from the cue, a tail message is due at.</summary>
    /// <param name="index">Which message.</param>
    /// <returns>The frame.</returns>
    public long TailFrame(int index) => frames[index];

    /// <summary>The 0-based channel of a tail message, as a synthesizer takes it.</summary>
    /// <param name="index">Which message.</param>
    /// <returns>The channel.</returns>
    public int TailChannel(int index) => channels[index];

    /// <summary>The command of a tail message, with no channel in it.</summary>
    /// <param name="index">Which message.</param>
    /// <returns>The command.</returns>
    public int TailCommand(int index) => commands[index];

    /// <summary>The first data byte of a tail message.</summary>
    /// <param name="index">Which message.</param>
    /// <returns>The data byte.</returns>
    public int TailData1(int index) => firstData[index];

    /// <summary>The second data byte of a tail message.</summary>
    /// <param name="index">Which message.</param>
    /// <returns>The data byte.</returns>
    public int TailData2(int index) => secondData[index];

    /// <summary>Takes the plan back: its cue will never be written, so the mixer passes it by.</summary>
    public void Disarm() => Volatile.Write(ref disarmed, 1);

    /// <summary>Builds the cue for a plan: the one message on the timeline that says "switch here".</summary>
    /// <param name="tick">The tick to write it at.</param>
    /// <param name="cue">The plan's number.</param>
    /// <returns>The event.</returns>
    public static MidiEvent CueEvent(long tick, int cue) =>
        new ControlChangeEvent(tick, CueChannel, (MidiController)CueController, cue);

    /// <summary>Whether an event is a cue the engine wrote, rather than music.</summary>
    /// <param name="midiEvent">The event.</param>
    /// <param name="tick">The tick a cue was written at.</param>
    /// <param name="cue">The number it carried.</param>
    /// <returns>True when it is that cue.</returns>
    public static bool IsCue(MidiEvent midiEvent, long tick, int cue) =>
        midiEvent is ControlChangeEvent control && control.AbsoluteTime == tick &&
        control.Channel == CueChannel && (int)control.Controller == CueController &&
        control.ControllerValue == cue;

    private static void Add(List<TailMessage> messages, MidiEvent midiEvent, TimeSpan at, int rate)
    {
        // Exactly what the timeline itself sends: the short message a MIDI file would carry, with
        // the channel already made 0-based.
        var raw = midiEvent.GetAsShortMessage();
        var status = raw & 0xFF;
        var command = status & 0xF0;

        if (command < 0x80 || command >= 0xF0)
        {
            return;
        }

        var frame = (long)Math.Round(at.TotalSeconds * rate, MidpointRounding.AwayFromZero);

        messages.Add(new TailMessage(frame < 0L ? 0L : frame, messages.Count, status & 0x0F,
            command, (raw >> 8) & 0x7F, (raw >> 16) & 0x7F));
    }

    private readonly struct TailMessage
    {
        public TailMessage(long frame, int sequence, int channel, int command, int data1, int data2)
        {
            Frame = frame;
            Sequence = sequence;
            Channel = channel;
            Command = command;
            Data1 = data1;
            Data2 = data2;
        }

        public long Frame { get; }

        public int Sequence { get; }

        public int Channel { get; }

        public int Command { get; }

        public int Data1 { get; }

        public int Data2 { get; }
    }
}
