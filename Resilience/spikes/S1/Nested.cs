namespace S1;

// Runtime-builder design: strategies are readonly structs nested by generic type, so the JIT specializes
// the whole chain (no delegate hop, no virtual call). Each layer has a synchronous fast path and an async
// slow path; the question is what the slow paths cost when the callback really suspends.

public interface IComponent
{
    ValueTask<Outcome<int>> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken ct);
}

public readonly struct Terminal : IComponent
{
    public ValueTask<Outcome<int>> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken ct)
    {
        try
        {
            var vt = callback(state, ct);
            if (vt.IsCompletedSuccessfully) return new(new Outcome<int>(vt.Result));
            return Slow(vt);
        }
        catch (Exception ex)
        {
            return new(new Outcome<int>(ex));
        }

        static async ValueTask<Outcome<int>> Slow(ValueTask<int> vt)
        {
            try { return new(await vt.ConfigureAwait(false)); }
            catch (Exception ex) { return new(ex); }
        }
    }
}

public readonly struct TimeoutLayer<TNext> : IComponent where TNext : struct, IComponent
{
    private readonly TimeSpan _timeout;
    private readonly TNext _next;
    public TimeoutLayer(TimeSpan timeout, TNext next) { _timeout = timeout; _next = next; }

    public ValueTask<Outcome<int>> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken ct)
    {
        var p = CtsPool.Rent(_timeout, ct);
        var vt = _next.ExecuteAsync(callback, state, p.Cts.Token);
        if (vt.IsCompletedSuccessfully)
        {
            var o = Map(vt.Result, p, ct);
            CtsPool.Return(p);
            return new(o);
        }
        return Slow(vt, p, ct);

        static async ValueTask<Outcome<int>> Slow(ValueTask<Outcome<int>> vt, PooledCts p, CancellationToken ct)
        {
            try { return Map(await vt.ConfigureAwait(false), p, ct); }
            finally { CtsPool.Return(p); }
        }
    }

    private static Outcome<int> Map(Outcome<int> o, PooledCts p, CancellationToken ct) =>
        o.Exception is OperationCanceledException && p.Cts.IsCancellationRequested && !ct.IsCancellationRequested
            ? new(Rejections.Timeout)
            : o;
}

public readonly struct RetryLayer<TNext> : IComponent where TNext : struct, IComponent
{
    private readonly int _maxRetries;
    private readonly TimeSpan _delay;
    private readonly TNext _next;
    public RetryLayer(int maxRetries, TimeSpan delay, TNext next) { _maxRetries = maxRetries; _delay = delay; _next = next; }

    public ValueTask<Outcome<int>> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken ct)
    {
        var vt = _next.ExecuteAsync(callback, state, ct);
        if (vt.IsCompletedSuccessfully && (!Predicates.ShouldHandle(vt.Result) || _maxRetries == 0)) return vt;
        return Loop(_next, _maxRetries, _delay, vt, callback, state, ct);

        static async ValueTask<Outcome<int>> Loop(TNext next, int max, TimeSpan baseDelay, ValueTask<Outcome<int>> pending,
            Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken ct)
        {
            var attempt = 0;
            while (true)
            {
                var o = await pending.ConfigureAwait(false);
                if (!Predicates.ShouldHandle(o) || attempt >= max) return o;
                attempt++;
                var delay = Backoff.Exponential(attempt, baseDelay);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct).ConfigureAwait(false);
                pending = next.ExecuteAsync(callback, state, ct);
            }
        }
    }
}

public readonly struct CircuitBreakerLayer<TNext> : IComponent where TNext : struct, IComponent
{
    private readonly CircuitBreaker _breaker;
    private readonly TNext _next;
    public CircuitBreakerLayer(CircuitBreaker breaker, TNext next) { _breaker = breaker; _next = next; }

    public ValueTask<Outcome<int>> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken ct)
    {
        if (!_breaker.TryEnter()) return new(new Outcome<int>(Rejections.BrokenCircuit));
        var vt = _next.ExecuteAsync(callback, state, ct);
        if (vt.IsCompletedSuccessfully)
        {
            _breaker.Record(Predicates.ShouldHandle(vt.Result));
            return vt;
        }
        return Slow(vt, _breaker);

        static async ValueTask<Outcome<int>> Slow(ValueTask<Outcome<int>> vt, CircuitBreaker breaker)
        {
            var o = await vt.ConfigureAwait(false);
            breaker.Record(Predicates.ShouldHandle(o));
            return o;
        }
    }
}

public readonly struct FallbackLayer<TNext> : IComponent where TNext : struct, IComponent
{
    private readonly int _fallback;
    private readonly TNext _next;
    public FallbackLayer(int fallback, TNext next) { _fallback = fallback; _next = next; }

    public ValueTask<Outcome<int>> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken ct)
    {
        var vt = _next.ExecuteAsync(callback, state, ct);
        if (vt.IsCompletedSuccessfully)
            return new(Predicates.ShouldHandle(vt.Result) ? new Outcome<int>(_fallback) : vt.Result);
        return Slow(vt, _fallback);

        static async ValueTask<Outcome<int>> Slow(ValueTask<Outcome<int>> vt, int fallback)
        {
            var o = await vt.ConfigureAwait(false);
            return Predicates.ShouldHandle(o) ? new(fallback) : o;
        }
    }
}

public sealed class NestedPipeline<TChain> where TChain : struct, IComponent
{
    private readonly TChain _chain;
    public NestedPipeline(TChain chain) => _chain = chain;

    public ValueTask<int> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken ct)
    {
        var vt = _chain.ExecuteAsync(callback, state, ct);
        if (vt.IsCompletedSuccessfully) return new(Unwrap(vt.Result));
        return Slow(vt);

        static async ValueTask<int> Slow(ValueTask<Outcome<int>> vt) => Unwrap(await vt.ConfigureAwait(false));
    }

    private static int Unwrap(Outcome<int> o) => o.Exception is { } ex ? throw ex : o.Result;
}

public static class Nested
{
    // What a builder chain `.AddFallback().AddTimeout().AddRetry().AddCircuitBreaker().AddTimeout()` would produce.
    public static NestedPipeline<FallbackLayer<TimeoutLayer<RetryLayer<CircuitBreakerLayer<TimeoutLayer<Terminal>>>>>> Create() =>
        new(new(0,
            new(Config.OuterTimeout,
                new(Config.MaxRetries, Config.RetryDelay,
                    new(Config.NewBreaker(),
                        new(Config.InnerTimeout, default(Terminal)))))));
}
