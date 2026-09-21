using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Internal;

/// <summary>A metre, and the tick it takes effect at.</summary>
internal readonly struct MeterChange
{
    /// <summary>Creates a metre change.</summary>
    /// <param name="tick">The tick the metre takes effect at.</param>
    /// <param name="meter">The metre from that tick on.</param>
    public MeterChange(long tick, MusicMeter meter)
    {
        Tick = tick;
        Meter = meter;
    }

    /// <summary>The tick the metre takes effect at.</summary>
    public long Tick { get; }

    /// <summary>The metre from that tick on.</summary>
    public MusicMeter Meter { get; }
}
