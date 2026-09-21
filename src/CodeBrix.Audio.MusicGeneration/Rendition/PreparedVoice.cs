using System.Collections.Generic;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// A change to the routing table that has already been BUILT and is waiting to be applied.
/// </summary>
/// <remarks>
/// <para>
/// IT EXISTS BECAUSE OF THE AUDIO THREAD. A <see cref="RoutingSynthesizer"/> is single-threaded by
/// contract: its table may not be changed while it is rendering. The only moment anything may
/// touch it is inside the sequencer's message hook, which runs on the rendering thread between one
/// block and the next - and that is the worst possible place to BUILD a synthesizer, because
/// building one may read a file.
/// </para>
/// <para>
/// So the two halves are split. The pump thread, which has all the time in the world, builds the
/// synthesizer and leaves one of these behind; the hook does nothing but hand it to the router.
/// </para>
/// <para>
/// IT ALSO CARRIES THE PART'S SETTINGS. A child that has just been built knows nothing: not the
/// level the music set at tick 0, not the position, not the expression, not the pitch-bend range.
/// Those are sent to it here, BEFORE its first note - see <see cref="ChannelState"/>.
/// </para>
/// </remarks>
internal sealed class PreparedVoice
{
    private readonly IMidiSynthesizer synthesizer;
    private readonly float gain;
    private readonly IMidiSynthesizer layer;
    private readonly float layerGain;
    private readonly bool clearsLayer;
    private readonly IReadOnlyList<ChannelStateMessage> state;

    private PreparedVoice(int channel, int program, IMidiSynthesizer synthesizer, float gain,
        IMidiSynthesizer layer, float layerGain, bool clearsLayer,
        IReadOnlyList<ChannelStateMessage> state)
    {
        Channel = channel;
        Program = program;
        this.synthesizer = synthesizer;
        this.gain = gain;
        this.layer = layer;
        this.layerGain = layerGain;
        this.clearsLayer = clearsLayer;
        this.state = state;
    }

    /// <summary>The MIDI channel the change applies to, 1 to 16.</summary>
    public int Channel { get; }

    /// <summary>
    /// The General MIDI program the change carries, which is what a program-change message has to
    /// name for a swap to be the one it asked for. It is -1 when the change is not a swap.
    /// </summary>
    public int Program { get; }

    /// <summary>Builds the change that gives a part its instrument for the first time.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizer">The instrument, already built.</param>
    /// <param name="gain">The level the part sits at.</param>
    /// <param name="layer">The instrument layered under it, already built, or null for none.</param>
    /// <param name="layerGain">The level the layer sits at.</param>
    /// <param name="state">The part's settings so far, sent to the new instrument before its first note.</param>
    /// <returns>The prepared change.</returns>
    public static PreparedVoice ForPart(int channel, IMidiSynthesizer synthesizer, float gain,
        IMidiSynthesizer layer, float layerGain, IReadOnlyList<ChannelStateMessage> state) =>
        new PreparedVoice(channel, -1, synthesizer, gain, layer, layerGain, false, state);

    /// <summary>Builds the change a program change later in the piece asks for.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="program">The program the music changed to.</param>
    /// <param name="synthesizer">The new instrument, already built.</param>
    /// <param name="gain">The level the part keeps sitting at.</param>
    /// <param name="state">The part's settings so far, so that the mix does not jump at the change.</param>
    /// <returns>The prepared change.</returns>
    public static PreparedVoice ForSwap(int channel, int program, IMidiSynthesizer synthesizer,
        float gain, IReadOnlyList<ChannelStateMessage> state) =>
        new PreparedVoice(channel, program, synthesizer, gain, null, 0.0F, false, state);

    /// <summary>Builds the change that takes the layer off a part that turned out not to be alone.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns>The prepared change.</returns>
    public static PreparedVoice ForLayerRemoval(int channel) =>
        new PreparedVoice(channel, -1, null, 0.0F, null, 0.0F, true, null);

    /// <summary>
    /// Applies the change. CALLED ON THE RENDERING THREAD, and it never builds anything: every
    /// synthesizer it hands over was built elsewhere.
    /// </summary>
    /// <param name="router">The routing table to change.</param>
    public void Apply(RoutingSynthesizer router)
    {
        if (clearsLayer)
        {
            router.ClearLayer(Channel);

            return;
        }

        if (synthesizer != null)
        {
            router.SetChannel(Channel, synthesizer, gain);
            SendState(synthesizer);
        }

        if (layer != null)
        {
            router.SetLayer(Channel, layer, layerGain);
            SendState(layer);
        }
    }

    private void SendState(IMidiSynthesizer target)
    {
        if (state == null || state.Count == 0)
        {
            return;
        }

        // The instrument is told directly rather than through the router, so that a child already
        // sitting on this channel - the other half of a layered pair - is not sent it twice.
        var wireChannel = Channel - 1;

        for (var i = 0; i < state.Count; i++)
        {
            var message = state[i];

            target.ProcessMidiMessage(wireChannel, message.Command, message.Data1, message.Data2);
        }
    }
}
