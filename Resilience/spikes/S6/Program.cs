using System.Diagnostics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using S6;

if (args.Contains("--bench"))
{
    BenchmarkRunner.Run<ReloadBenchmarks>();
    return 0;
}

var failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    if (!ok) failures++;
}

const string P = "Resilience:CatalogPipeline:";
var breaker = CatalogPipeline.BreakerForTests;

// ---------- Setup: in-memory configuration, DI, hosted activation ----------
var root = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        [P + "Retry:MaxRetries"] = "3",
        [P + "Attempt:Timeout"] = "00:00:02",
        [P + "CircuitBreaker:MinimumThroughput"] = "1000000", // keep the circuit closed except in test 4
    })
    .Build();

void Set(params (string key, string? value)[] kv)
{
    foreach (var (k, v) in kv) root[P + k] = v;
    root.Reload(); // fires the reload token → IOptionsMonitor.OnChange → CatalogPipeline.TryApply
}

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(root);
services.AddResiliencePipeline<CatalogPipeline>();
services.AddResiliencePipeline<CatalogPipeline>(); // idempotent: TryAddEnumerable → one binding
var provider = services.BuildServiceProvider();
var hosted = provider.GetServices<IHostedService>().ToArray();
foreach (var h in hosted) await h.StartAsync(default);

Console.WriteLine("Setup");
Check(hosted.Length == 1, $"one hosted binding after two AddResiliencePipeline calls (got {hosted.Length})");
Check(CatalogPipeline.Current.MaxRetries == 3 && CatalogPipeline.Current.InnerTimeout == TimeSpan.FromSeconds(2), "startup applies configuration");

// ---------- Test 1: next execution sees the new values, the in-flight one keeps its snapshot ----------
Console.WriteLine("Test 1 — reload MaxRetries 3→1, Attempt:Timeout 2s→50ms during an execution");
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var e1Attempts = 0;
    var e1LaterAttemptCancelled = false;
    var e1 = CatalogPipeline.ExecuteAsync(async (_, ct) =>
    {
        if (Interlocked.Increment(ref e1Attempts) == 1) await gate.Task; // parked across the reload
        else
        {
            try { await Task.Delay(200, ct); } // 200 ms: fine under the OLD 2 s timeout, would die under 50 ms
            catch (OperationCanceledException) { e1LaterAttemptCancelled = true; throw; }
        }
        return -1; // handled → retry
    }, 0, default).AsTask();

    var v0 = CatalogPipeline.Current.Version;
    Set(("Retry:MaxRetries", "1"), ("Attempt:Timeout", "00:00:00.050"));
    Check(CatalogPipeline.Current.Version > v0 && CatalogPipeline.Current.MaxRetries == 1, "snapshot swapped synchronously on reload");

    var e2Attempts = 0;
    var r2 = await CatalogPipeline.ExecuteAsync((_, _) => { e2Attempts++; return new ValueTask<int>(-1); }, 0, default);
    Check(e2Attempts == 2 && r2 == 0, $"next execution uses MaxRetries=1 (attempts {e2Attempts}, expected 2)");

    var e3Attempts = 0;
    var sw = Stopwatch.StartNew();
    var r3 = await CatalogPipeline.ExecuteAsync(async (_, ct) => { e3Attempts++; await Task.Delay(Timeout.Infinite, ct); return 42; }, 0, default);
    sw.Stop();
    Check(e3Attempts == 2 && r3 == 0 && sw.ElapsedMilliseconds < 1000, $"next execution uses the 50 ms attempt timeout (2 attempts in {sw.ElapsedMilliseconds} ms)");

    gate.SetResult();
    var r1 = await e1;
    Check(e1Attempts == 4 && r1 == 0, $"in-flight execution kept MaxRetries=3 (attempts {e1Attempts}, expected 4)");
    Check(!e1LaterAttemptCancelled, "in-flight execution kept the 2 s attempt timeout for its post-reload attempts");
}

