using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>
/// The typed event handlers of one breaker, plus the code that raises them in transition order. Shared
/// by the execution hooks and by manual control.
/// </summary>
internal sealed class CircuitEventHandlers<T>(
    Func<OnCircuitOpenedArguments<T>, ValueTask>? onOpened,
    Func<OnCircuitClosedArguments<T>, ValueTask>? onClosed,
    Func<OnCircuitHalfOpenedArguments, ValueTask>? onHalfOpened)
{
    /// <summary>Whether any event is set; without one, transitions skip the sequencer altogether.</summary>
    public bool Any { get; } = onOpened is not null || onClosed is not null || onHalfOpened is not null;

    /// <summary>Raises the event of <paramref name="transition"/> once every earlier transition's event is done.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask RaiseAsync(CircuitController controller, Transition transition, object? source, Outcome<T> outcome, bool isManual, bool continueOnCapturedContext)
    {
        var generation = transition.Generation;
        await controller.Events.WaitTurnAsync(generation).ConfigureAwait(continueOnCapturedContext);
        try
        {
            switch (transition.State)
            {
                case CircuitState.Open or CircuitState.Isolated when onOpened is not null:
                    await onOpened(new OnCircuitOpenedArguments<T>(source, outcome, transition.BreakDuration, isManual)).ConfigureAwait(continueOnCapturedContext);
                    break;
                case CircuitState.Closed when onClosed is not null:
                    await onClosed(new OnCircuitClosedArguments<T>(source, outcome, isManual)).ConfigureAwait(continueOnCapturedContext);
                    break;
                case CircuitState.HalfOpen when onHalfOpened is not null:
                    await onHalfOpened(new OnCircuitHalfOpenedArguments((ExecutionFrame)source!)).ConfigureAwait(continueOnCapturedContext);
                    break;
            }
        }
        finally
        {
            controller.Events.Complete(generation);
        }
    }
}

/// <summary>Creates the controller of one breaker and attaches its state provider and manual control.</summary>
internal static class CircuitBreakerSetup
{
    public static CircuitController CreateController<TOptions>(CircuitBreakerStrategyOptions<TOptions> options, StrategyBuildContext context) => new(
        context.TimeProvider,
        options.FailureRatio,
        options.MinimumThroughput,
        options.SamplingDuration,
        options.BreakDuration,
        options.SlowCallDurationThreshold,
        options.SlowCallRatio,
        options.BreakDurationGenerator);

    /// <summary>
    /// Wires <see cref="CircuitBreakerStrategyOptions{TResult}.StateProvider"/> and
    /// <see cref="CircuitBreakerStrategyOptions{TResult}.ManualControl"/> to the controller. Manual events
    /// carry a default <typeparamref name="TOptions"/> result, as in Polly.
    /// </summary>
    /// <returns>
    /// The attachment, or null when there is neither: disposing it detaches the manual control and releases the state
    /// provider, so both can be attached to a breaker that replaces this one (a generated pipeline's UseTimeProvider or
    /// reload). M6 disposes it with the pipeline.
    /// </returns>
    public static IDisposable? Attach<TOptions>(CircuitBreakerStrategyOptions<TOptions> options, CircuitController controller)
    {
        var stateProvider = options.StateProvider;
        Func<CircuitState>? state = null;
        if (stateProvider is not null)
        {
            state = () => controller.State;
            stateProvider.Initialize(state);
        }

        IDisposable? manual = null;
        if (options.ManualControl is { } manualControl)
        {
            // OnHalfOpened is never raised manually, but it must count towards Any: every transition of a
            // controller either passes through its sequencer or none does, or the generation order has gaps.
            var handlers = new CircuitEventHandlers<TOptions>(options.OnOpened, options.OnClosed, options.OnHalfOpened);
            manual = manualControl.Initialize(
                context => RaiseManualAsync(handlers, controller, controller.Isolate(), context),
                context => RaiseManualAsync(handlers, controller, controller.Close(), context));
        }

        return stateProvider is null && manual is null ? null : new BreakerAttachment(stateProvider, state, manual);
    }

    private sealed class BreakerAttachment(CircuitBreakerStateProvider? stateProvider, Func<CircuitState>? state, IDisposable? manual) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (stateProvider is not null && state is not null)
            {
                stateProvider.Release(state);
            }

            manual?.Dispose();
        }
    }

    private static Task RaiseManualAsync<TOptions>(CircuitEventHandlers<TOptions> handlers, CircuitController controller, Transition transition, ResilienceContext context)
    {
        if (!transition.Happened || !handlers.Any)
        {
            return Task.CompletedTask;
        }

        return handlers.RaiseAsync(controller, transition, context, Outcome.FromResult<TOptions>(default), isManual: true, context.ContinueOnCapturedContext).AsTask();
    }
}

/// <summary>
/// The circuit breaker as interpreter hooks, over a <see cref="CircuitController"/> shared by every result
/// type of the pipeline. Slot use: <c>Long</c> = admission time in µs (only when slow calls are tracked).
/// </summary>
internal sealed class CircuitBreakerStrategy<T> : PipelineStrategy<T>
{
    private readonly CircuitController _controller;
    private readonly Func<CircuitBreakerPredicateArguments<T>, bool> _shouldHandle;
    private readonly CircuitEventHandlers<T> _handlers;
    private readonly bool _measure;

