using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A clock the test moves by hand. Nothing in this suite ever sleeps or depends on how busy the
/// machine is: a paced generator waits on a timer this provides, and the test decides when that
/// timer is due.
/// </summary>
/// <remarks>
/// It is deliberately hand-written. A time-testing NuGet package would be a fourth dependency in a
/// repository whose whole point is that it has three.
/// </remarks>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object gate = new object();
    private readonly List<ManualTimer> timers = new List<ManualTimer>();

    private DateTimeOffset now;

    /// <summary>Starts the clock at an arbitrary, fixed moment.</summary>
    public ManualTimeProvider()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    /// <summary>Starts the clock at a chosen moment.</summary>
    /// <param name="start">The moment the clock starts at.</param>
    public ManualTimeProvider(DateTimeOffset start) => now = start;

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) { return now; }
    }

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        lock (gate) { return now.UtcTicks; }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime,
        TimeSpan period)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        var timer = new ManualTimer(this, callback, state);

        lock (gate)
        {
            timers.Add(timer);
        }

        timer.Change(dueTime, period);

        return timer;
    }

    /// <summary>
    /// How many timers are waiting on this clock right now. A test uses it to tell whether
    /// everything that is going to wait on the clock HAS waited on it yet: moving the clock before
    /// a generator has asked it for its next moment is not slowing the generator down, it is
    /// running two clocks, and everything measured against wall time then reads nonsense.
    /// </summary>
    public int PendingTimerCount
    {
        get
        {
            lock (gate)
            {
                var pending = 0;

                foreach (var timer in timers)
                {
                    if (timer.DueAt.HasValue)
                    {
                        pending++;
                    }
                }

                return pending;
            }
        }
    }

    /// <summary>Moves the clock forward, running every timer that falls due on the way.</summary>
    /// <param name="amount">How far to move it. Zero runs anything already due.</param>
    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Time does not go backwards.");
        }

        AdvanceTo(GetUtcNow() + amount);
    }

    /// <summary>Moves the clock to a moment, running every timer that falls due on the way.</summary>
    /// <param name="target">The moment to move it to. A moment in the past does nothing.</param>
    public void AdvanceTo(DateTimeOffset target)
    {
        while (true)
        {
            ManualTimer due;

            lock (gate)
            {
                due = EarliestDue();

                if (due == null || due.DueAt > target)
                {
                    if (target > now)
                    {
                        now = target;
                    }

                    return;
                }

                now = due.DueAt.Value;
                due.Reschedule();
            }

            // Outside the lock: the callback resumes whatever was waiting, and that may schedule
            // the next timer straight away.
            due.Run();
        }
    }

    /// <summary>
    /// Moves the clock to exactly the moment the next timer falls due, and runs it. This is what
    /// makes a pacing test exact rather than approximate.
    /// </summary>
    /// <returns>False when nothing is waiting on the clock.</returns>
    public bool AdvanceToNextDue()
    {
        ManualTimer due;

        lock (gate)
        {
            due = EarliestDue();

            if (due == null)
            {
                return false;
            }

            now = due.DueAt.Value;
            due.Reschedule();
        }

        due.Run();

        return true;
    }

    private ManualTimer EarliestDue()
    {
        ManualTimer earliest = null;

        foreach (var timer in timers)
        {
            if (!timer.DueAt.HasValue)
            {
                continue;
            }

            if (earliest == null || timer.DueAt.Value < earliest.DueAt.Value)
            {
                earliest = timer;
            }
        }

        return earliest;
    }

    private void Forget(ManualTimer timer)
    {
        lock (gate)
        {
            timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider provider;
        private readonly TimerCallback callback;
        private readonly object state;

        private TimeSpan period = Timeout.InfiniteTimeSpan;
        private bool disposed;

        public ManualTimer(ManualTimeProvider provider, TimerCallback callback, object state)
        {
            this.provider = provider;
            this.callback = callback;
            this.state = state;
        }

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (disposed)
            {
                return false;
            }

            this.period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : provider.GetUtcNow() + dueTime;

            return true;
        }

        public void Reschedule() =>
            DueAt = period <= TimeSpan.Zero || period == Timeout.InfiniteTimeSpan
                ? null
                : DueAt + period;

        public void Run() => callback(state);

        public void Dispose()
        {
            disposed = true;
            DueAt = null;
            provider.Forget(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
