using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Polly;
using S1;

namespace S8;

/// <summary>
/// Telemetry overhead = (X_On - X_Off) within one Sink row, for X = Ours and Polly.
/// The S8 target is Ours_On - Ours_Off &lt;= 120 ns at Sink = MeterNoOp (Polly's own ~230 ns setup:
/// a no-op MeterListener, NullLoggerFactory). Off methods ignore the sink (nothing to listen to), they
/// are repeated per row so each row is a self-contained comparison.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
public class HappyPathTelemetryBenchmarks
{
    private IDisposable? _sink;
    private FlatPooledPipeline _oursOff = null!;
    private FlatTelemetryPipeline _oursRuntimeOff = null!, _oursOn = null!, _oursOnLean = null!;
    private ResiliencePipeline<int> _pollyOff = null!, _pollyOn = null!;
    private readonly Flip _state = new();

    [Params(Sink.None, Sink.MeterNoOp, Sink.OTel, Sink.MeterNoOpLogInfo)]
    public Sink Sink { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var lf = Sinks.LoggerFactoryFor(Sink);
        _oursOff = new FlatPooledPipeline();
        _oursRuntimeOff = new FlatTelemetryPipeline(null);
        _oursOn = new FlatTelemetryPipeline(Pipelines.OurTelemetry(lf));
        _oursOnLean = new FlatTelemetryPipeline(Pipelines.OurTelemetry(lf, emitExecutingEvent: false));
        _pollyOff = Pipelines.Polly(null);
        _pollyOn = Pipelines.Polly(lf);
        _sink = Sinks.Attach(Sink);
    }

    [GlobalCleanup] public void Cleanup() => _sink?.Dispose();

    [Benchmark] public ValueTask<int> Ours_Off() => _oursOff.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
    [Benchmark] public ValueTask<int> Ours_RuntimeOff() => _oursRuntimeOff.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
    [Benchmark] public ValueTask<int> Ours_On() => _oursOn.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
    [Benchmark] public ValueTask<int> Ours_On_Lean() => _oursOnLean.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
    [Benchmark(Baseline = true)] public ValueTask<int> Polly_Off() => _pollyOff.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
    [Benchmark] public ValueTask<int> Polly_On() => _pollyOn.ExecuteAsync(Callbacks.Sync, _state, CancellationToken.None);
}

/// <summary>One handled attempt + one retry per execution: 2 attempt records, 1 OnRetry event, 1 duration.</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
public class OneRetryTelemetryBenchmarks
{
    private IDisposable? _sink;
    private FlatPooledPipeline _oursOff = null!;
    private FlatTelemetryPipeline _oursOn = null!;
    private ResiliencePipeline<int> _pollyOff = null!, _pollyOn = null!;
    private readonly Flip _state = new();

    [GlobalSetup]
    public void Setup()
    {
        _oursOff = new FlatPooledPipeline();
        _oursOn = new FlatTelemetryPipeline(Pipelines.OurTelemetry(Sinks.LoggerFactoryFor(Sink.MeterNoOp)));
        _pollyOff = Pipelines.Polly(null);
        _pollyOn = Pipelines.Polly(Sinks.LoggerFactoryFor(Sink.MeterNoOp));
        _sink = Sinks.Attach(Sink.MeterNoOp);
    }

    [GlobalCleanup] public void Cleanup() => _sink?.Dispose();

    [Benchmark] public ValueTask<int> Ours_Off() => _oursOff.ExecuteAsync(Callbacks.OneRetry, _state, CancellationToken.None);
    [Benchmark] public ValueTask<int> Ours_On() => _oursOn.ExecuteAsync(Callbacks.OneRetry, _state, CancellationToken.None);
    [Benchmark(Baseline = true)] public ValueTask<int> Polly_Off() => _pollyOff.ExecuteAsync(Callbacks.OneRetry, _state, CancellationToken.None);
    [Benchmark] public ValueTask<int> Polly_On() => _pollyOn.ExecuteAsync(Callbacks.OneRetry, _state, CancellationToken.None);
}
