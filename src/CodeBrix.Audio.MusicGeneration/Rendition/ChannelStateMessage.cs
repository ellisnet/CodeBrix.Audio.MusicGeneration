namespace CodeBrix.Audio.MusicGeneration.Rendition;

/// <summary>
/// One MIDI message, ready to be handed straight to a synthesizer: the command, and its two data
/// bytes. The channel is not in it, because the only thing that sends one of these already knows
/// which part it is talking to.
/// </summary>
internal readonly struct ChannelStateMessage
{
    /// <summary>Creates a message.</summary>
    /// <param name="command">The command byte with the channel masked off - 0xB0, 0xE0 or 0xD0.</param>
    /// <param name="data1">The first data byte.</param>
    /// <param name="data2">The second data byte.</param>
    public ChannelStateMessage(int command, int data1, int data2)
    {
        Command = command;
        Data1 = data1;
        Data2 = data2;
    }

    /// <summary>The command byte with the channel masked off.</summary>
    public int Command { get; }

    /// <summary>The first data byte.</summary>
    public int Data1 { get; }

    /// <summary>The second data byte.</summary>
    public int Data2 { get; }
}
