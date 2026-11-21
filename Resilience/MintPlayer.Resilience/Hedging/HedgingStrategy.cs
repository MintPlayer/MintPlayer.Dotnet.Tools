using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Hedging;

/// <summary>
/// Hedging as a <see cref="ForkingStrategy{T}"/>: it runs the inner remainder of the pipeline once per attempt,
/// concurrently, and keeps the first acceptable outcome. A port of Polly's <c>HedgingResilienceStrategy</c>,
/// <c>HedgingExecutionContext</c> and <c>TaskExecution</c>, with the same pooling (one execution context per
/// execution and one attempt object per attempt, both pooled per strategy; attempt CTSs from the CTS pool).
/// The remaining allocations (a task per attempt, <c>Task.WhenAny</c>, the delay timer) are intrinsic (S1).
/// </summary>
internal sealed class HedgingStrategy<T> : ForkingStrategy<T>
{
    private readonly ObjectPool<HedgingExecution<T>> _executions;
    private readonly ObjectPool<HedgingAttempt<T>> _attempts;

    public HedgingStrategy(HedgingStrategyOptions<T> options, StrategyBuildContext context)
    {
        var snapshot = options.Snapshot();
        Delay = snapshot.Delay;
        TotalAttempts = snapshot.MaxHedgedAttempts + 1;
        ShouldHandle = snapshot.ShouldHandle;
        ActionGenerator = snapshot.ActionGenerator;
        IsDefaultActionGenerator = ReferenceEquals(snapshot.ActionGenerator, HedgingStrategyOptions<T>.DefaultActionGenerator);
        DelayGenerator = snapshot.DelayGenerator;
        OnHedging = snapshot.OnHedging;
        TimeProvider = context.TimeProvider;
        CancellationPool = CancellationTokenSourcePool.For(context.TimeProvider);
        _attempts = new(() => new HedgingAttempt<T>(this));
        _executions = new(() => new HedgingExecution<T>(this));
    }

    public TimeSpan Delay { get; }

    /// <summary>The primary attempt plus the hedged attempts.</summary>
    public int TotalAttempts { get; }

    public Func<HedgingPredicateArguments<T>, bool> ShouldHandle { get; }

    public Func<HedgingActionGeneratorArguments<T>, Func<ValueTask<Outcome<T>>>?> ActionGenerator { get; }

    /// <summary>With the default generator a hedged attempt runs the inner pipeline directly, without the generator's closures.</summary>
    public bool IsDefaultActionGenerator { get; }

    public Func<HedgingDelayGeneratorArguments, TimeSpan>? DelayGenerator { get; }

    public Func<OnHedgingArguments<T>, ValueTask>? OnHedging { get; }

    public TimeProvider TimeProvider { get; }

    public CancellationTokenSourcePool CancellationPool { get; }

    public HedgingAttempt<T> RentAttempt() => _attempts.Get();

