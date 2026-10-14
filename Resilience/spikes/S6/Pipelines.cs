using System.Runtime.CompilerServices;
using S1;

namespace S6;

// Three copies of the S1 flat pipeline, minus fallback-as-strategy (kept as the final `? 0 :`):
//   [Timeout(10s, "Total")] [Retry(3)] [CircuitBreaker] [Timeout(2s, "Attempt")]
// Bodies are identical except for WHERE the numbers come from. All use the pooling builder (S2 default-on).

/// <summary>A — constants folded in (static readonly → JIT constants at tier 1). No reload.</summary>
public static class ConstPipeline
{
    private static readonly Breaker s_breaker = new();
    private static readonly TimeSpan OuterTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InnerTimeout = TimeSpan.FromSeconds(2);
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.Zero;
    private static readonly BreakerSettings BreakerSettings = new(0.9, 10, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken cancellationToken)
    {
        Outcome<int> outcome;
        var outer = CtsPool.Rent(OuterTimeout, cancellationToken);
        try
        {
            var attempt = 0;
            while (true)
            {
                if (!s_breaker.TryEnter())
                {
                    outcome = new(Rejections.BrokenCircuit);
                }
                else
                {
                    var inner = CtsPool.Rent(InnerTimeout, outer.Cts.Token);
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
                    s_breaker.Record(Predicates.ShouldHandle(outcome), in BreakerSettings);
                }

                if (!Predicates.ShouldHandle(outcome) || attempt >= MaxRetries) break;
                attempt++;
                var delay = Backoff.Exponential(attempt, RetryDelay);
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

/// <summary>
/// B — what the generator emits for <c>[ResiliencePipeline(Reloadable = true)]</c>: a static snapshot reference,
/// read ONCE at entry (one load), then plain field reads. The whole execution — every retry, every attempt's
/// timeout — uses the snapshot it started with, so an in-flight execution never sees a half-applied reload.
/// Circuit-breaker HEALTH state lives outside the snapshot and survives reloads.
/// </summary>
public sealed partial class CatalogPipeline : IReloadableResiliencePipeline<CatalogPipelineOptions>
{
    private CatalogPipeline() { }

    private static readonly Breaker s_breaker = new();
    private static long s_version;
    // Initialised from the attribute values, so the pipeline works before (or without) DI.
    private static CatalogSnapshot s_current = new(new CatalogPipelineOptions(), 0);

    public static CatalogSnapshot Current => Volatile.Read(ref s_current);
    internal static Breaker BreakerForTests => s_breaker;

    public static string DefaultSectionPath => "Resilience:CatalogPipeline";

    public static void AddServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, string? sectionPath) =>
        services.AddReloadablePipeline<CatalogPipeline, CatalogPipelineOptions>(sectionPath);

    public static bool TryApply(CatalogPipelineOptions options, out string? error)
    {
        error = options.Validate();
        if (error is not null) return false; // keep serving the previous snapshot
        Volatile.Write(ref s_current, new CatalogSnapshot(options, Interlocked.Increment(ref s_version)));
        return true;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken cancellationToken)
    {
        var o = Volatile.Read(ref s_current); // the only extra work vs A
        Outcome<int> outcome;
        var outer = CtsPool.Rent(o.OuterTimeout, cancellationToken);
        try
        {
            var attempt = 0;
            while (true)
            {
                if (!s_breaker.TryEnter())
                {
                    outcome = new(Rejections.BrokenCircuit);
                }
                else
                {
                    var inner = CtsPool.Rent(o.InnerTimeout, outer.Cts.Token);
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
                    s_breaker.Record(Predicates.ShouldHandle(outcome), in o.Breaker);
                }

                if (!Predicates.ShouldHandle(outcome) || attempt >= o.MaxRetries) break;
                attempt++;
                var delay = Backoff.Exponential(attempt, o.RetryDelay);
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

/// <summary>
/// C — readonly struct snapshot copied into the execution's frame (state machine) at entry. Removes the pointer
/// chase on every field use at the cost of a ~64-byte copy per execution and a bigger state machine.
/// </summary>
public sealed class StructPipeline : IReloadableResiliencePipeline<CatalogPipelineOptions>
{
    private StructPipeline() { }

    private sealed class Holder(CatalogValues values) { public readonly CatalogValues Values = values; }

    public static string DefaultSectionPath => "Resilience:CatalogPipeline";

    public static void AddServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, string? sectionPath) =>
        services.AddReloadablePipeline<StructPipeline, CatalogPipelineOptions>(sectionPath);

    public static int CurrentMaxRetries => Volatile.Read(ref s_current).Values.MaxRetries;

    private static readonly Breaker s_breaker = new();
    private static Holder s_current = new(new CatalogValues(new CatalogPipelineOptions()));

    public static bool TryApply(CatalogPipelineOptions options, out string? error)
    {
        error = options.Validate();
        if (error is not null) return false;
        Volatile.Write(ref s_current, new Holder(new CatalogValues(options)));
        return true;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<int>> callback, TState state, CancellationToken cancellationToken)
    {
        var o = Volatile.Read(ref s_current).Values; // struct copy
        Outcome<int> outcome;
        var outer = CtsPool.Rent(o.OuterTimeout, cancellationToken);
        try
        {
            var attempt = 0;
            while (true)
            {
                if (!s_breaker.TryEnter())
                {
                    outcome = new(Rejections.BrokenCircuit);
                }
                else
                {
                    var inner = CtsPool.Rent(o.InnerTimeout, outer.Cts.Token);
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
                    s_breaker.Record(Predicates.ShouldHandle(outcome), in o.Breaker);
                }

                if (!Predicates.ShouldHandle(outcome) || attempt >= o.MaxRetries) break;
                attempt++;
                var delay = Backoff.Exponential(attempt, o.RetryDelay);
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