    /// <summary>The state-provider and manual-control attachment of a typed breaker, released by <see cref="Pipeline.GeneratedPipelineSupport.ReleaseAttachments{T}"/>.</summary>
    internal IDisposable? Attachment { get; init; }

    public CircuitBreakerStrategy(CircuitController controller, Func<CircuitBreakerPredicateArguments<T>, bool> shouldHandle, CircuitEventHandlers<T> handlers)
    {
        _controller = controller;
        _shouldHandle = shouldHandle;
        _handlers = handlers;
        _measure = controller.MeasuresDuration;
    }

    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        var admission = _controller.TryEnter(out var word);
        if (admission == Admission.Closed)
        {
            if (_measure)
            {
                frame.Slots[index].Long = _controller.Now();
            }

            return new(true);
        }

        if (admission == Admission.Rejected)
        {
            frame.Outcome = _controller.Reject<T>(word);
            return new(false);
        }

        // The probe: raise OnHalfOpened (awaited before the call runs, as in Polly), then time the call.
        if (_handlers.Any)
        {
            return EnterProbeAsync(frame, index, new Transition(word, word, TimeSpan.Zero));
        }

        if (_measure)
        {
            frame.Slots[index].Long = _controller.Now();
        }

        return new(true);
    }

    public override ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index)
    {
        var outcome = frame.Outcome;
        var start = _measure ? frame.Slots[index].Long : CircuitController.NotMeasured;

        var transition = _shouldHandle(new CircuitBreakerPredicateArguments<T>(frame, outcome))
            ? _controller.RecordFailure(ExceptionOf(outcome), start, frame)
            : _controller.RecordSuccess(start, frame);

        if (!transition.Happened || !_handlers.Any)
        {
            return new(false);
        }

        return RaiseAsync(frame, transition, outcome);
    }

    // The exception a later rejection carries as InnerException (Polly's _breakingException). A handled
    // rejection from an inner strategy is materialized here, on the failure path only.
    private static Exception? ExceptionOf(in Outcome<T> outcome) => outcome.IsRejected ? outcome.Exception : outcome.RawException;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> EnterProbeAsync(ExecutionFrame<T> frame, int index, Transition transition)
    {
        var continueOnCapturedContext = frame.ContinueOnCapturedContext;
        try
        {
            await _handlers.RaiseAsync(_controller, transition, frame, default, isManual: false, continueOnCapturedContext).ConfigureAwait(continueOnCapturedContext);
        }
        catch (Exception e)
        {
            // The probe will not run, so its exit never records an outcome. Polly leaves the circuit
            // half-open forever here; count the event's exception as a failed probe instead, which
            // re-opens the circuit, and let the exception become the outcome.
            var reopened = _controller.RecordFailure(e, CircuitController.NotMeasured, frame);
            if (reopened.Happened)
            {
                await _handlers.RaiseAsync(_controller, reopened, frame, Outcome.FromException<T>(e), isManual: false, continueOnCapturedContext).ConfigureAwait(continueOnCapturedContext);
            }

            throw;
        }

        if (_measure)
        {
            frame.Slots[index].Long = _controller.Now();
        }

        return true;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> RaiseAsync(ExecutionFrame<T> frame, Transition transition, Outcome<T> outcome)
    {
        await _handlers.RaiseAsync(_controller, transition, frame, outcome, isManual: false, frame.ContinueOnCapturedContext).ConfigureAwait(frame.ContinueOnCapturedContext);
        return false;
    }
}

/// <summary>
/// The circuit breaker for the non-generic builder: one controller per <c>Build()</c>, shared by every
/// result type; <see cref="CircuitBreakerStrategyOptions"/> is adapted to each result type.
/// </summary>
internal sealed class CircuitBreakerStrategyFactory : StrategyFactory
{
    private readonly CircuitBreakerStrategyOptions<object> _options;
    private readonly CircuitController _controller;

    public CircuitBreakerStrategyFactory(CircuitBreakerStrategyOptions<object> options, StrategyBuildContext context)
    {
        _options = options.Snapshot();
        _controller = CircuitBreakerSetup.CreateController(_options, context);
        Attachment = CircuitBreakerSetup.Attach(_options, _controller);
    }

    /// <summary>The state-provider and manual-control attachment; M6 disposes it when the pipeline is disposed or reloaded.</summary>
    public IDisposable? Attachment { get; }

    public override PipelineStrategy<TResult> Create<TResult>()
    {
        var source = _options;
        if (typeof(TResult) == typeof(object))
        {
            return (PipelineStrategy<TResult>)(object)new CircuitBreakerStrategy<object>(
                _controller,
                source.ShouldHandle,
                new CircuitEventHandlers<object>(source.OnOpened, source.OnClosed, source.OnHalfOpened));
        }

        var shouldHandle = source.ShouldHandle;
        var onOpened = source.OnOpened;
        var onClosed = source.OnClosed;
        return new CircuitBreakerStrategy<TResult>(
            _controller,
            ReferenceEquals(shouldHandle, CircuitBreakerStrategyOptions<object>.DefaultShouldHandle)
                ? CircuitBreakerStrategyOptions<TResult>.DefaultShouldHandle
                : args => shouldHandle(args.AsObject()),
            new CircuitEventHandlers<TResult>(
                onOpened is null ? null : args => onOpened(args.AsObject()),
                onClosed is null ? null : args => onClosed(args.AsObject()),
                source.OnHalfOpened));
    }
}