    public void ReturnAttempt(HedgingAttempt<T> attempt) => _attempts.Return(attempt);

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public override async ValueTask ExecuteAsync(ExecutionFrame<T> frame, int index, InnerPipeline<T> inner)
    {
        // Hedging always needs the context: every attempt runs on its own copy of it (as in Polly).
        var primary = frame.Context;
        var continueOnCapturedContext = frame.ContinueOnCapturedContext;
        var execution = _executions.Get();
        execution.Initialize(primary, inner);
        try
        {
            // The token at this depth; the attempts link to it, and the strategies inside cannot replace it.
            var cancellationToken = primary.CancellationToken;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    frame.Outcome = Outcome.FromException<T>(new OperationCanceledException(cancellationToken));
                    return;
                }

                if (await execution.LoadAsync().ConfigureAwait(continueOnCapturedContext) is { } finished)
                {
                    frame.Outcome = WithCallerCancellationToken(finished, cancellationToken);
                    return;
                }

                var delay = DelayGenerator is { } generator
                    ? generator(new HedgingDelayGeneratorArguments(primary, execution.LoadedAttempts))
                    : Delay;

                var completed = await execution.TryWaitForCompletedAttemptAsync(delay).ConfigureAwait(continueOnCapturedContext);
                if (completed is null)
                {
                    continue;
                }

                if (!completed.IsHandled)
                {
                    completed.Accept();
                    frame.Outcome = WithCallerCancellationToken(completed.Outcome, cancellationToken);
                    return;
                }
            }
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(continueOnCapturedContext);
            _executions.Return(execution);
        }
    }

    /// <summary>An attempt cancelled because the caller cancelled reports the caller's token, as in Polly.</summary>
    private static Outcome<T> WithCallerCancellationToken(Outcome<T> outcome, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested && outcome.RawException is OperationCanceledException cancellation && cancellation.CancellationToken != callerToken)
        {
            return Outcome.FromException<T>(new OperationCanceledException(cancellation.Message, cancellation, callerToken));
        }

        return outcome;
    }
}

/// <summary>
/// The state of one hedging execution: every attempt started so far, and those still running. Pooled per
/// strategy (Polly's <c>HedgingExecutionContext</c>).
/// </summary>
internal sealed class HedgingExecution<T>(HedgingStrategy<T> strategy)
{
    private readonly List<HedgingAttempt<T>> _attempts = [];
    private readonly List<HedgingAttempt<T>> _running = [];
    private ResilienceContext? _primary;
    private InnerPipeline<T>? _inner;
    private bool _declined;

    public int LoadedAttempts => _attempts.Count;

    private ResilienceContext Primary => _primary!;

    public void Initialize(ResilienceContext primary, InnerPipeline<T> inner)
    {
        _primary = primary;
        _inner = inner;
    }

    /// <summary>
    /// Starts the next attempt, if any is left and the action generator provides one. Returns the final outcome
    /// when no attempt can start and none is running: then every attempt was handled, and the first one to
    /// complete in start order (the primary, as in Polly) is accepted.
    /// </summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Outcome<T>?> LoadAsync()
    {
        if (_attempts.Count >= strategy.TotalAttempts)
        {
            return OutcomeWhenNothingStarts();
        }

        var attempt = strategy.RentAttempt();
        bool started;
        try
        {
            started = await attempt.InitializeAsync(Primary, _inner!, _attempts.Count).ConfigureAwait(Primary.ContinueOnCapturedContext);
        }
        catch
        {
            // OnHedging threw; the attempt has released what it held.
            strategy.ReturnAttempt(attempt);
            throw;
        }

        _declined = !started;
        if (started)
        {
            _attempts.Add(attempt);
            _running.Add(attempt);
            return null;
        }

        strategy.ReturnAttempt(attempt);
        return OutcomeWhenNothingStarts();
    }

    /// <summary>
    /// Waits for a running attempt to complete, or for <paramref name="delay"/> (null = start the next attempt).
    /// Zero: parallel mode, no wait. Negative: fallback mode, wait for a completion. All attempts started: wait
    /// for a completion.
    /// </summary>
    /// <remarks>
    /// Deviation from Polly: when the action generator has just declined (returned null) and the delay is zero,
    /// this waits for a completion too. Polly asks the generator again at once, spinning until an attempt completes.
    /// </remarks>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<HedgingAttempt<T>?> TryWaitForCompletedAttemptAsync(TimeSpan delay)
    {
        if (TryRemoveCompleted() is { } completed)
        {
            return completed;
        }

        var continueOnCapturedContext = Primary.ContinueOnCapturedContext;
        if (_attempts.Count == strategy.TotalAttempts || delay < TimeSpan.Zero || (_declined && delay == TimeSpan.Zero))
        {
            await WhenAnyRunning().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | (continueOnCapturedContext ? ConfigureAwaitOptions.ContinueOnCapturedContext : 0));
            return TryRemoveCompleted();
        }

