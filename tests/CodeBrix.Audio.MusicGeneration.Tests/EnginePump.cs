using System;
using System.Threading.Tasks;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// Drives an engine on a clock the test controls: it moves the clock to the next thing waiting on
/// it - a generator's pacing, or the engine's own pump - until whatever the test is waiting for
/// has happened. Nothing here sleeps.
/// </summary>
public static class EnginePump
{
    private const int StepLimit = 200000;
    private const int QuiescenceYields = 256;

    /// <summary>Runs until a condition is met.</summary>
    /// <param name="time">The clock the engine and the generator are running on.</param>
    /// <param name="until">What the test is waiting for.</param>
    /// <returns>A task that completes once the condition is met.</returns>
    /// <exception cref="InvalidOperationException">
    /// The condition was never met, which fails the test with a message rather than hanging.
    /// </exception>
    public static async Task RunUntilAsync(ManualTimeProvider time, Func<bool> until)
    {
        var steps = 0;

        while (!until())
        {
            steps++;

            if (steps > StepLimit)
            {
                throw new InvalidOperationException(
                    "The engine never reached the state the test was waiting for.");
            }

            await StepAsync(time).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Moves the clock on by one step, having first let everything that is going to wait on the
    /// clock get there.
    /// </summary>
    /// <param name="time">The clock.</param>
    /// <returns>A task that completes once the step has been taken.</returns>
    /// <remarks>
    /// THE WAIT MATTERS. A generation runs on its own task; between one item and the next there is
    /// a moment when it is neither waiting on the clock nor holding anything up. Moving the clock
    /// in that moment would run time on WITHOUT the generator, which is how a perfectly healthy
    /// generator ends up measured as one that cannot keep up. So the clock waits for the pump timer
    /// AND whatever is generating to be on it - and gives up waiting after a while, because once a
    /// generation has finished nothing but the pump is ever going to be there.
    /// </remarks>
    public static async Task StepAsync(ManualTimeProvider time)
    {
        for (var yield = 0; yield < QuiescenceYields && time.PendingTimerCount < 2; yield++)
        {
            await Task.Yield();
        }

        time.AdvanceToNextDue();

        await Task.Yield();
    }
}
