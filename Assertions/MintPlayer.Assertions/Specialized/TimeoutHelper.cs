namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Waits on a task with a timeout, without leaving a timer behind when the task wins.
/// </summary>
/// <remarks>
/// The obvious spelling — <c>await Task.WhenAny(task, Task.Delay(timeout)) == task</c> — leaks: when
/// the task completes first, the <c>Task.Delay</c> timer stays armed for the full timeout, holding
/// its callback and state alive. One stray timer is nothing; one per assertion, in a suite that runs
/// thousands, is a steady drip of live timer objects and a measurable tail of wake-ups after the
/// tests have finished.
///
/// Cancelling the delay as soon as the task wins is the whole fix. The <c>CancellationTokenSource</c>
/// is disposed either way.
/// </remarks>
internal static class TimeoutHelper
{
    /// <summary>
    /// Returns true when <paramref name="task"/> completed within <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// Does not observe the task's outcome — a faulted task still returns <c>true</c>, because it
    /// did complete in time. The caller awaits the task itself to surface a fault, which is what
    /// keeps "it threw" and "it ran out of time" as two distinct failure messages.
    /// </remarks>
    public static async Task<bool> CompletesWithin(Task task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource();

        var delay = Task.Delay(timeout, cts.Token);
        var winner = await Task.WhenAny(task, delay).ConfigureAwait(false);

        if (winner != task) return false;

        // The task won: cancel the timer rather than letting it run to term.
        cts.Cancel();
        return true;
    }
}
