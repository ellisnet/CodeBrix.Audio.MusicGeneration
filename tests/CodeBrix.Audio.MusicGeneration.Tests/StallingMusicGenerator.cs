using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A generator that writes a piece it already has, paced on the test's clock, AND CAN BE MADE TO
/// STALL in the middle of it - which is the one thing the replay generators cannot do.
/// </summary>
/// <remarks>
/// <para>
/// TWO THINGS MAKE IT DIFFERENT FROM A REPLAY, and both are needed to see a play head run past the
/// music:
/// </para>
/// <list type="bullet">
/// <item><description>
/// ITS NOTES RING PAST THE END OF ITS PASS. A replay rounds a pass up to a whole bar AFTER the last
/// note has finished sounding, so nothing of it is ever still ringing when the next segment is
/// placed. Here the length of a pass is given, so a note can be written that sounds across the
/// seam - which is the seam lever the plan asks for, and the reason the timeline's horizon can run
/// ahead of the music that has been written.
/// </description></item>
/// <item><description>
/// IT CAN STOP WRITING AND START AGAIN. <see cref="StallFrom"/> holds everything from a tick
/// onwards until <see cref="Resume"/> - or <see cref="ResumeByEndingThePass"/>, which ends the
/// pass where it stood. While it is stalling it WAITS ON THE TEST'S CLOCK, so the clock keeps
/// something to move and the engine sees time pass without music arriving, exactly as it would
/// with a model that is thinking.
/// </description></item>
/// </list>
/// <para>
/// THE SETTLED TICK IS HONEST, as a replay's is: events that share a tick are released together and
/// only the last of them says that tick has settled, and the pass ends by settling the whole of its
/// own length.
/// </para>
/// </remarks>
internal sealed class StallingMusicGenerator : IMusicGenerator
{
    private readonly object gate = new object();
    private readonly List<MusicRequest> requests = new List<MusicRequest>();
    private readonly IReadOnlyList<MidiEvent> music;
    private readonly TimeProvider clock;
    private readonly TimeSpan pumpInterval = TimeSpan.FromMilliseconds(100.0);
    private readonly long passTicks;
    private readonly double secondsPerTick;

    private long stallFromTick = long.MaxValue;
    private bool endThePass;
    private bool stalling;
    private int itemCount;
    private bool loaded;

    /// <summary>Creates a generator over a piece.</summary>
    /// <param name="name">The name it is asked for under.</param>
    /// <param name="timeProvider">The clock its pacing and its stalling run on.</param>
    /// <param name="music">The music of one pass, in tick order, at ticks from the pass's start.</param>
    /// <param name="passTicks">
    /// How long one pass is. It is what the pass settles through, and it is DELIBERATELY allowed to
    /// be shorter than the end of the last note, so that notes ring across the seam.
    /// </param>
    /// <param name="ticksPerQuarterNote">The resolution the music is written at.</param>
    /// <param name="beatsPerMinute">The tempo the pacing is measured at.</param>
    public StallingMusicGenerator(string name, TimeProvider timeProvider,
        IReadOnlyList<MidiEvent> music, long passTicks, int ticksPerQuarterNote,
        double beatsPerMinute = 120.0)
    {
        Name = name;
        clock = timeProvider;
        this.music = music;
        this.passTicks = passTicks;
        secondsPerTick = 60.0 / (beatsPerMinute * ticksPerQuarterNote);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Family => "Test";

    /// <inheritdoc />
    public string Description => "A test generator that can stall in the middle of a piece.";

    /// <summary>
    /// A continuation, and nothing else - the same as a replay, so that the engine asks it for the
    /// next segment the way it asks any generator that cannot be told what to play.
    /// </summary>
    public MusicRequestFeatures Honours => MusicRequestFeatures.Continuation;

    /// <inheritdoc />
    public bool IsLoaded
    {
        get { lock (gate) { return loaded; } }
    }

    /// <summary>How much faster than real time it writes its music.</summary>
    public double PacingRate { get; set; } = 4.0;

    /// <summary>Every request it was asked with, in order, as it was at the moment it was asked.</summary>
    public IReadOnlyList<MusicRequest> Requests
    {
        get { lock (gate) { return requests.ToArray(); } }
    }

    /// <summary>How many passes it has been asked for.</summary>
    public int PassCount
    {
        get { lock (gate) { return requests.Count; } }
    }

    /// <summary>How many items have been pulled out of it altogether.</summary>
    public int ItemCount => Volatile.Read(ref itemCount);

    /// <summary>Whether it is stalled right now, with music still to write.</summary>
    public bool IsStalling
    {
        get { lock (gate) { return stalling; } }
    }

    /// <summary>Stops writing anything at or after a tick, until it is told to carry on.</summary>
    /// <param name="tick">The first tick of the music it will not write yet.</param>
    public void StallFrom(long tick)
    {
        lock (gate)
        {
            stallFromTick = tick;
            endThePass = false;
        }
    }

    /// <summary>Carries on writing where it stopped.</summary>
    public void Resume()
    {
        lock (gate)
        {
            stallFromTick = long.MaxValue;
            endThePass = false;
        }
    }

    /// <summary>
    /// Ends the pass where it stopped instead of carrying on, which is a generator that has decided
    /// the piece is over - and the one case where the bars before the next segment are the REST the
    /// engine inserted rather than music.
    /// </summary>
    public void ResumeByEndingThePass()
    {
        lock (gate)
        {
            endThePass = true;
        }
    }

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            loaded = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Release()
    {
        lock (gate)
        {
            loaded = false;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(MusicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        MusicGeneratorCapabilities.EnsureHonoured(this, request);

        lock (gate)
        {
            requests.Add(request.Clone());
            loaded = true;
        }

        var started = clock.GetUtcNow();
        var settledSoFar = GeneratedMusicEvent.NothingSettled;
        var ended = false;

        for (var i = 0; i < music.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tick = music[i].AbsoluteTime;

            if (!await ReadyForAsync(tick, started, cancellationToken).ConfigureAwait(false))
            {
                ended = true;

                break;
            }

            var lastOfTick = i == music.Count - 1 || music[i + 1].AbsoluteTime > tick;
            var settled = lastOfTick ? tick : settledSoFar;

            if (lastOfTick)
            {
                settledSoFar = tick;
            }

            Interlocked.Increment(ref itemCount);

            yield return GeneratedMusicEvent.FromMidiEvent(music[i].Clone(), settled);
        }

        if (!ended && passTicks > settledSoFar)
        {
            // The pass runs to its own length, and the silence at the end of it is settled music
            // too - so the pass ends by saying so, with nothing to play.
            Interlocked.Increment(ref itemCount);

            yield return GeneratedMusicEvent.SettledThrough(passTicks);
        }
    }

    /// <summary>
    /// Waits until the music at a tick may be written: for its moment to come round, and for any
    /// stall to be lifted.
    /// </summary>
    /// <returns>False when the pass was told to end where it stood.</returns>
    private async Task<bool> ReadyForAsync(long tick, DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (gate)
            {
                if (tick < stallFromTick)
                {
                    stalling = false;

                    break;
                }

                if (endThePass)
                {
                    stalling = false;

                    return false;
                }

                stalling = true;
            }

            // WAITING ON THE TEST'S CLOCK, never on the real one: a stall is a generator that is
            // thinking, and the clock has to have something on it or the test's pump would be the
            // only thing moving time.
            await Task.Delay(pumpInterval, clock, cancellationToken).ConfigureAwait(false);
        }

        var due = started + TimeSpan.FromSeconds(tick * secondsPerTick / PacingRate);
        var wait = due - clock.GetUtcNow();

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds)), clock,
                cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
