using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Fallback;

/// <summary>Fallback as an interpreter hook: an exit that replaces a handled outcome. No slot use.</summary>
internal sealed class FallbackStrategy<T> : PipelineStrategy<T>
{
    private readonly Func<FallbackPredicateArguments<T>, bool> _shouldHandle;
    private readonly Func<FallbackActionArguments<T>, ValueTask<Outcome<T>>> _fallbackAction;
    private readonly Func<OnFallbackArguments<T>, ValueTask>? _onFallback;

    public FallbackStrategy(FallbackStrategyOptions<T> options)
    {
        _shouldHandle = options.ShouldHandle;
        _fallbackAction = options.FallbackAction!;
        _onFallback = options.OnFallback;
    }

    public override ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index)
    {
        var outcome = frame.Outcome;
        return _shouldHandle(new FallbackPredicateArguments<T>(frame, outcome))
            ? FallbackAsync(frame, outcome)
            : new(false);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> FallbackAsync(ExecutionFrame<T> frame, Outcome<T> outcome)
    {
        var continueOnCapturedContext = frame.ContinueOnCapturedContext;
        if (_onFallback is not null)
        {
            await _onFallback(new OnFallbackArguments<T>(frame, outcome)).ConfigureAwait(continueOnCapturedContext);
        }

        try
        {
            frame.Outcome = await _fallbackAction(new FallbackActionArguments<T>(frame, outcome)).ConfigureAwait(continueOnCapturedContext);
        }
        catch (Exception e)
        {
            frame.Outcome = Outcome.FromException<T>(e);
        }

        return false;
    }
}
