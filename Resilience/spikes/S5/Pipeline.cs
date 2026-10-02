using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using S1;

namespace S5;

/// <summary>Cached instances for the rejected "throw a cached exception" variant (kept apart from the sentinels).</summary>
public static class ThrownCache
{
    private static readonly ResilienceRejectedException CircuitOpen = new CircuitOpenException();
    private static readonly ResilienceRejectedException RateLimited = new RateLimitedException();
    private static readonly ResilienceRejectedException Timeout = new ResilienceTimeoutException();

    public static ResilienceRejectedException For(RejectionKind kind) => kind switch
    {
        RejectionKind.CircuitOpen or RejectionKind.CircuitIsolated => CircuitOpen,
        RejectionKind.RateLimited => RateLimited,
        _ => Timeout,
    };
}

/// <summary>Minimal breaker: only the pre-execute check matters for S5 (S4 owns the real one).</summary>
public sealed class Breaker(TimeProvider time)
{
    private readonly Lock _lock = new();
    private bool _open;
    private DateTimeOffset _blockedUntil;

    /// <summary>The exception that broke the circuit; becomes InnerException of the thrown rejection.</summary>
    public Exception? BreakingException { get; private set; }

    public void Trip(TimeSpan duration, Exception? cause)
    {
        lock (_lock) { _open = true; _blockedUntil = time.GetUtcNow() + duration; BreakingException = cause; }
    }

    public bool TryEnter(out TimeSpan retryAfter)
    {
        lock (_lock)
        {
            retryAfter = default;
            if (!_open) return true;
            var now = time.GetUtcNow();
            if (now >= _blockedUntil) { _open = false; return true; } // half-open simplified away
            retryAfter = _blockedUntil - now;
            return false;
        }
    }
}

/// <summary>
/// Shape the generator would emit for [CircuitBreaker] [RateLimiter] [Timeout]: rejections are decided
/// before the callback and returned as a value, never thrown.
/// </summary>
public sealed class OurPipeline(Breaker? breaker, RateLimiter? limiter, TimeSpan timeout)
{
    public Breaker? Breaker => breaker;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Outcome<T>> TryExecuteAsync<T, TState>(Func<TState, CancellationToken, ValueTask<T>> callback, TState state, CancellationToken cancellationToken)
    {
        if (breaker is not null && !breaker.TryEnter(out var cbRetryAfter))
            return Outcome<T>.Rejected(RejectionKind.CircuitOpen, cbRetryAfter);

        RateLimitLease? lease = null;
        if (limiter is not null)
        {
            lease = limiter.AttemptAcquire(1);
            if (!lease.IsAcquired)
            {
                TimeSpan? retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) ? ra : null;
                lease.Dispose();
                return Outcome<T>.Rejected(RejectionKind.RateLimited, retryAfter);
            }
        }

        try
        {
            var cts = CtsPool.Rent(timeout, cancellationToken);
            try
            {
                return new(await callback(state, cts.Cts.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cts.Cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return Outcome<T>.Rejected(RejectionKind.Timeout);
            }
            catch (Exception ex)
            {
                return new(ex);
            }
            finally
            {
                CtsPool.Return(cts);
            }
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <summary>Recommended throwing API: a fresh exception per rejection (own stack, own Data).</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T> ExecuteAsync<T, TState>(Func<TState, CancellationToken, ValueTask<T>> callback, TState state, CancellationToken cancellationToken)
    {
        var outcome = await TryExecuteAsync(callback, state, cancellationToken).ConfigureAwait(false);
        return outcome.GetResultOrThrow(outcome.Rejection == RejectionKind.CircuitOpen ? breaker?.BreakingException : null);
    }

    /// <summary>Rejected alternative: throw the shared cached instance.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T> ExecuteAsyncCachedThrow<T, TState>(Func<TState, CancellationToken, ValueTask<T>> callback, TState state, CancellationToken cancellationToken)
    {
        var outcome = await TryExecuteAsync(callback, state, cancellationToken).ConfigureAwait(false);
        if (outcome.IsRejected) throw ThrownCache.For(outcome.Rejection);
        return outcome.GetResultOrThrow();
    }
}