        if (delay == TimeSpan.Zero || _attempts.Count == 0)
        {
            return null;
        }

        var whenAny = WhenAnyRunning();
        await whenAny.WaitAsync(delay, strategy.TimeProvider, Primary.CancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | (continueOnCapturedContext ? ConfigureAwaitOptions.ContinueOnCapturedContext : 0));

        return whenAny.IsCompleted ? TryRemoveCompleted() : null;
    }

    /// <summary>
    /// Ends the execution: the accepted attempt's context properties are merged into the primary context, the
    /// other attempts are cancelled and awaited (the hedging strategy completes only when all of them have), and
    /// the results of the attempts that were not accepted are disposed.
    /// </summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask DisposeAsync()
    {
        foreach (var attempt in _attempts)
        {
            if (attempt.IsAccepted)
            {
                Primary.Properties.AddOrReplaceProperties(attempt.Context.Properties);
                break;
            }
        }

        foreach (var attempt in _running)
        {
            attempt.Cancel();
        }

        foreach (var attempt in _attempts)
        {
            await attempt.Execution.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await attempt.ResetAsync().ConfigureAwait(false);
            strategy.ReturnAttempt(attempt);
        }

        _attempts.Clear();
        _running.Clear();
        _declined = false;
        _primary = null;
        _inner = null;
    }

    private Outcome<T>? OutcomeWhenNothingStarts()
    {
        if (_running.Count != 0)
        {
            return null;
        }

        foreach (var attempt in _attempts)
        {
            if (attempt.Execution.IsCompleted)
            {
                attempt.Accept();
                return attempt.Outcome;
            }
        }

        return null;
    }

    private Task WhenAnyRunning()
    {
        switch (_running.Count)
        {
            case 0:
                return Task.CompletedTask;
            case 1:
                return _running[0].Execution;
            case 2:
                return Task.WhenAny(_running[0].Execution, _running[1].Execution);
            default:
                var tasks = new Task[_running.Count];
                for (var i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = _running[i].Execution;
                }

                return Task.WhenAny(tasks);
        }
    }

    private HedgingAttempt<T>? TryRemoveCompleted()
    {
        for (var i = 0; i < _running.Count; i++)
        {
            var attempt = _running[i];
            if (attempt.Execution.IsCompleted)
            {
                _running.RemoveAt(i);
                return attempt;
            }
        }

        return null;
    }
}

/// <summary>
/// One attempt (primary or hedged): its own context (a copy of the primary context), its own CTS linked to
/// the primary token, the running task and its outcome. Pooled per strategy (Polly's <c>TaskExecution</c>).
/// </summary>
internal sealed class HedgingAttempt<T>(HedgingStrategy<T> strategy)
{
    private readonly ResilienceContext _context = new();
    private CancellationTokenSource? _cancellation;
    private CancellationTokenRegistration _registration;

    /// <summary>The attempt; it never faults (failures are outcomes).</summary>
    public Task Execution { get; private set; } = Task.CompletedTask;

    /// <summary>The attempt's outcome; valid once <see cref="Execution"/> has completed.</summary>
    public Outcome<T> Outcome { get; private set; }

    /// <summary>Whether <see cref="HedgingStrategyOptions{TResult}.ShouldHandle"/> handled the outcome (so hedging goes on).</summary>
    public bool IsHandled { get; private set; }

    /// <summary>Whether this attempt's outcome is the one the strategy returns.</summary>
    public bool IsAccepted { get; private set; }

    public ResilienceContext Context => _context;

    public void Accept() => IsAccepted = true;

    public void Cancel()
    {
        if (!IsAccepted)
        {
            _cancellation?.Cancel();
        }
    }