// ---------- Test 2: an invalid reload is rejected and the old snapshot stays live ----------
Console.WriteLine("Test 2 — invalid values");
{
    var before = CatalogPipeline.Current;
    var failuresBefore = ReloadDiagnostics<CatalogPipeline>.Failures;
    Exception? thrown = null;
    try { Set(("Retry:MaxRetries", "-1")); } catch (Exception ex) { thrown = ex; }
    Check(thrown is null, "semantic validation failure does not throw out of IConfigurationRoot.Reload()");
    Check(ReferenceEquals(CatalogPipeline.Current, before), "snapshot unchanged after rejected reload");
    Check(ReloadDiagnostics<CatalogPipeline>.Failures == failuresBefore + 1,
        $"exactly one failure reported (no double binding): {ReloadDiagnostics<CatalogPipeline>.LastError}");

    thrown = null;
    try { Set(("Retry:MaxRetries", "abc")); } catch (Exception ex) { thrown = ex; }
    Check(thrown is null, "unconvertible value does not throw out of Reload() (factory in try/catch)");
    Check(ReferenceEquals(CatalogPipeline.Current, before), "snapshot unchanged after unconvertible value");
    Check(ReloadDiagnostics<CatalogPipeline>.Failures == failuresBefore + 2, $"binder error reported: {ReloadDiagnostics<CatalogPipeline>.LastError}");

    Set(("Retry:MaxRetries", "1"));
    Check(CatalogPipeline.Current.MaxRetries == 1 && !ReferenceEquals(CatalogPipeline.Current, before), "a later valid reload is applied again");
}

// ---------- Test 2b: the same unconvertible value through a plain IOptionsMonitor.OnChange binding ----------
Console.WriteLine("Test 2b — IOptionsMonitor.OnChange binding (StructPipeline, own configuration root)");
{
    var root2 = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [P + "Retry:MaxRetries"] = "2" }).Build();
    var sc2 = new ServiceCollection();
    sc2.AddSingleton<IConfiguration>(root2);
    sc2.AddReloadablePipeline<StructPipeline, CatalogPipelineOptions>(viaMonitor: true);
    await using var sp2 = sc2.BuildServiceProvider();
    var hosted2 = sp2.GetServices<IHostedService>().ToArray();
    foreach (var h in hosted2) await h.StartAsync(default);
    Check(StructPipeline.CurrentMaxRetries == 2, "monitor binding applies at start");

    root2[P + "Retry:MaxRetries"] = "1"; root2.Reload();
    Check(StructPipeline.CurrentMaxRetries == 1, "monitor binding applies a valid reload");

    Exception? thrown = null;
    root2[P + "Retry:MaxRetries"] = "abc";
    try { root2.Reload(); } catch (Exception ex) { thrown = ex; }
    Check(thrown is AggregateException, $"[expected failure mode] unconvertible value THROWS out of Reload(): {thrown?.GetType().Name}: {thrown?.InnerException?.Message}");
    Check(StructPipeline.CurrentMaxRetries == 1, "snapshot unchanged (listener never ran)");
    foreach (var h in hosted2) await h.StopAsync(default);
}

// ---------- Test 3: circuit-breaker health state survives a reload; new thresholds apply ----------
Console.WriteLine("Test 3 — circuit breaker across reloads");
{
    breaker.Reset();
    Set(("Retry:MaxRetries", "0"), ("CircuitBreaker:MinimumThroughput", "100"));
    for (var i = 0; i < 5; i++) await CatalogPipeline.ExecuteAsync((_, _) => new ValueTask<int>(-1), 0, default);
    Check(breaker.State == 0, "5 failures under MinimumThroughput=100: closed");

    Set(("CircuitBreaker:MinimumThroughput", "6"));
    await CatalogPipeline.ExecuteAsync((_, _) => new ValueTask<int>(-1), 0, default);
    Check(breaker.State == 1, "reload to MinimumThroughput=6 keeps the 5 counted failures: 6th failure opens");

    Set(("Retry:MaxRetries", "2"), ("CircuitBreaker:FailureRatio", "0.5"));
    var called = false;
    await CatalogPipeline.ExecuteAsync((_, _) => { called = true; return new ValueTask<int>(42); }, 0, default);
    Check(breaker.State == 1 && !called, "an unrelated reload does NOT close an open circuit (Polly's rebuild would)");

    breaker.Reset();
    Set(("CircuitBreaker:MinimumThroughput", "1000000"), ("CircuitBreaker:FailureRatio", "0.9"));
}

