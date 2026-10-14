using BenchmarkDotNet.Attributes;
using Polly;

namespace S5;

/// <summary>Timing for rejected calls. Run on a quiet machine: dotnet run -c Release -- --bench</summary>
[MemoryDiagnoser]
public class RejectionBenchmarks
{
    private static readonly Func<int, CancellationToken, ValueTask<int>> Cb = static (s, _) => ValueTask.FromResult(s);
    private static readonly Func<ResilienceContext, int, ValueTask<Polly.Outcome<int>>> PollyCb = static (_, s) => Polly.Outcome.FromResultAsValueTask(s);

    [Params(Scenario.CircuitOpen, Scenario.ConcurrencyLimited, Scenario.FixedWindowLimited)]
    public Scenario Scenario { get; set; }

    private OurPipeline _ours = null!;
    private ResiliencePipeline<int> _polly = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ours = Scenarios.Ours(Scenario);
        _polly = Scenarios.Polly(Scenario);
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<bool> Ours_TryExecuteAsync()
        => (await _ours.TryExecuteAsync(Cb, 42, default)).IsRejected;

    [Benchmark]
    public async ValueTask<bool> Ours_ExecuteAsync_FreshThrow()
    {
        try { await _ours.ExecuteAsync(Cb, 42, default); return false; }
        catch (ResilienceRejectedException) { return true; }
    }

    [Benchmark]
    public async ValueTask<bool> Ours_ExecuteAsync_CachedThrow()
    {
        try { await _ours.ExecuteAsyncCachedThrow(Cb, 42, default); return false; }
        catch (ResilienceRejectedException) { return true; }
    }

    [Benchmark]
    public async ValueTask<bool> Polly_ExecuteOutcomeAsync()
    {
        var ctx = ResilienceContextPool.Shared.Get();
        var o = await _polly.ExecuteOutcomeAsync(PollyCb, ctx, 42);
        ResilienceContextPool.Shared.Return(ctx);
        return o.Exception is not null;
    }

    [Benchmark]
    public async ValueTask<bool> Polly_ExecuteAsync_Throws()
    {
        try { await _polly.ExecuteAsync(Cb, 42, default); return false; }
        catch (Exception) { return true; }
    }
}
