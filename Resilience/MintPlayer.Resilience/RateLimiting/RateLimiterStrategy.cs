using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.RateLimiting;

/// <summary>
/// The rate limiter for any result type, created once per <c>Build()</c>: it owns the default
/// <see cref="ConcurrencyLimiter"/> when the options name no limiter.
/// </summary>
internal sealed class RateLimiterStrategyFactory : StrategyFactory
{
    public RateLimiterStrategyFactory(RateLimiterStrategyOptions options)
    {
        var snapshot = options.Snapshot();
        OnRejected = snapshot.OnRejected;
        if (snapshot.Instance is { } instance)
        {
            Limiter = instance;
        }
        else if (snapshot.RateLimiter is { } generator)
        {
            Generator = generator;
        }
        else
        {
            Limiter = Wrapper = new ConcurrencyLimiter(snapshot.DefaultRateLimiterOptions);
        }
    }

    /// <summary>The limiter acquired from directly (no delegate, no context), when there is one.</summary>
    public RateLimiter? Limiter { get; }

    /// <summary>The user's lease delegate, when <see cref="Limiter"/> is null.</summary>
    public Func<RateLimiterArguments, ValueTask<RateLimitLease>>? Generator { get; }

    public Func<OnRateLimiterRejectedArguments, ValueTask>? OnRejected { get; }

    /// <summary>The default limiter this strategy created and owns; M6 disposes it with the pipeline (Polly's <c>Wrapper</c>).</summary>
    public RateLimiter? Wrapper { get; }

    public override PipelineStrategy<TResult> Create<TResult>() => new RateLimiterStrategy<TResult>(this);
}

/// <summary>
/// The rate limiter as interpreter hooks. Slot use: <c>Object</c> = the acquired lease, disposed on exit.
/// A refused lease is disposed at once, after <c>OnRejected</c>. A cancelled wait in the limiter's queue
/// surfaces as the <see cref="OperationCanceledException"/> the limiter throws.
/// </summary>
internal sealed class RateLimiterStrategy<T>(RateLimiterStrategyFactory shared) : PipelineStrategy<T>
{
    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        var acquire = shared.Limiter is { } limiter
            ? limiter.AcquireAsync(1, frame.CancellationToken)
            : shared.Generator!(new RateLimiterArguments(frame));

        return acquire.IsCompletedSuccessfully
            ? Admit(frame, index, acquire.Result)
            : EnterSlowAsync(frame, index, acquire);
    }

    public override ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index)
    {
        ref var slot = ref frame.Slots[index];
        if (slot.Object is RateLimitLease lease)
        {
            slot.Object = null;
            lease.Dispose();
        }

        return new(false);
    }

    private ValueTask<bool> Admit(ExecutionFrame<T> frame, int index, RateLimitLease lease)
    {
        if (lease.IsAcquired)
        {
            frame.Slots[index].Object = lease;
            return new(true);
        }

        var retryAfterTicks = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? Math.Max(0, retryAfter.Ticks) : -1;
        frame.Outcome = Outcome.Rejected<T>(RejectionKind.RateLimited, retryAfterTicks);
        if (shared.OnRejected is { } onRejected)
        {
            return RaiseRejectedAsync(onRejected, frame, lease);
        }

        lease.Dispose();
        return new(false);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> EnterSlowAsync(ExecutionFrame<T> frame, int index, ValueTask<RateLimitLease> acquire)
    {
        var lease = await acquire.ConfigureAwait(frame.ContinueOnCapturedContext);
        return await Admit(frame, index, lease).ConfigureAwait(frame.ContinueOnCapturedContext);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<bool> RaiseRejectedAsync(Func<OnRateLimiterRejectedArguments, ValueTask> onRejected, ExecutionFrame<T> frame, RateLimitLease lease)
    {
        try
        {
            await onRejected(new OnRateLimiterRejectedArguments(frame, lease)).ConfigureAwait(frame.ContinueOnCapturedContext);
        }
        finally
        {
            lease.Dispose();
        }

        return false;
    }
}