// ---------- Test 4: swapping snapshots while executions run ----------
Console.WriteLine("Test 4 — concurrency: 16 workers x 20,000 executions while MaxRetries toggles 1/3");
{
    Set(("Retry:MaxRetries", "1"), ("Attempt:Timeout", "00:00:05"));
    const int Workers = 16, PerWorker = 20_000;
    var done = 0;
    var bad = 0; var saw2 = 0; var saw4 = 0;
    var reloads = 0;
    var toggler = Task.Run(() =>
    {
        while (Volatile.Read(ref done) < Workers)
        {
            Set(("Retry:MaxRetries", reloads % 2 == 0 ? "3" : "1"));
            reloads++;
        }
    });
    var workers = Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
    {
        var box = new StrongBox<int>();
        for (var i = 0; i < PerWorker; i++)
        {
            box.Value = 0;
            var r = await CatalogPipeline.ExecuteAsync(static (b, _) => { b.Value++; return new ValueTask<int>(-1); }, box, default);
            if (r != 0) Interlocked.Increment(ref bad);
            else if (box.Value == 2) Interlocked.Increment(ref saw2);
            else if (box.Value == 4) Interlocked.Increment(ref saw4);
            else Interlocked.Increment(ref bad); // a torn or mid-execution-changed retry count
        }
        Interlocked.Increment(ref done);
    })).ToArray();
    await Task.WhenAll(workers);
    await toggler;
    Check(bad == 0, $"every execution ran with ONE consistent MaxRetries (2 attempts: {saw2:N0}, 4 attempts: {saw4:N0}, other: {bad})");
    Check(saw2 > 0 && saw4 > 0 && reloads > 10, $"both values observed across {reloads:N0} reloads");
}

// ---------- Test 5: reloadable JSON file; a removed key falls back to the attribute default ----------
Console.WriteLine("Test 5 — JSON file (reloadOnChange) with a second service provider");
{
    // The generated class is static, so only ONE provider may bind it at a time: stop the first one.
    foreach (var h in hosted) await h.StopAsync(default);
    await provider.DisposeAsync();

    var dir = Path.Combine(Path.GetTempPath(), "s6-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var file = Path.Combine(dir, "appsettings.json");
    File.WriteAllText(file, """{ "Resilience": { "CatalogPipeline": { "Retry": { "MaxRetries": 1 }, "Attempt": { "Timeout": "00:00:00.250" } } } }""");

    var json = new ConfigurationBuilder().SetBasePath(dir).AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).Build();
    var sc = new ServiceCollection();
    sc.AddSingleton<IConfiguration>(json);
    sc.AddResiliencePipeline<CatalogPipeline>();
    await using var sp = sc.BuildServiceProvider();
    foreach (var h in sp.GetServices<IHostedService>()) await h.StartAsync(default);
    Check(CatalogPipeline.Current.MaxRetries == 1 && CatalogPipeline.Current.InnerTimeout == TimeSpan.FromMilliseconds(250), "file values applied at start");

    // Waits for the binding's own "reload attempted" signal (FileSystemWatcher can fire more than once; we
    // want the first attempt that reflects the write).
    async Task<bool> WriteAndWait(string content, bool expectApplied)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(bool applied) { if (applied == expectApplied) tcs.TrySetResult(applied); }
        ReloadDiagnostics<CatalogPipeline>.Reloaded += Handler;
        try
        {
            File.WriteAllText(file, content);
            return await Task.WhenAny(tcs.Task, Task.Delay(10_000)) == tcs.Task;
        }
        finally { ReloadDiagnostics<CatalogPipeline>.Reloaded -= Handler; }
    }

    var fired = await WriteAndWait("""{ "Resilience": { "CatalogPipeline": { "Attempt": { "Timeout": "00:00:00.100" } } } }""", expectApplied: true);
    Check(fired, "file change reached the binding and was applied");
    Check(CatalogPipeline.Current.MaxRetries == 3, $"removed Retry:MaxRetries falls back to the attribute default 3 (got {CatalogPipeline.Current.MaxRetries})");
    Check(CatalogPipeline.Current.InnerTimeout == TimeSpan.FromMilliseconds(100), "edited Attempt:Timeout applied");

    var good = CatalogPipeline.Current;
    fired = await WriteAndWait("""{ "Resilience": { "CatalogPipeline": { "Retry": { "MaxRetries": "abc" } } } }""", expectApplied: false);
    Check(fired && ReferenceEquals(CatalogPipeline.Current, good), $"unconvertible value in the file: rejected, process alive, snapshot kept ({ReloadDiagnostics<CatalogPipeline>.LastError})");

    fired = await WriteAndWait("""{ "Resilience": { "CatalogPipeline": { "Retry": { "MaxRetries": 5 } } } }""", expectApplied: true);
    Check(fired && CatalogPipeline.Current.MaxRetries == 5, "file watcher still live after the bad write: MaxRetries=5 applied");
    try { Directory.Delete(dir, true); } catch { }
}