    /// <summary>Starts the attempt. False when the action generator returned no action (nothing was started).</summary>
    public ValueTask<bool> InitializeAsync(ResilienceContext primary, InnerPipeline<T> inner, int attemptNumber)
    {
        _cancellation = strategy.CancellationPool.Rent(System.Threading.Timeout.InfiniteTimeSpan);
        _context.InitializeFrom(primary, _cancellation.Token);
        _registration = primary.CancellationToken.UnsafeRegister(static state => ((CancellationTokenSource)state!).Cancel(), _cancellation);

        if (attemptNumber == 0)
        {
            // The primary attempt starts inline: a synchronous callback completes before the first delay.
            Execution = RunAsync(inner, null, isSynchronous: false, attemptNumber);
            return new(true);
        }

        return InitializeHedgedAsync(primary, inner, attemptNumber);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> InitializeHedgedAsync(ResilienceContext primary, InnerPipeline<T> inner, int attemptNumber)
    {
        Func<ValueTask<Outcome<T>>>? action = null;
        if (!strategy.IsDefaultActionGenerator)
        {
            try
            {
                action = strategy.ActionGenerator(new HedgingActionGeneratorArguments<T>(primary, _context, attemptNumber, inner.Callback));
            }
            catch (Exception e)
            {
                // As in Polly: a failing generator is the attempt's outcome, and OnHedging is not raised.
                SetOutcome(new Outcome<T>(e), attemptNumber);
                Execution = Task.CompletedTask;
                return true;
            }

            if (action is null)
            {
                await ResetAsync().ConfigureAwait(false);
                return false;
            }
        }

        if (strategy.OnHedging is { } onHedging)
        {
            try
            {
                await onHedging(new OnHedgingArguments<T>(primary, _context, attemptNumber - 1)).ConfigureAwait(primary.ContinueOnCapturedContext);
            }
            catch
            {
                await ResetAsync().ConfigureAwait(false);
                throw;
            }
        }

        Execution = RunAsync(inner, action, primary.IsSynchronous, attemptNumber);
        return true;
    }

    private async Task RunAsync(InnerPipeline<T> inner, Func<ValueTask<Outcome<T>>>? action, bool isSynchronous, int attemptNumber)
    {
        Outcome<T> outcome;
        try
        {
            var task = action is not null
                ? action()
                : isSynchronous
                    ? new ValueTask<Outcome<T>>(Task.Run(() => inner.ExecuteAsync(_context).AsTask()))
                    : inner.ExecuteAsync(_context);
            outcome = await task.ConfigureAwait(_context.ContinueOnCapturedContext);
        }
        catch (Exception e)
        {
            outcome = new Outcome<T>(e);
        }

        SetOutcome(outcome, attemptNumber);
    }

    private void SetOutcome(Outcome<T> outcome, int attemptNumber)
    {
        Outcome = outcome;
        try
        {
            IsHandled = strategy.ShouldHandle(new HedgingPredicateArguments<T>(_context, outcome, attemptNumber));
        }
        catch (Exception e)
        {
            // A throwing predicate ends the hedging with its exception.
            Outcome = new Outcome<T>(e);
            IsHandled = false;
        }
    }

    /// <summary>Releases the attempt: disposes a result that was not accepted, returns the CTS, clears the context.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask ResetAsync()
    {
        _registration.Dispose();
        _registration = default;

        // Value-type results are skipped, as in the retry strategy: testing them for IDisposable would box.
        if (!IsAccepted && !typeof(T).IsValueType && Outcome.TryGetResult(out var result))
        {
            await DisposeHelper.TryDisposeSafeAsync(result, _context.IsSynchronous).ConfigureAwait(false);
        }

        if (_cancellation is { } cancellation)
        {
            // A cancelled source cannot be reset; the pool disposes it.
            strategy.CancellationPool.Return(cancellation);
        }

        _cancellation = null;
        Outcome = default;
        IsHandled = false;
        IsAccepted = false;
        Execution = Task.CompletedTask;
        _context.Reset();
    }
}
