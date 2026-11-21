using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;

namespace S1;

public sealed class Flip { public int Calls; }

public static class Callbacks
{
    public static readonly Func<Flip, CancellationToken, ValueTask<int>> Sync =
        static (_, _) => new ValueTask<int>(42);

    public static readonly Func<Flip, CancellationToken, ValueTask<int>> Async =
        static async (_, _) => { await Task.Yield(); return 42; };

    // Odd calls fail (-1, a handled result), even calls succeed: exactly one retry per execution.
    public static readonly Func<Flip, CancellationToken, ValueTask<int>> OneRetry =
        static (s, _) => new ValueTask<int>((++s.Calls & 1) == 1 ? -1 : 42);

    public static readonly Func<Flip, CancellationToken, ValueTask<int>> AlwaysFail =
        static (s, _) => { s.Calls++; return new ValueTask<int>(-1); };
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
public class PipelineBenchmarks
{
    private readonly ResiliencePipeline<int> _polly = PollySetup.Create();
    private readonly FlatPipeline _flat = new();
    private readonly FlatPooledPipeline _flatPooled = new();
    private readonly NestedPipeline<FallbackLayer<TimeoutLayer<RetryLayer<CircuitBreakerLayer<TimeoutLayer<Terminal>>>>>> _nested = Nested.Create();
    private readonly Flip _state = new();

    // ---- Sync: callback completes synchronously, happy path (Polly's own zero-alloc scenario) ----
    [Benchmark, BenchmarkCategory("Sync")] public ValueTask<int> Sync_Direct() => Callbacks.Sync(_state, CancellationToken.None);
    [Benchmark(Baseline = true), BenchmarkCategory("Sync")] public ValueTask<int> Sync_Polly() => _polly.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("Sync")] public ValueTask<int> Sync_Flat() => _flat.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("Sync")] public ValueTask<int> Sync_FlatPooled() => _flatPooled.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("Sync")] public ValueTask<int> Sync_Nested() => _nested.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);

    // ---- Async: callback really suspends (the path production I/O takes) ----
    [Benchmark, BenchmarkCategory("Async")] public async Task<int> Async_Direct() => await Callbacks.Async(_state, CancellationToken.None);
    [Benchmark(Baseline = true), BenchmarkCategory("Async")] public async Task<int> Async_Polly() => await _polly.ExecuteAsync(Callbacks.Async, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("Async")] public async Task<int> Async_Flat() => await _flat.ExecuteAsync(Callbacks.Async, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("Async")] public async Task<int> Async_FlatPooled() => await _flatPooled.ExecuteAsync(Callbacks.Async, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("Async")] public async Task<int> Async_Nested() => await _nested.ExecuteAsync(Callbacks.Async, _state, CancellationToken.None);

    // ---- OneRetry: first attempt returns a handled result, second succeeds (zero delay) ----
    [Benchmark, BenchmarkCategory("OneRetry")] public async ValueTask<int> OneRetry_Direct() { await Callbacks.OneRetry(_state, CancellationToken.None); return await Callbacks.OneRetry(_state, CancellationToken.None); }
    [Benchmark(Baseline = true), BenchmarkCategory("OneRetry")] public ValueTask<int> OneRetry_Polly() => _polly.ExecuteAsync(Callbacks.OneRetry, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("OneRetry")] public ValueTask<int> OneRetry_Flat() => _flat.ExecuteAsync(Callbacks.OneRetry, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("OneRetry")] public ValueTask<int> OneRetry_FlatPooled() => _flatPooled.ExecuteAsync(Callbacks.OneRetry, _state, CancellationToken.None);
    [Benchmark, BenchmarkCategory("OneRetry")] public ValueTask<int> OneRetry_Nested() => _nested.ExecuteAsync(Callbacks.OneRetry, _state, CancellationToken.None);
}
