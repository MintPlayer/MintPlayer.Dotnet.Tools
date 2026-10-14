using System.Runtime.CompilerServices;

namespace MintPlayer.Resilience.Pipeline;

/// <summary>The predicate strategies use when the user sets none (Polly's: any exception except a cancellation).</summary>
internal static class DefaultPredicates
{
    /// <summary>
    /// Handles every exception except <see cref="OperationCanceledException"/>, and every rejection (Polly
    /// sees those as <c>TimeoutRejectedException</c> / <c>BrokenCircuitException</c>, which it handles too).
    /// Never allocates.
    /// </summary>
    public static bool HandleOutcome<T>(in Outcome<T> outcome)
        => outcome.IsRejected || outcome.RawException is { } exception && exception is not OperationCanceledException;
}

/// <summary>Waits on the pipeline's <see cref="TimeProvider"/>, blocking for a synchronous execution.</summary>
internal static class DelayHelper
{
    public static ValueTask DelayAsync(TimeProvider timeProvider, TimeSpan delay, ExecutionFrame frame)
    {
        var token = frame.CancellationToken;
        token.ThrowIfCancellationRequested();

        if (delay == TimeSpan.MaxValue)
        {
            delay = System.Threading.Timeout.InfiniteTimeSpan;
        }

        if (frame.IsSynchronous)
        {
            // Sync-over-async only on a resilience event (a retry delay), never on the happy path; as in Polly.
            Task.Delay(delay, timeProvider, token).GetAwaiter().GetResult();
            return default;
        }

        return new ValueTask(Task.Delay(delay, timeProvider, token));
    }
}

/// <summary>Disposes a result that is being discarded (a retried response), swallowing failures; as in Polly.</summary>
internal static class DisposeHelper
{
    public static ValueTask TryDisposeSafeAsync<T>(T value, bool isSynchronous)
    {
        try
        {
            if (isSynchronous)
            {
                if (value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                else if (value is IAsyncDisposable asyncDisposable)
                {
                    return SwallowAsync(asyncDisposable.DisposeAsync());
                }
            }
            else
            {
                if (value is IAsyncDisposable asyncDisposable)
                {
                    return SwallowAsync(asyncDisposable.DisposeAsync());
                }

                if (value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // A failing Dispose must not fail the retry.
        }

        return default;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private static async ValueTask SwallowAsync(ValueTask task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failing DisposeAsync must not fail the retry.
        }
    }
}
