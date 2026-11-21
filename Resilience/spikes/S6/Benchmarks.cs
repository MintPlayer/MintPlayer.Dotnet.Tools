using BenchmarkDotNet.Attributes;

namespace S6;

/// <summary>
/// A vs B vs C on a synchronously completing callback (the path where a per-execution option read is most
/// visible). All three include the same two intrinsic CancelAfter timer operations and breaker lock.
/// Run: dotnet run -c Release -- --bench
/// </summary>
[MemoryDiagnoser]
public class ReloadBenchmarks
{
    private static readonly Func<int, CancellationToken, ValueTask<int>> s_callback = static (s, _) => new ValueTask<int>(s);

    [GlobalSetup]
    public void Setup()
    {
        // Give B and C a non-default snapshot (values equal to A's constants) so the benchmark reads a published
        // object, not the static initializer's.
        var o = new CatalogPipelineOptions();
        if (!CatalogPipeline.TryApply(o, out var e1)) throw new InvalidOperationException(e1);
        if (!StructPipeline.TryApply(o, out var e2)) throw new InvalidOperationException(e2);
    }

    [Benchmark(Baseline = true)]
    public int A_Constants() => Result(ConstPipeline.ExecuteAsync(s_callback, 42, CancellationToken.None));

    [Benchmark]
    public int B_ClassSnapshot() => Result(CatalogPipeline.ExecuteAsync(s_callback, 42, CancellationToken.None));

    [Benchmark]
    public int C_StructSnapshot() => Result(StructPipeline.ExecuteAsync(s_callback, 42, CancellationToken.None));

    private static int Result(ValueTask<int> t) => t.IsCompletedSuccessfully ? t.Result : t.AsTask().GetAwaiter().GetResult();
}
