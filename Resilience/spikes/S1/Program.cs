using BenchmarkDotNet.Running;
using S1;

// Fairness gate: every implementation must produce the same results and attempt counts before any
// number counts.
static async Task<(int result, int calls)> Run(Func<Func<Flip, CancellationToken, ValueTask<int>>, Flip, ValueTask<int>> exec,
    Func<Flip, CancellationToken, ValueTask<int>> callback)
{
    var flip = new Flip();
    var r = await exec(callback, flip);
    return (r, flip.Calls);
}

var impls = new (string name, Func<Func<Func<Flip, CancellationToken, ValueTask<int>>, Flip, ValueTask<int>>> make)[]
{
    ("Polly", () => { var p = PollySetup.Create(); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
    ("Flat", () => { var p = new FlatPipeline(); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
    ("FlatPooled", () => { var p = new FlatPooledPipeline(); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
    ("Nested", () => { var p = Nested.Create(); return (cb, s) => p.ExecuteAsync(cb, s, CancellationToken.None); }),
};

var ok = true;
Console.WriteLine("Fairness gate:");
foreach (var (name, make) in impls)
{
    var sync = await Run(make(), Callbacks.Sync);
    var async = await Run(make(), Callbacks.Async);
    var retry = await Run(make(), Callbacks.OneRetry);
    var fail = await Run(make(), Callbacks.AlwaysFail);
    var pass = sync.result == 42 && async.result == 42 && retry is (42, 2) && fail is (0, Config.MaxRetries + 1);
    ok &= pass;
    Console.WriteLine($"  {name,-11} sync={sync.result} async={async.result} oneRetry={retry} alwaysFail={fail} -> {(pass ? "OK" : "FAIL")}");
}

Console.WriteLine("Attribute order:");
AttributeOrder.Run();

if (!ok) { Console.WriteLine("FAIRNESS GATE FAILED"); return 1; }
if (args.Contains("--verify-only")) return 0;

BenchmarkRunner.Run<PipelineBenchmarks>(args: args);
return 0;
