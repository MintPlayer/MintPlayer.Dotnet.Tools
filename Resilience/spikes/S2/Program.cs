using S1;
using S2;

// ---------- Part 1: where do the bytes go? (exact bytes/op, not a BDN average) ----------
const int Warmup = 20_000, Ops = 200_000, Concurrency = 16;

var flat = new FlatPipeline();
var flatPooled = new FlatPooledPipeline();
var cb = Variants.AsyncCallback;

var cases = new (string name, Func<ValueTask<int>> op)[]
{
    ("callback only (baseline)", () => cb(null, CancellationToken.None)),
    ("plain builder, no CTS", () => Variants.PlainWrap(cb, null, CancellationToken.None)),
    ("pooled builder, no CTS", () => Variants.PooledWrap(cb, null, CancellationToken.None)),
    ("pooled builder + 2 CTS", () => Variants.PooledWithCts(cb, null, CancellationToken.None)),
    ("Flat (full pipeline)", () => flat.ExecuteAsync(cb, (object?)null, CancellationToken.None)),
    ("FlatPooled (full pipeline)", () => flatPooled.ExecuteAsync(cb, (object?)null, CancellationToken.None)),
};

static async Task Loop(Func<ValueTask<int>> op, int n)
{
    for (var i = 0; i < n; i++)
        if (await op() != 42) throw new InvalidOperationException("wrong result");
}

static async Task<double> Measure(Func<ValueTask<int>> op, int parallel)
{
    await Task.WhenAll(Enumerable.Range(0, parallel).Select(_ => Task.Run(() => Loop(op, Warmup / parallel))));
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var before = GC.GetTotalAllocatedBytes(precise: true);
    await Task.WhenAll(Enumerable.Range(0, parallel).Select(_ => Task.Run(() => Loop(op, Ops / parallel))));
    var after = GC.GetTotalAllocatedBytes(precise: true);
    return (after - before) / (double)Ops;
}

Console.WriteLine($"Part 1 — bytes/op over {Ops:N0} ops (includes the callback's own box):");
Console.WriteLine($"  {"case",-30} {"CTS pool",-14} {"sequential",10} {"16 concurrent",14}");
foreach (var threadStatic in new[] { true, false })
{
    CtsPool.UseThreadStatic = threadStatic;
    foreach (var (name, op) in cases)
    {
        var seq = await Measure(op, 1);
        var par = await Measure(op, Concurrency);
        Console.WriteLine($"  {name,-30} {(threadStatic ? "thread-static" : "queue only"),-14} {seq,10:F1} {par,14:F1}");
    }
}
CtsPool.UseThreadStatic = true;

// ---------- Part 2: misuse — does the pooled builder fail loudly or corrupt silently? ----------
Console.WriteLine();
Console.WriteLine("Part 2 — misuse (plain builder vs pooled builder):");

static async Task<string> Try(Func<Task> f)
{
    try { await f(); return "works"; }
    catch (Exception ex) { return $"throws {ex.GetType().Name}"; }
}

foreach (var (label, exec) in new (string, Func<ValueTask<int>>)[]
{
    ("plain ", () => Variants.PlainWrap(cb, null, CancellationToken.None)),
    ("pooled", () => Variants.PooledWrap(cb, null, CancellationToken.None)),
})
{
    // Await the same ValueTask twice, with other operations in between (so a pooled box is re-rented).
    var doubleAwait = await Try(async () =>
    {
        var vt = exec();
        await vt;
        for (var i = 0; i < 50; i++) await exec();
        var again = await vt;
        if (again != 42) throw new Exception($"silent corruption: {again}");
    });

    // Await twice immediately (no re-rent in between).
    var doubleAwaitImmediate = await Try(async () => { var vt = exec(); await vt; await vt; });

    // Two concurrent awaiters on the same ValueTask.
    // Crashes the process under the pooled builder (unobservable throw on the thread pool), so opt-in only.
    var concurrentAwait = !args.Contains("--concurrent-awaiters") ? "skipped (run with --concurrent-awaiters)" : await Try(async () =>
    {
        var vt = exec();
        await Task.WhenAll(Task.Run(async () => await vt), Task.Run(async () => await vt));
    });

    // .Result before completion.
    var earlyResult = await Try(() => { var vt = exec(); _ = vt.Result; return Task.CompletedTask; });

    // Correct usage: .AsTask() once, then await the Task as often as you like.
    var asTask = await Try(async () => { var t = exec().AsTask(); await t; await t; });

    Console.WriteLine($"  {label} double await (re-rented): {doubleAwait}");
    Console.WriteLine($"  {label} double await (immediate): {doubleAwaitImmediate}");
    Console.WriteLine($"  {label} two concurrent awaiters:  {concurrentAwait}");
    Console.WriteLine($"  {label} .Result before completion: {earlyResult}");
    Console.WriteLine($"  {label} .AsTask() then await twice: {asTask}");
}

// ---------- Part 3: correctness under stress ----------
Console.WriteLine();
var wrong = 0;
await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
{
    for (var i = 0; i < 20_000; i++)
        if (await flatPooled.ExecuteAsync(cb, (object?)null, CancellationToken.None) != 42) Interlocked.Increment(ref wrong);
})));
Console.WriteLine($"Part 3 — 64 × 20,000 concurrent FlatPooled executions: {wrong} wrong results");
