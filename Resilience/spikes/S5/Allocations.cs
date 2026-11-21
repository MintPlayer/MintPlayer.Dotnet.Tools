using System.Diagnostics;
using System.Runtime.CompilerServices;
using Polly;

namespace S5;

/// <summary>Bytes per rejected call via GC.GetTotalAllocatedBytes(precise: true). Stopwatch ns are indicative only.</summary>
public static class Allocations
{
    public const int N = 200_000;
    private static readonly Func<int, CancellationToken, ValueTask<int>> Cb = static (s, _) => ValueTask.FromResult(s);
    private static readonly Func<ResilienceContext, int, ValueTask<Polly.Outcome<int>>> PollyCb = static (_, s) => Polly.Outcome.FromResultAsValueTask(s);
    private static readonly Func<int, CancellationToken, ValueTask<int>> PollyThrowCb = static (s, _) => ValueTask.FromResult(s);

    public static void Run()
    {
        Console.WriteLine($"## Allocation per rejected call (N = {N:N0}, after warm-up)");
        Console.WriteLine("| Scenario | Variant | B/op | ns/op (indicative) |");
        Console.WriteLine("|---|---|---:|---:|");
        foreach (var s in Enum.GetValues<Scenario>())
        {
            var ours = Scenarios.Ours(s);
            var polly = Scenarios.Polly(s);

            Report(s, "Ours TryExecuteAsync (Outcome)", () =>
            {
                var o = ours.TryExecuteAsync(Cb, 42, default).GetAwaiter().GetResult();
                return o.IsRejected;
            });
            Report(s, "Ours ExecuteAsync (fresh exception per throw)", () =>
            {
                try { ours.ExecuteAsync(Cb, 42, default).GetAwaiter().GetResult(); return false; }
                catch (ResilienceRejectedException) { return true; }
            });
            Report(s, "Ours ExecuteAsync (cached exception thrown)", () =>
            {
                try { ours.ExecuteAsyncCachedThrow(Cb, 42, default).GetAwaiter().GetResult(); return false; }
                catch (ResilienceRejectedException) { return true; }
            });
            Report(s, "Polly ExecuteOutcomeAsync (pooled context)", () =>
            {
                var ctx = ResilienceContextPool.Shared.Get();
                var o = polly.ExecuteOutcomeAsync(PollyCb, ctx, 42).GetAwaiter().GetResult();
                ResilienceContextPool.Shared.Return(ctx);
                return o.Exception is not null;
            });
            Report(s, "Polly ExecuteAsync (throws)", () =>
            {
                try { polly.ExecuteAsync(PollyThrowCb, 42, default).GetAwaiter().GetResult(); return false; }
                catch (Exception e) when (e is Polly.CircuitBreaker.BrokenCircuitException or Polly.RateLimiting.RateLimiterRejectedException) { return true; }
            });
        }

        Console.WriteLine();
        Console.WriteLine("## Bare synchronous throw/catch (no pipeline)");
        Console.WriteLine("| Scenario | Variant | B/op | ns/op (indicative) |");
        Console.WriteLine("|---|---|---:|---:|");
        var cached = new CircuitOpenException();
        Report(null, "throw cached instance", () => { try { Throw(cached); } catch (Exception) { return true; } return false; });
        Report(null, "throw new CircuitOpenException()", () => { try { Throw(new CircuitOpenException()); } catch (Exception) { return true; } return false; });
        Report(null, "throw new StacklessRejection()", () => { try { Throw(new StacklessRejection()); } catch (Exception) { return true; } return false; });
        Report(null, "new CircuitOpenException() (no throw)", () => { GC.KeepAlive(new CircuitOpenException()); return true; });
        Report(null, "EDI.SetCurrentStackTrace(new Exception()) (what Polly's rate limiter does)", () =>
        {
            GC.KeepAlive(System.Runtime.ExceptionServices.ExceptionDispatchInfo.SetCurrentStackTrace(new Exception())); return true;
        });

        Console.WriteLine();
        Console.WriteLine("## BCL limiter attribution (exhausted limiter, no pipeline)");
        Console.WriteLine("| Scenario | Variant | B/op | ns/op (indicative) |");
        Console.WriteLine("|---|---|---:|---:|");
        var fw = Scenarios.ExhaustedFixedWindow();
        var cl = Scenarios.ExhaustedConcurrency();
        Report(null, "FixedWindow AttemptAcquire + Dispose", () => { var l = fw.AttemptAcquire(1); var r = !l.IsAcquired; l.Dispose(); return r; });
        Report(null, "FixedWindow AttemptAcquire + TryGetMetadata(RetryAfter)", () =>
        {
            var l = fw.AttemptAcquire(1); _ = l.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out _); l.Dispose(); return true;
        });
        Report(null, "Concurrency AttemptAcquire + TryGetMetadata(RetryAfter)", () =>
        {
            var l = cl.AttemptAcquire(1); _ = l.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out _); l.Dispose(); return true;
        });
        Report(null, "Concurrency AcquireAsync (Polly's default path)", () =>
        {
            var l = cl.AcquireAsync(1).GetAwaiter().GetResult(); var r = !l.IsAcquired; l.Dispose(); return r;
        });
        Console.WriteLine();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Throw(Exception e) => throw e;

    private static void Report(Scenario? s, string name, Func<bool> op)
    {
        for (var i = 0; i < 5_000; i++) if (!op()) throw new InvalidOperationException($"{s} {name}: call was not rejected");
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < N; i++) op();
        sw.Stop();
        var after = GC.GetTotalAllocatedBytes(precise: true);
        Console.WriteLine($"| {s?.ToString() ?? "-"} | {name} | {(after - before) / (double)N:F1} | {sw.Elapsed.TotalNanoseconds / N:F0} |");
    }
}

/// <summary>A lightweight exception that hides its stack trace from ToString/StackTrace.</summary>
public sealed class StacklessRejection() : Exception("The circuit is open.")
{
    public override string? StackTrace => null;
}
