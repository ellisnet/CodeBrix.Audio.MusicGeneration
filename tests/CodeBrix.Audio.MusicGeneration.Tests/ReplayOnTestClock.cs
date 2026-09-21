using CodeBrix.Audio.MusicGeneration.Replay;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Puts a replay generator on the clock the test controls.
/// </summary>
/// <remarks>
/// IT MATTERS MORE THAN IT LOOKS. A test that drives the engine on a hand-moved clock while the
/// generator paces itself against the real one is not slowing time down, it is running two clocks:
/// the engine sees hours pass while the generator writes a bar, measures a generation rate near
/// zero and falls back to delivering a whole segment at a time. Both ends go on the same clock.
/// </remarks>
internal static class ReplayOnTestClock
{
    /// <summary>Puts a registered replay generator on a clock, at a pacing rate.</summary>
    /// <param name="name">The name it is registered under.</param>
    /// <param name="time">The clock the test moves.</param>
    /// <param name="pacingRate">How much faster than real time it releases its music.</param>
    /// <returns>The generator.</returns>
    public static ReplayMusicGenerator Registered(string name, ManualTimeProvider time,
        double pacingRate = ReplayMusicGenerator.DefaultPacingRate)
    {
        var generator = (ReplayMusicGenerator)MusicGeneratorRegistry.Resolve(name);

        return On(generator, time, pacingRate);
    }

    /// <summary>Puts a replay generator on a clock, at a pacing rate.</summary>
    /// <param name="generator">The generator.</param>
    /// <param name="time">The clock the test moves.</param>
    /// <param name="pacingRate">How much faster than real time it releases its music.</param>
    /// <returns>The same generator, for chaining.</returns>
    public static ReplayMusicGenerator On(ReplayMusicGenerator generator, ManualTimeProvider time,
        double pacingRate = ReplayMusicGenerator.DefaultPacingRate)
    {
        generator.TimeProvider = time;
        generator.PacingRate = pacingRate;

        return generator;
    }
}
