using System.Runtime.InteropServices;
using Spike.S3;
using Spike.S3.Generated;

var ct = CancellationToken.None;
var failures = 0;
Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");

// ---------- Part 1: the generator's classification of every call site ----------
Console.WriteLine();
Console.WriteLine("== Site report ==");
foreach (var s in S3SiteReport.Sites.OrderBy(s => s.Location.Length).ThenBy(s => s.Location, StringComparer.Ordinal))
{
    var file = Path.GetFileName(s.Location[..s.Location.IndexOf('(')]) + s.Location[s.Location.IndexOf('(')..];
    Console.WriteLine($"[{s.Outcome,-10}] {s.Site,-34} {file,-20} {s.Detail}");
    if (s.Rewrite.Length > 0) Console.WriteLine($"             -> {s.Rewrite}");
}

// ---------- Part 2: every shape runs; intercepted results must equal the closure semantics ----------
Console.WriteLine();
Console.WriteLine("== Shapes: result, expected (closure semantics), intercepted? ==");
var shapes = new Shapes(10);
var cases = new (string Name, Func<Task<object?>> Run, object Expected)[]
{
    ("ReadOnlyLocals", () => Box(shapes.ReadOnlyLocals(ct)), 42),
    ("CapturedParameters", () => Box(shapes.CapturedParameters(40, "ab", ct)), 42),
    ("ThisImplicit", () => Box(shapes.ThisImplicit(ct)), 11),
    ("ThisExplicitAndLocals", () => Box(shapes.ThisExplicitAndLocals(ct)), 14),
    ("MutatedAfterCall", async () => await shapes.MutatedAfterCall(ct), 2),
    ("MutatedInsideLambda", () => Box(shapes.MutatedInsideLambda(ct)), 11),
    ("WrittenBeforeCallOnly", () => Box(shapes.WrittenBeforeCallOnly(ct)), 5),
    ("MutableStructCapture", () => Box(shapes.MutableStructCapture(ct)), 12),
    ("ReadonlyStructCapture", () => Box(shapes.ReadonlyStructCapture(ct)), 251),
    ("NestedLambdaUsingCapture", () => Box(shapes.NestedLambdaUsingCapture(ct)), 4),
    ("NestedStaticLambda", () => Box(shapes.NestedStaticLambda(ct)), 4),
    ("AsyncLambda", async () => await shapes.AsyncLambda(ct), 11),
    ("AsyncLambdaSyncCompleting", () => Box(shapes.AsyncLambdaSyncCompleting(ct)), 8),
    ("GenericMethod", () => Box(shapes.GenericMethod("abcd", x => x.Length, ct)), 4),
    ("GenericResult", () => Box(shapes.GenericResult("r", ct)), "r"),
    ("StaticMethodGroup", () => Box(shapes.StaticMethodGroup(ct)), 5),
    ("InstanceMethodGroup", () => Box(shapes.InstanceMethodGroup(ct)), 10),
    ("OutVarInsideLambda", () => Box(shapes.OutVarInsideLambda(ct)), 42),
    ("RefToCapturedInsideLambda", () => Box(shapes.RefToCapturedInsideLambda(ct)), 11),
    ("SharedWithOtherClosure", () => Box(shapes.SharedWithOtherClosure(ct)), 9),
    ("LoopVariable", () => Box(shapes.LoopVariable(ct)), 12),
    ("NonCapturingLambda", () => Box(shapes.NonCapturingLambda(ct)), 9),
    ("BaseAccess", () => Box(shapes.BaseAccess(ct)), 100),
    ("TypedLambdaParameter", () => Box(shapes.TypedLambdaParameter(ct)), 5),
    ("AnonymousMethod", () => Box(shapes.AnonymousMethod(ct)), 6),
    ("InsideOuterLambda", () => Box(shapes.InsideOuterLambda(ct)), 33),
    ("AnonymousTypeProjection", () => Box(shapes.AnonymousTypeProjection(ct)), 7),
    ("NonStaticLocalFunction", () => Box(shapes.NonStaticLocalFunction(ct)), 4),
    ("StaticLocalFunction", () => Box(shapes.StaticLocalFunction(ct)), 4),
    ("CapturedCtOfOuterMethod", () => Box(shapes.CapturedCtOfOuterMethod(ct)), 0),
    ("LocalsInStructMethod", () => Box(new StructShapes(4).LocalsInStructMethod(ct)), 5),
};
foreach (var (name, run, expected) in cases)
{
    var before = Pipeline.Intercepted;
    var actual = await run();
    var intercepted = Pipeline.Intercepted - before;
    var ok = Equals(actual, expected);
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name,-28} result={actual,-5} expected={expected,-5} intercepted={intercepted}");
}

