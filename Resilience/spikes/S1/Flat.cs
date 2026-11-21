using System.Runtime.CompilerServices;

namespace S1;

/// <summary>
/// What the generator would emit for
/// [Fallback] [Timeout(10s)] [Retry(3)] [CircuitBreaker] [Timeout(2s)]: one async method, strategies
/// inlined, constants folded, no delegate hop between strategies.
/// </summary>
public sealed class FlatPipeline
{
    private readonly CircuitBreaker _breaker = Config.NewBreaker();

    public async ValueTask<int> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken cancellationToken)
    {
        Outcome<int> outcome;
        var outer = CtsPool.Rent(Config.OuterTimeout, cancellationToken);
        try
        {
            var attempt = 0;
            while (true)
            {
                if (!_breaker.TryEnter())
                {
                    outcome = new(Rejections.BrokenCircuit);
                }
                else
                {
                    var inner = CtsPool.Rent(Config.InnerTimeout, outer.Cts.Token);
                    try
                    {
                        outcome = new(await callback(state, inner.Cts.Token).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException) when (inner.Cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        outcome = new(Rejections.Timeout);
                    }
                    catch (Exception ex)
                    {
                        outcome = new(ex);
                    }
                    finally
                    {
                        CtsPool.Return(inner);
                    }
                    _breaker.Record(Predicates.ShouldHandle(outcome));
                }

                if (!Predicates.ShouldHandle(outcome) || attempt >= Config.MaxRetries) break;
                attempt++;
                var delay = Backoff.Exponential(attempt, Config.RetryDelay);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, outer.Cts.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            CtsPool.Return(outer);
        }

        return Predicates.ShouldHandle(outcome) ? 0 : outcome.Result; // fallback
    }
}

/// <summary>Identical body; only the method builder differs (also feeds S2).</summary>
public sealed class FlatPooledPipeline
{
    private readonly CircuitBreaker _breaker = Config.NewBreaker();

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken cancellationToken)
    {
        Outcome<int> outcome;
        var outer = CtsPool.Rent(Config.OuterTimeout, cancellationToken);
        try
        {
            var attempt = 0;
            while (true)
            {
                if (!_breaker.TryEnter())
                {
                    outcome = new(Rejections.BrokenCircuit);
                }
                else
                {
                    var inner = CtsPool.Rent(Config.InnerTimeout, outer.Cts.Token);
                    try
                    {
                        outcome = new(await callback(state, inner.Cts.Token).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException) when (inner.Cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        outcome = new(Rejections.Timeout);
                    }
                    catch (Exception ex)
                    {
                        outcome = new(ex);
                    }
                    finally
                    {
                        CtsPool.Return(inner);
                    }
                    _breaker.Record(Predicates.ShouldHandle(outcome));
                }

                if (!Predicates.ShouldHandle(outcome) || attempt >= Config.MaxRetries) break;
                attempt++;
                var delay = Backoff.Exponential(attempt, Config.RetryDelay);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, outer.Cts.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            CtsPool.Return(outer);
        }

        return Predicates.ShouldHandle(outcome) ? 0 : outcome.Result;
    }
}
