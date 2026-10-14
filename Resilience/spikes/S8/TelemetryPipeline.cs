using System.Diagnostics;
using System.Runtime.CompilerServices;
using S1;

namespace S8;

public enum BreakerTransition : byte { None, Opened, Closed }

/// <summary>S1's lock-based breaker, reporting its state transitions so they can be telemetered.</summary>
public sealed class ObservedBreaker(double failureRatio, int minimumThroughput, TimeSpan sampling, TimeSpan breakDuration, TimeProvider time)
{
    private readonly object _lock = new();
    private int _state; // 0 closed, 1 open, 2 half-open
    private DateTimeOffset _blockedUntil;
    private DateTimeOffset _windowStart = time.GetUtcNow();
    private int _successes, _failures;

    public TimeSpan BreakDuration => breakDuration;

    public bool TryEnter(out bool halfOpened)
    {
        halfOpened = false;
        lock (_lock)
        {
            if (_state == 0) return true;
            if (_state == 1 && time.GetUtcNow() >= _blockedUntil) { _state = 2; halfOpened = true; return true; }
            return false;
        }
    }

    public BreakerTransition Record(bool failure)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (_state == 2)
            {
                if (failure) { _state = 1; _blockedUntil = now + breakDuration; return BreakerTransition.Opened; }
                _state = 0; ResetWindow(now); return BreakerTransition.Closed;
            }
            if (now - _windowStart >= sampling) ResetWindow(now);
            if (failure) _failures++; else _successes++;
            var total = _failures + _successes;
            if (_state == 0 && total >= minimumThroughput && (double)_failures / total >= failureRatio)
            {
                _state = 1;
                _blockedUntil = now + breakDuration;
                return BreakerTransition.Opened;
            }
            return BreakerTransition.None;
        }
    }

    private void ResetWindow(DateTimeOffset now) { _windowStart = now; _successes = 0; _failures = 0; }
}

/// <summary>
/// S1's FlatPooledPipeline with telemetry woven in. The telemetry reference is nullable: that is the runtime
/// builder's shape (one null check per call site when telemetry is off). A generated pipeline with telemetry
/// on emits the same calls without the null checks; with telemetry off it is S1's FlatPooledPipeline exactly.
/// </summary>
public sealed class FlatTelemetryPipeline(PipelineTelemetry? telemetry, TimeProvider? time = null, TimeSpan? attemptTimeout = null)
{
    private readonly ObservedBreaker _breaker = new(Config.FailureRatio, Config.MinimumThroughput, Config.Sampling, Config.Break, time ?? TimeProvider.System);
    private readonly PipelineTelemetry? _t = telemetry;
    private readonly TimeSpan _innerTimeout = attemptTimeout ?? Config.InnerTimeout;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken cancellationToken)
    {
        var t = _t;
        Activity? activity = null;
        long pipelineStart = 0;
        if (t is not null) pipelineStart = t.PipelineExecuting(out activity);

        Outcome<int> outcome;
        var outer = CtsPool.Rent(Config.OuterTimeout, cancellationToken);
        try
        {
            var attempt = 0;
            while (true)
            {
                // The attempt is timed around breaker + attempt timeout + callback, as Polly's retry times its inner pipeline.
                long attemptStart = t is not null ? Stopwatch.GetTimestamp() : 0;
                if (!_breaker.TryEnter(out var halfOpened))
                {
                    outcome = new(Rejections.BrokenCircuit);
                    t?.OnCircuitRejected();
                }
                else
                {
                    if (halfOpened) t?.OnCircuitHalfOpened();
                    var inner = CtsPool.Rent(_innerTimeout, outer.Cts.Token);
                    try
                    {
                        outcome = new(await callback(state, inner.Cts.Token).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException) when (inner.Cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        outcome = new(Rejections.Timeout);
                        t?.OnTimeout(_innerTimeout);
                    }
                    catch (Exception ex)
                    {
                        outcome = new(ex);
                    }
                    finally
                    {
                        CtsPool.Return(inner);
                    }
                    var transition = _breaker.Record(Predicates.ShouldHandle(outcome));
                    if (transition != BreakerTransition.None && t is not null)
                    {
                        if (transition == BreakerTransition.Opened) t.OnCircuitOpened(_breaker.BreakDuration);
                        else t.OnCircuitClosed();
                    }
                }

                var handled = Predicates.ShouldHandle(outcome);
                var isLast = attempt >= Config.MaxRetries;
                t?.ExecutionAttempt(attempt, handled, isLast, attemptStart, outcome.Exception);

                if (!handled || isLast) break;
                attempt++;
                var delay = Backoff.Exponential(attempt, Config.RetryDelay);
                t?.OnRetry(attempt - 1, delay, outcome.Exception);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, outer.Cts.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            CtsPool.Return(outer);
        }

        var result = outcome.Result;
        if (Predicates.ShouldHandle(outcome))
        {
            t?.OnFallback(outcome.Exception);
            result = 0;
        }
        // Like Polly, PipelineExecuted reports the outcome after the fallback: a fallback success is not "handled".
        t?.PipelineExecuted(pipelineStart, handled: false, exception: null, activity);
        return result;
    }
}
