using System.Diagnostics.Metrics;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Logging;
using Polly;
using S1;
using S8;

if (args.Contains("--bench"))
{
    var rest = args.Where(a => a != "--bench").ToArray();
    if (!rest.Contains("--filter") && !rest.Contains("--list")) rest = [.. rest, "--filter", "*"];
    BenchmarkSwitcher.FromTypes([typeof(HappyPathTelemetryBenchmarks), typeof(OneRetryTelemetryBenchmarks)]).Run(rest);
    return 0;
}

// ======================================================================= Part 1: which events fire?
Console.WriteLine("Part 1 - telemetry emitted per execution (metrics: instrument [event.name/severity attempt]; logs: category id:name level)");

var capture = new Capture();
using (capture.Start())
{
    using var lf = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));

    var impls = new (string name, Func<Func<Func<Flip, CancellationToken, ValueTask<int>>, Flip, ValueTask<int>>> make)[]
    {
        ("Ours", () => { var p = new FlatTelemetryPipeline(Pipelines.OurTelemetry(lf)); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
        ("Ours (PollyCompatible names)", () => { var p = new FlatTelemetryPipeline(Pipelines.OurTelemetry(lf, names: TelemetryNames.PollyCompatible)); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
        ("Polly 8.8.0", () => { var p = Pipelines.Polly(lf); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
    };

    foreach (var (scenario, cb) in new[] { ("happy path", Callbacks.Sync), ("one retry", Callbacks.OneRetry), ("always fail", Callbacks.AlwaysFail) })
    {
        foreach (var (name, make) in impls)
        {
            if (name.Contains("Compatible") && scenario != "happy path") continue;
            var exec = make();
            var flip = new Flip();
            capture.Clear();
            var r = await exec(cb, flip);
            Console.WriteLine($"  [{scenario}] {name}: result={r}, calls={flip.Calls}");
            capture.Print("    ");
        }
    }

    // Our breaker / timeout events (the parts the scenarios above don't reach).
    var time = new ManualTime();
    var ours = new FlatTelemetryPipeline(Pipelines.OurTelemetry(lf), time);
    var f = new Flip();
    capture.Clear();
    for (var i = 0; i < 3; i++) await ours.ExecuteAsync(Callbacks.AlwaysFail, f, CancellationToken.None);
    Console.WriteLine($"  [breaker opens] Ours: 3 x always-fail = {f.Calls} failures (opens at 10, then rejections):");
    capture.PrintCounts("    ");
    capture.Clear();
    time.Advance(Config.Break + TimeSpan.FromSeconds(1));
    await ours.ExecuteAsync(Callbacks.Sync, f, CancellationToken.None);
    Console.WriteLine("  [break elapsed, one success] Ours:");
    capture.PrintCounts("    ");

    var timing = new FlatTelemetryPipeline(Pipelines.OurTelemetry(lf), attemptTimeout: TimeSpan.FromMilliseconds(20));
    capture.Clear();
    var tr = await timing.ExecuteAsync(Callbacks.Hang, f, CancellationToken.None);
    Console.WriteLine($"  [attempt timeout 20 ms, callback hangs] Ours: result={tr}");
    capture.PrintCounts("    ");
}

// ======================================================================= Part 2: allocations
const int Warmup = 20_000, Ops = 200_000;

static async Task Loop(Func<ValueTask<int>> op, int n)
{
    for (var i = 0; i < n; i++)
        if (await op() is not (42 or 0 or -1)) throw new InvalidOperationException("wrong result");
}

static async Task<double> BytesPerOp(Func<ValueTask<int>> op)
{
    await Loop(op, Warmup);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var before = GC.GetTotalAllocatedBytes(precise: true);
    await Loop(op, Ops);
    return (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)Ops;
}

Console.WriteLine();
Console.WriteLine($"Part 2 - bytes/op over {Ops:N0} ops after {Warmup:N0} warm-up (Async includes the callback's own box: see 'callback only')");

var variants = new (string name, Func<Sink, Func<Func<Flip, CancellationToken, ValueTask<int>>, Flip, ValueTask<int>>> make)[]
{
    ("callback only", _ => (cb, s) => cb(s, CancellationToken.None)),
    ("Ours off (generated: compiled out)", _ => { var p = new FlatPooledPipeline(); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
    ("Ours runtime, telemetry = null", _ => { var p = new FlatTelemetryPipeline(null); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
    ("Ours on", sink => { var p = new FlatTelemetryPipeline(Pipelines.OurTelemetry(Sinks.LoggerFactoryFor(sink))); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
    ("Polly off", _ => { var p = Pipelines.Polly(null); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
    ("Polly on", sink => { var p = Pipelines.Polly(Sinks.LoggerFactoryFor(sink)); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
};
var sinks = Enum.GetValues<Sink>();
var workloads = new[] { ("Sync", Callbacks.Sync), ("OneRetry", Callbacks.OneRetry), ("Async", Callbacks.Async) };

Console.Write($"  {"variant",-36} {"workload",-9}");
foreach (var s in sinks) Console.Write($" {s,17}");
Console.WriteLine();
foreach (var (wname, cb) in workloads)
{
    foreach (var (vname, make) in variants)
    {
        Console.Write($"  {vname,-36} {wname,-9}");
        foreach (var sink in sinks)
        {
            var exec = make(sink);
            var flip = new Flip();
            double bytes;
            using (Sinks.Attach(sink)) bytes = await BytesPerOp(() => exec(cb, flip));
            Console.Write($" {bytes,17:F1}");
        }
        Console.WriteLine();
    }
}

// Open circuit: every attempt is rejected (4 rejections, 4 attempts with error.type, 3 OnRetry, 1 OnFallback per call).
Console.WriteLine();
Console.WriteLine("  Open circuit (always-fail callback until open, then measured with the circuit held open):");
foreach (var sink in new[] { Sink.None, Sink.MeterNoOp, Sink.OTel })
{
    var ours = new FlatTelemetryPipeline(Pipelines.OurTelemetry(Sinks.LoggerFactoryFor(sink)), new ManualTime());
    var polly = Pipelines.Polly(Sinks.LoggerFactoryFor(sink));
    var pollyOff = Pipelines.Polly(null);
    var flip = new Flip();
    for (var i = 0; i < 5; i++)
    {
        await ours.ExecuteAsync(Callbacks.AlwaysFail, flip, CancellationToken.None);
        await polly.ExecuteAsync(Callbacks.AlwaysFail, flip, CancellationToken.None);
        await pollyOff.ExecuteAsync(Callbacks.AlwaysFail, flip, CancellationToken.None);
    }
    var before = flip.Calls;
    double o, p, q;
    using (Sinks.Attach(sink))
    {
        o = await BytesPerOp(() => ours.ExecuteAsync(Callbacks.AlwaysFail, flip, CancellationToken.None));
        p = await BytesPerOp(() => polly.ExecuteAsync(Callbacks.AlwaysFail, flip, CancellationToken.None));
        q = await BytesPerOp(() => pollyOff.ExecuteAsync(Callbacks.AlwaysFail, flip, CancellationToken.None));
    }
    Console.WriteLine($"    {sink,-10} ours on {o,8:F1}   polly on {p,8:F1}   polly off {q,8:F1}   (callback calls while open: {flip.Calls - before})");
}
return 0;

/// <summary>Records every measurement (both meters) and every log event, for Part 1.</summary>
sealed class Capture : ILoggerProvider
{
    private readonly List<string> _lines = [];
    private readonly object _lock = new();

    public IDisposable Start()
    {
        var l = new MeterListener
        {
            InstrumentPublished = (i, ml) => { if (Sinks.Meters.Contains(i.Meter.Name)) ml.EnableMeasurementEvents(i); },
        };
        l.SetMeasurementEventCallback<int>((i, v, tags, _) => Add(i, tags));
        l.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, tags));
        l.Start();
        return l;
    }

    private void Add(Instrument i, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string ev = "", sev = "", att = "", err = "", keys = "";
        foreach (var t in tags)
        {
            keys += (keys.Length > 0 ? "," : "") + t.Key;
            switch (t.Key)
            {
                case Tags.EventName: ev = (string)t.Value!; break;
                case Tags.EventSeverity: sev = (string)t.Value!; break;
                case Tags.AttemptNumber: att += $" #{t.Value}"; break;
                case Tags.AttemptHandled: att += $" handled={t.Value}"; break;
                case "error.type" or "exception.type": err = $" {t.Key}={t.Value}"; break;
            }
        }
        lock (_lock) _lines.Add($"metric {i.Meter.Name}/{i.Name} ({i.Unit}) [{ev}/{sev}{att}{err}] tags={keys}");
    }

    public void Clear() { lock (_lock) _lines.Clear(); }

    public void Print(string indent) { lock (_lock) foreach (var l in _lines) Console.WriteLine(indent + l); }

    public void PrintCounts(string indent)
    {
        lock (_lock)
            foreach (var g in _lines.Select(l => l.Split(" tags=")[0]).GroupBy(l => l))
                Console.WriteLine($"{indent}{g.Count(),3} x {g.Key}");
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName) => new CategoryLogger(this, categoryName);
    void IDisposable.Dispose() { }

    private sealed class CategoryLogger(Capture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lock) owner._lines.Add($"log    {category} {eventId.Id}:{eventId.Name} {logLevel}");
        }
    }
}