// ---------- Part 3: the generator's rewrites, pasted in (Rewritten.cs), vs closure semantics ----------
Console.WriteLine();
Console.WriteLine("== Rewritten (static lambda + state) vs closure semantics ==");
foreach (var (name, actual, expected, expectMatch) in await Rewritten.RunAll(ct))
{
    var match = Equals(actual, expected);
    var ok = match == expectMatch;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name,-34} rewritten={actual,-5} closure={expected,-5} {(expectMatch ? "must match" : "naive rewrite MUST differ (proves the fallback is required)")}");
}

// ---------- Part 4: allocation per call ----------
const int Warmup = 20_000, Ops = 200_000;
Console.WriteLine();
Console.WriteLine($"== Allocations: bytes/op over {Ops:N0} calls after {5 * Warmup:N0} warm-up calls ==");
var bench = new Bench();
var allocCases = new (string Name, Func<int, long> Run)[]
{
    ("Non-capturing lambda", Bench.NonCapturing),
    ("Local: plain (closure)", Bench.Local_Plain),
    ("Local: intercepted", Bench.Local_Intercepted),
    ("Local: rewritten static+state", Bench.Local_Rewritten),
    ("this: plain", bench.This_Plain),
    ("this: intercepted", bench.This_Intercepted),
    ("this: rewritten", bench.This_Rewritten),
    ("2 locals + this: plain", bench.TwoLocalsThis_Plain),
    ("2 locals + this: intercepted", bench.TwoLocalsThis_Intercepted),
    ("2 locals + this: rewritten (tuple)", bench.TwoLocalsThis_Rewritten),
    ("async lambda: plain", Bench.AsyncLambda_Plain),
    ("async lambda: intercepted", Bench.AsyncLambda_Intercepted),
    ("async lambda: rewritten", Bench.AsyncLambda_Rewritten),
    ("shared capture: plain", Bench.Shared_Plain),
    ("shared capture: rewritten", Bench.Shared_Rewritten),
    ("async caller: plain", n => Bench.AsyncCaller_Plain(n).GetAwaiter().GetResult()),
    ("async caller: intercepted", n => Bench.AsyncCaller_Intercepted(n).GetAwaiter().GetResult()),
    ("async caller: rewritten", n => Bench.AsyncCaller_Rewritten(n).GetAwaiter().GetResult()),
    ("inlinable pipeline, closure (JIT EA?)", Bench.InlinablePipeline_Local),
};
foreach (var (name, run) in allocCases)
{

    for (var w = 0; w < 5; w++) run(Warmup);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var interceptedBefore = Pipeline.Intercepted;
    var before = GC.GetTotalAllocatedBytes(precise: true);
    var result = run(Ops);
    var after = GC.GetTotalAllocatedBytes(precise: true);
    var intercepted = Pipeline.Intercepted - interceptedBefore;

    Console.WriteLine($"{name,-40} {(after - before) / (double)Ops,8:F2} B/op   intercepted={intercepted,7}  checksum={result}");
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

static Task<object?> Box<T>(T value) => Task.FromResult<object?>(value);