// ---------- Allocation check: bytes/op on the sync-completing path ----------
Console.WriteLine("Allocations — sync-completing callback, 200,000 ops after 20,000 warm-up");
{
    // Test 4 left ~850k counted failures in the (preserved) breaker window; with the default MinimumThroughput
    // of 10 the next success would correctly OPEN the circuit. Reset, since this section measures the happy path.
    breaker.Reset();
    CatalogPipeline.TryApply(new CatalogPipelineOptions(), out _);
    StructPipeline.TryApply(new CatalogPipelineOptions(), out _);
    Func<int, CancellationToken, ValueTask<int>> cb = static (s, _) => new ValueTask<int>(s);
    (string name, Func<ValueTask<int>> op)[] cases =
    [
        ("A constants", () => ConstPipeline.ExecuteAsync(cb, 42, default)),
        ("B class snapshot", () => CatalogPipeline.ExecuteAsync(cb, 42, default)),
        ("C struct snapshot", () => StructPipeline.ExecuteAsync(cb, 42, default)),
    ];
    foreach (var (name, op) in cases)
    {
        for (var i = 0; i < 20_000; i++) if (op().Result != 42) throw new InvalidOperationException();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        // The sync-completing path never leaves this thread, so the per-thread counter is exact; the process-wide
        // counter also sees background threads (tiered JIT, timers) and is printed for reference only.
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var beforeThread = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200_000; i++) if (op().Result != 42) throw new InvalidOperationException();
        var perOpThread = (GC.GetAllocatedBytesForCurrentThread() - beforeThread) / 200_000.0;
        var perOp = (GC.GetTotalAllocatedBytes(precise: true) - before) / 200_000.0;
        Check(perOpThread == 0, $"{name,-18} {perOpThread:F2} B/op this thread ({perOp:F3} B/op process-wide)");
    }
}

Console.WriteLine("Allocations — async-suspending callback (Task.Yield), 200,000 ops after 20,000 warm-up, process-wide");
{
    // The snapshot (B: one reference; C: the whole struct) is hoisted into the state machine here. With the pooling
    // builder the box is reused, so the only steady-state allocation should be the callback's own box.
    Func<int, CancellationToken, ValueTask<int>> cb = static async (s, _) => { await Task.Yield(); return s; };
    (string name, Func<ValueTask<int>> op)[] cases =
    [
        ("callback only", () => cb(42, default)),
        ("A constants", () => ConstPipeline.ExecuteAsync(cb, 42, default)),
        ("B class snapshot", () => CatalogPipeline.ExecuteAsync(cb, 42, default)),
        ("C struct snapshot", () => StructPipeline.ExecuteAsync(cb, 42, default)),
    ];
    static async Task Loop(Func<ValueTask<int>> op, int n) { for (var i = 0; i < n; i++) if (await op() != 42) throw new InvalidOperationException(); }
    double baseline = 0;
    foreach (var (name, op) in cases)
    {
        await Loop(op, 20_000);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        await Loop(op, 200_000);
        var perOp = (GC.GetTotalAllocatedBytes(precise: true) - before) / 200_000.0;
        if (name == "callback only") { baseline = perOp; Console.WriteLine($"  [INFO] {name,-18} {perOp:F2} B/op"); continue; }
        Check(perOp - baseline < 1, $"{name,-18} {perOp:F2} B/op ({perOp - baseline:+0.00;-0.00} above the callback)");
    }
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
