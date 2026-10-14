using BenchmarkDotNet.Attributes;
using Polly;

namespace S4;

/// <summary>
/// Closed-state TryEnter + Record throughput under contention. Every invocation performs TotalOps
/// operations split evenly over <see cref="Threads"/> threads, so Mean = wall time per operation
/// (lower = more aggregate throughput). Polly is the whole pipeline (context pooling, strategy
/// dispatch) around its lock-based controller; "Locked" is Polly's controller logic alone under the
/// same lock, which is the apples-to-apples comparison for the lock-vs-CAS question.
/// </summary>
[MemoryDiagnoser]
public class ClosedStateBenchmarks
{
    private const int TotalOps = 1 << 18;

    [Params(1, 4, 16)] public int Threads;

    /// <summary>0 = all successes; 20 = every 20th call fails (5 %, below the 50 % ratio, never trips).</summary>
    [Params(0, 20)] public int FailureEvery;

    private ResiliencePipeline<int> _polly = null!;
    private LockedCircuitBreaker _locked = null!;
    private LockFreeCircuitBreaker _free = null!;

    [GlobalSetup]
    public void Setup()
    {
        ThreadPool.SetMinThreads(64, 64);
        // Polly defaults except the ratio: 30 s sampling (3 s windows), throughput 100, break 5 s.
        var o = new CbOptions(0.5, 100, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        _polly = new PollyBreaker(o, TimeProvider.System).Pipeline;
        _locked = new LockedCircuitBreaker(o, TimeProvider.System);
        _free = new LockFreeCircuitBreaker(o, TimeProvider.System);
    }

    private void Run(Action<int> perThread)
    {
        var n = TotalOps / Threads;
        if (Threads == 1) { perThread(n); return; }
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ => perThread(n));
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = TotalOps)]
    public void Polly() => Run(n =>
    {
        var p = _polly; var every = FailureEvery;
        for (var i = 0; i < n; i++)
            p.Execute(static v => v, every != 0 && i % every == 0 ? -1 : 1);
    });

    [Benchmark(OperationsPerInvoke = TotalOps)]
    public void Locked() => Run(n =>
    {
        var b = _locked; var every = FailureEvery;
        for (var i = 0; i < n; i++)
        {
            if (b.TryEnter() == Admission.Rejected) continue;
            if (every != 0 && i % every == 0) b.RecordFailure(); else b.RecordSuccess();
        }
    });

    [Benchmark(OperationsPerInvoke = TotalOps)]
    public void LockFree() => Run(n =>
    {
        var b = _free; var every = FailureEvery;
        for (var i = 0; i < n; i++)
        {
            if (b.TryEnter() == Admission.Rejected) continue;
            if (every != 0 && i % every == 0) b.RecordFailure(); else b.RecordSuccess();
        }
    });

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_free.State != CbState.Closed || _locked.State != CbState.Closed)
            throw new InvalidOperationException("breaker tripped during a closed-state benchmark");
    }
}
