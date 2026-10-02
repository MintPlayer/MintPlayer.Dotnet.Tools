using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Timeout;

/// <summary>
/// Timeout for any result type. One instance per <c>Build()</c>, owning the CTS pool of the pipeline's
/// <see cref="TimeProvider"/>; its typed hooks are <see cref="TimeoutStrategy{T}"/>.
/// </summary>
internal sealed class TimeoutStrategyFactory : StrategyFactory
{
    public TimeoutStrategyFactory(TimeoutStrategyOptions options, StrategyBuildContext context)
    {
        var snapshot = options.Snapshot();
        Timeout = snapshot.Timeout;
        TimeoutGenerator = snapshot.TimeoutGenerator;
        OnTimeout = snapshot.OnTimeout;
        Pool = CancellationTokenSourcePool.For(context.TimeProvider);
    }

    public TimeSpan Timeout { get; }

    public Func<TimeoutGeneratorArguments, TimeSpan>? TimeoutGenerator { get; }

    public Func<OnTimeoutArguments, ValueTask>? OnTimeout { get; }

    public CancellationTokenSourcePool Pool { get; }

    public override PipelineStrategy<TResult> Create<TResult>() => new TimeoutStrategy<TResult>(this);
}

/// <summary>
/// Timeout as interpreter hooks. Slot use: <c>Object</c> = the rented CTS (null when no timeout applies),
/// <c>Token</c> = the token it replaced, <c>Registration</c> = the link from that token, <c>Long</c> = the
/// timeout in ticks.
/// </summary>
internal sealed class TimeoutStrategy<T>(TimeoutStrategyFactory shared) : PipelineStrategy<T>
{
    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        var timeout = shared.TimeoutGenerator is { } generator
            ? generator(new TimeoutGeneratorArguments(frame))
            : shared.Timeout;

        ref var slot = ref frame.Slots[index];
        if (timeout <= TimeSpan.Zero)
        {
            // Zero, negative and InfiniteTimeSpan: no timeout for this execution (Polly's ShouldApplyTimeout).
            slot.Object = null;
            return new(true);
        }

        var previous = frame.CancellationToken;
        var source = shared.Pool.Rent(timeout);
        slot.Object = source;
        slot.Token = previous;
        slot.Long = timeout.Ticks;
        slot.Registration = previous.CanBeCanceled
            ? previous.UnsafeRegister(static state => ((CancellationTokenSource)state!).Cancel(), source)
            : default;
        frame.SetCancellationToken(source.Token);
        return new(true);
    }

    public override ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index)
    {
        ref var slot = ref frame.Slots[index];
        if (slot.Object is not CancellationTokenSource source)
        {
            return new(false);
        }

        var previous = slot.Token;
        var timeoutTicks = slot.Long;
        var fired = source.IsCancellationRequested;

        frame.SetCancellationToken(previous);
        slot.Registration.Dispose();
        slot = default;
        shared.Pool.Return(source);

        // A timeout only when our token fired, the callback reacted with a cancellation, and the caller
        // did not cancel: then it is a TimeoutRejectedException in Polly, a Timeout rejection here.
        if (fired && frame.Outcome.RawException is OperationCanceledException cancellation && !previous.IsCancellationRequested)
        {
            frame.Outcome = Outcome.Rejected<T>(RejectionKind.Timeout, timeoutTicks, cancellation);
            if (shared.OnTimeout is { } onTimeout)
            {
                return RaiseOnTimeoutAsync(onTimeout, frame, TimeSpan.FromTicks(timeoutTicks));
            }
        }

        return new(false);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<bool> RaiseOnTimeoutAsync(Func<OnTimeoutArguments, ValueTask> onTimeout, ExecutionFrame<T> frame, TimeSpan timeout)
    {
        await onTimeout(new OnTimeoutArguments(frame, timeout)).ConfigureAwait(frame.ContinueOnCapturedContext);
        return false;
    }
}
