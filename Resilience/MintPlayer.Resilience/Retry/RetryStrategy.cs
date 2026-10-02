using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Retry;

/// <summary>
/// Retry as interpreter hooks. Slot use: <c>Int</c> = 0-based attempt, <c>Double</c> = decorrelated-jitter
/// state, <c>Long</c> = timestamp at the start of the current attempt.
/// </summary>
internal sealed class RetryStrategy<T> : PipelineStrategy<T>
{
    private readonly TimeProvider _timeProvider;
    private readonly int _maxRetryAttempts;
    private readonly DelayBackoffType _backoffType;
    private readonly bool _useJitter;
    private readonly TimeSpan _delay;
    private readonly TimeSpan? _maxDelay;
    private readonly Func<RetryPredicateArguments<T>, bool> _shouldHandle;
    private readonly Func<RetryDelayGeneratorArguments<T>, TimeSpan?>? _delayGenerator;
    private readonly Func<OnRetryArguments<T>, ValueTask>? _onRetry;
    private readonly Func<double> _randomizer;
    private readonly RetryBudget? _budget;
    private readonly Func<OnRetryBudgetExhaustedArguments<T>, ValueTask>? _onBudgetExhausted;

    public RetryStrategy(RetryStrategyOptions<T> options, StrategyBuildContext context)
    {
        _timeProvider = context.TimeProvider;
        _maxRetryAttempts = options.MaxRetryAttempts;
        _backoffType = options.BackoffType;
        _useJitter = options.UseJitter;
        _delay = options.Delay;
        _maxDelay = options.MaxDelay;
        _shouldHandle = options.ShouldHandle;
        _delayGenerator = options.DelayGenerator;
        _onRetry = options.OnRetry;
        _randomizer = options.Randomizer;
        _budget = options.Budget;
        _onBudgetExhausted = options.OnBudgetExhausted;
    }

    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        // Once per execution: retries re-enter only the strategies inside this one.
        _budget?.Deposit();
        ref var slot = ref frame.Slots[index];
        slot.Int = 0;
        slot.Double = 0;
        slot.Long = _timeProvider.GetTimestamp();
        return new(true);
    }

    public override ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index)
    {
        ref var slot = ref frame.Slots[index];
        var attempt = slot.Int;
        var outcome = frame.Outcome;

        // As Polly: the predicate runs for every attempt, the last one included.
        var handle = _shouldHandle(new RetryPredicateArguments<T>(frame, outcome, attempt));
        if (!handle || IsLastAttempt(attempt))
        {
            return new(false);
        }

        if (_budget is not null && !_budget.TryWithdraw())
        {
            // Out of budget: this attempt becomes the last one, and its outcome is returned.
            return _onBudgetExhausted is null ? new(false) : RaiseBudgetExhaustedAsync(_onBudgetExhausted, frame, outcome, attempt);
        }

        var delay = RetryHelper.GetRetryDelay(_backoffType, _useJitter, attempt, _delay, _maxDelay, ref slot.Double, _randomizer);
        if (_delayGenerator is not null
            && _delayGenerator(new RetryDelayGeneratorArguments<T>(frame, outcome, attempt)) is TimeSpan generated
            && RetryHelper.IsValidDelay(generated))
        {
            delay = generated;
        }

        var duration = _timeProvider.GetElapsedTime(slot.Long);
        return RetryAsync(frame, index, outcome, attempt, delay, duration);
    }

    // attempt == int.MaxValue: retry forever without incrementing (Polly's IsLastAttempt).
    private bool IsLastAttempt(int attempt) => attempt != int.MaxValue && attempt >= _maxRetryAttempts;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<bool> RaiseBudgetExhaustedAsync(Func<OnRetryBudgetExhaustedArguments<T>, ValueTask> onBudgetExhausted, ExecutionFrame<T> frame, Outcome<T> outcome, int attempt)
    {
        await onBudgetExhausted(new OnRetryBudgetExhaustedArguments<T>(frame, outcome, attempt)).ConfigureAwait(frame.ContinueOnCapturedContext);
        return false;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> RetryAsync(ExecutionFrame<T> frame, int index, Outcome<T> outcome, int attempt, TimeSpan delay, TimeSpan duration)
    {
        var continueOnCapturedContext = frame.ContinueOnCapturedContext;
        if (_onRetry is not null)
        {
            await _onRetry(new OnRetryArguments<T>(frame, outcome, attempt, delay, duration)).ConfigureAwait(continueOnCapturedContext);
        }

        // The discarded result (e.g. an HttpResponseMessage) is disposed, as in Polly. Value types are skipped:
        // testing them for IDisposable would box on every retry.
        if (!typeof(T).IsValueType && outcome.TryGetResult(out var result))
        {
            await DisposeHelper.TryDisposeSafeAsync(result, frame.IsSynchronous).ConfigureAwait(continueOnCapturedContext);
        }

        try
        {
            frame.CancellationToken.ThrowIfCancellationRequested();
            if (delay > TimeSpan.Zero)
            {
                await DelayHelper.DelayAsync(_timeProvider, delay, frame).ConfigureAwait(continueOnCapturedContext);
            }
        }
        catch (OperationCanceledException e)
        {
            frame.Outcome = Outcome.FromException<T>(e);
            return false;
        }

        var slots = frame.Slots;
        if (attempt != int.MaxValue)
        {
            slots[index].Int = attempt + 1;
        }

        slots[index].Long = _timeProvider.GetTimestamp();
        return true;
    }
}

/// <summary>Retry for the non-generic builder: adapts <see cref="RetryStrategyOptions"/> to each result type.</summary>
internal sealed class RetryStrategyFactory(RetryStrategyOptions<object> options, StrategyBuildContext context) : StrategyFactory
{
    private readonly RetryStrategyOptions<object> _options = options.Snapshot();

    public override PipelineStrategy<TResult> Create<TResult>()
    {
        if (typeof(TResult) == typeof(object))
        {
            return (PipelineStrategy<TResult>)(object)new RetryStrategy<object>(_options, context);
        }

        var source = _options;
        var shouldHandle = source.ShouldHandle;
        var delayGenerator = source.DelayGenerator;
        var onRetry = source.OnRetry;
        var onBudgetExhausted = source.OnBudgetExhausted;
        var typed = new RetryStrategyOptions<TResult>
        {
            Name = source.Name,
            MaxRetryAttempts = source.MaxRetryAttempts,
            BackoffType = source.BackoffType,
            UseJitter = source.UseJitter,
            Delay = source.Delay,
            MaxDelay = source.MaxDelay,
            Randomizer = source.Randomizer,
            ShouldHandle = ReferenceEquals(shouldHandle, RetryStrategyOptions<object>.DefaultShouldHandle)
                ? RetryStrategyOptions<TResult>.DefaultShouldHandle
                : args => shouldHandle(args.AsObject()),
            DelayGenerator = delayGenerator is null ? null : args => delayGenerator(args.AsObject()),
            OnRetry = onRetry is null ? null : args => onRetry(args.AsObject()),
            Budget = source.Budget,
            OnBudgetExhausted = onBudgetExhausted is null ? null : args => onBudgetExhausted(args.AsObject()),
        };

        return new RetryStrategy<TResult>(typed, context);
    }
}
