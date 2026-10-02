using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

namespace S5;

/// <summary>Empirical checks: what does the runtime do to an Exception instance that is thrown more than once?</summary>
public static class Safety
{
    [MethodImpl(MethodImplOptions.NoInlining)] private static void ThrowFromAlpha(Exception e) => throw e;
    [MethodImpl(MethodImplOptions.NoInlining)] private static void ThrowFromBeta(Exception e) => throw e;

    private static Exception Catch(Action<Exception> thrower, Exception e)
    {
        try { thrower(e); } catch (Exception c) { return c; }
        throw new InvalidOperationException("did not throw");
    }

    private static string Has(string? s, string marker) => s is not null && s.Contains(marker) ? "yes" : "no";

    public static void Run()
    {
        Console.WriteLine("## Safety experiments");
        E1_Sequential();
        E2_HeldThenOverwritten();
        E3_Concurrent();
        E4_Data();
        E5_Growth();
        E6_FieldsMutated();
        E7_Stackless();
        E8_Sentinel();
    }

    private static void E1_Sequential()
    {
        Console.WriteLine("### E1 same instance thrown from two sites, sequentially");
        var ex = new CircuitOpenException();
        var a = Catch(ThrowFromAlpha, ex);
        var stA = a.StackTrace;
        var tsA = a.TargetSite?.Name;
        var srcA = a.Source;
        var b = Catch(ThrowFromBeta, ex);
        Console.WriteLine($"- same object reference caught both times: {ReferenceEquals(a, b)}");
        Console.WriteLine($"- after Alpha throw: StackTrace has Alpha={Has(stA, "ThrowFromAlpha")}; TargetSite={tsA}; Source={srcA}");
        Console.WriteLine($"- after Beta throw:  StackTrace has Beta={Has(b.StackTrace, "ThrowFromBeta")}, Alpha={Has(b.StackTrace, "ThrowFromAlpha")}; TargetSite={b.TargetSite?.Name} (cached from first read?); Source={b.Source}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception? RequestOne(OurPipeline p)
    {
        try { p.ExecuteAsyncCachedThrow(static (int s, CancellationToken _) => ValueTask.FromResult(s), 1, default).GetAwaiter().GetResult(); }
        catch (Exception e) { return e; }
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception? RequestTwo(OurPipeline p)
    {
        try { p.ExecuteAsyncCachedThrow(static (int s, CancellationToken _) => ValueTask.FromResult(s), 2, default).GetAwaiter().GetResult(); }
        catch (Exception e) { return e; }
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception? RequestOneFresh(OurPipeline p)
    {
        try { p.ExecuteAsync(static (int s, CancellationToken _) => ValueTask.FromResult(s), 1, default).GetAwaiter().GetResult(); }
        catch (Exception e) { return e; }
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception? RequestTwoFresh(OurPipeline p)
    {
        try { p.ExecuteAsync(static (int s, CancellationToken _) => ValueTask.FromResult(s), 2, default).GetAwaiter().GetResult(); }
        catch (Exception e) { return e; }
        return null;
    }

    private static void E2_HeldThenOverwritten()
    {
        Console.WriteLine("### E2 request 1 catches, request 2 (other thread) is rejected, then request 1 logs");
        foreach (var fresh in new[] { false, true })
        {
            var p = Scenarios.Ours(Scenario.CircuitOpen);
            var log = new CaptureLogger();
            var held = fresh ? RequestOneFresh(p)! : RequestOne(p)!;
            var before = held.ToString();
            var t = new Thread(() => { if (fresh) RequestTwoFresh(p); else RequestTwo(p); });
            t.Start(); t.Join();
            log.LogError(held, "Request {Id} rejected", 1);
            var logged = log.Last;
            var name = fresh ? "fresh" : "cached";
            Console.WriteLine($"- {name}: ToString before other request: RequestOne={Has(before, "RequestOne")}, RequestTwo={Has(before, "RequestTwo")}");
            Console.WriteLine($"- {name}: ILogger output for request 1 after request 2: RequestOne={Has(logged, "RequestOne")}, RequestTwo={Has(logged, "RequestTwo")}");
            if (!fresh) Console.WriteLine("```\n" + logged + "\n```");
        }
    }

    private static void E3_Concurrent()
    {
        Console.WriteLine("### E3 concurrent: 8 threads x 50k throws; after catch, does StackTrace show this thread's own throw site?");
        const int threads = 8, iters = 50_000;
        foreach (var mode in new[] { "cached, bare throw", "fresh, bare throw", "cached, via ExecuteAsync (EDI path)", "fresh, via ExecuteAsync (EDI path)" })
        {
            var shared = new CircuitOpenException();
            var p = Scenarios.Ours(Scenario.CircuitOpen);
            long wrong = 0, both = 0, empty = 0, readFaults = 0;
            var ts = Enumerable.Range(0, threads).Select(tid => new Thread(() =>
            {
                var alpha = tid % 2 == 0;
                string mine = mode.Contains("Execute") ? (alpha ? "RequestOne" : "RequestTwo") : (alpha ? "ThrowFromAlpha" : "ThrowFromBeta");
                string other = mode.Contains("Execute") ? (alpha ? "RequestTwo" : "RequestOne") : (alpha ? "ThrowFromBeta" : "ThrowFromAlpha");
                for (var i = 0; i < iters; i++)
                {
                    Exception e = mode switch
                    {
                        "cached, bare throw" => Catch(alpha ? ThrowFromAlpha : ThrowFromBeta, shared),
                        "fresh, bare throw" => Catch(alpha ? ThrowFromAlpha : ThrowFromBeta, new CircuitOpenException()),
                        "cached, via ExecuteAsync (EDI path)" => (alpha ? RequestOne(p) : RequestTwo(p))!,
                        _ => (alpha ? RequestOneFresh(p) : RequestTwoFresh(p))!,
                    };
                    string? st;
                    try { st = e.StackTrace; } catch { Interlocked.Increment(ref readFaults); continue; }
                    if (string.IsNullOrEmpty(st)) Interlocked.Increment(ref empty);
                    else if (!st.Contains(mine)) Interlocked.Increment(ref wrong);
                    else if (st.Contains(other)) Interlocked.Increment(ref both);
                }
            })).ToList();
            ts.ForEach(t => t.Start());
            ts.ForEach(t => t.Join());
            Console.WriteLine($"- {mode}: of {threads * iters:N0} catches, other thread's stack only={wrong:N0}, both stacks mixed={both:N0}, empty={empty:N0}, read faults={readFaults:N0}");
        }
    }

    private static void E4_Data()
    {
        Console.WriteLine("### E4 Exception.Data on a shared instance");
        var ex = new CircuitOpenException();
        var first = Catch(ThrowFromAlpha, ex);
        first.Data["RequestId"] = "req-1"; // e.g. an exception filter / middleware enriching the exception
        var second = Catch(ThrowFromBeta, ex);
        Console.WriteLine($"- second, unrelated caller sees Data[\"RequestId\"] = {second.Data["RequestId"] ?? "null"}");

        var shared = new CircuitOpenException();
        long faults = 0;
        var ts = Enumerable.Range(0, 8).Select(tid => new Thread(() =>
        {
            for (var i = 0; i < 100_000; i++)
            {
                try { shared.Data[$"k{tid}"] = i; shared.Data.Remove($"k{tid}"); }
                catch { Interlocked.Increment(ref faults); }
            }
        }) { IsBackground = true }).ToList();
        ts.ForEach(t => t.Start());
        var finished = ts.All(t => t.Join(TimeSpan.FromSeconds(20)));
        Console.WriteLine($"- 8 threads add/remove own key 100k times: finished={finished}, exceptions={faults:N0}, leftover entries={(finished ? shared.Data.Count : -1)} (expected 0)");
    }

    private static void E5_Growth()
    {
        Console.WriteLine("### E5 does the stack trace string grow across repeated throws of one instance?");
        var a = new CircuitOpenException();
        var b = new CircuitOpenException();
        var p = Scenarios.Ours(Scenario.CircuitOpen);
        var c = (Exception)Rejections.CircuitOpen;
        for (var n = 1; n <= 1000; n++)
        {
            try { ThrowFromAlpha(a); } catch { }
            try { ExceptionDispatchInfo.Capture(b).Throw(); } catch { }
            if (n is 1 or 10 or 100 or 1000)
                Console.WriteLine($"- n={n}: `throw ex` len={a.StackTrace?.Length ?? 0}, `EDI.Capture(ex).Throw()` len={b.StackTrace?.Length ?? 0}");
        }

        var d = new CircuitOpenException();
        for (var i = 1; i <= 1000; i++)
        {
            try { ExecuteThrowing(d).GetAwaiter().GetResult(); } catch { }
            if (i is 1 or 10 or 100 or 1000) Console.WriteLine($"- async method throwing cached instance, awaited, n={i}: len={d.StackTrace?.Length ?? 0}");
        }
        GC.KeepAlive(p); GC.KeepAlive(c);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<int> ExecuteThrowing(Exception e)
    {
        await Task.CompletedTask;
        throw e;
    }

    private static void E6_FieldsMutated()
    {
        Console.WriteLine("### E6 private System.Exception fields changed by a throw of an existing instance");
        var fields = typeof(Exception).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var ex = new CircuitOpenException();
        object?[] Snap() => fields.Select(f => f.GetValue(ex)).ToArray();
        string Diff(object?[] x, object?[] y) => string.Join(", ", fields.Where((f, i) => !Equals(x[i], y[i])).Select(f => f.Name));
        var s0 = Snap();
        Catch(ThrowFromAlpha, ex); var s1 = Snap();
        _ = ex.StackTrace; _ = ex.TargetSite; _ = ex.Source; var s2 = Snap();
        Catch(ThrowFromBeta, ex); var s3 = Snap();
        try { ExceptionDispatchInfo.Capture(ex).Throw(); } catch { }
        var s4 = Snap();
        Console.WriteLine($"- all fields: {string.Join(", ", fields.Select(f => f.Name))}");
        Console.WriteLine($"- first throw changed: {Diff(s0, s1)}");
        Console.WriteLine($"- reading StackTrace/TargetSite/Source changed: {Diff(s1, s2)}");
        Console.WriteLine($"- second throw (other site) changed: {Diff(s2, s3)}");
        Console.WriteLine($"- EDI.Capture(ex).Throw() changed: {Diff(s3, s4)}");
    }

    private static void E7_Stackless()
    {
        Console.WriteLine("### E7 stackless exception type (StackTrace overridden to null)");
        var ex = new StacklessRejection();
        var c = Catch(ThrowFromAlpha, ex);
        var frames = new System.Diagnostics.StackTrace(c).FrameCount;
        var edi = ExceptionDispatchInfo.Capture(c);
        Exception? re = null;
        try { edi.Throw(); } catch (Exception r) { re = r; }
        Console.WriteLine($"- ToString() contains a frame: {Has(c.ToString(), "ThrowFromAlpha")}; ex.StackTrace is null: {c.StackTrace is null}; new StackTrace(ex).FrameCount = {frames} (runtime still captured it)");
        Console.WriteLine($"- after EDI rethrow, ToString contains frame: {Has(re?.ToString(), "ThrowFromAlpha")}");
    }

    private static void E8_Sentinel()
    {
        Console.WriteLine("### E8 never-thrown sentinel handed out via Outcome.Exception");
        var p = Scenarios.Ours(Scenario.CircuitOpen);
        for (var i = 0; i < 10_000; i++)
            _ = p.TryExecuteAsync(static (int s, CancellationToken _) => ValueTask.FromResult(s), 1, default).GetAwaiter().GetResult().Exception;
        var o = p.TryExecuteAsync(static (int s, CancellationToken _) => ValueTask.FromResult(s), 1, default).GetAwaiter().GetResult();
        Console.WriteLine($"- after 10k rejections: Exception is CircuitOpenException={o.Exception is CircuitOpenException}, StackTrace is null={o.Exception!.StackTrace is null}, Data.Count={o.Exception.Data.Count}, RetryAfter={o.RetryAfter}");

        // The hazard if a user rethrows the sentinel themselves (`throw outcome.Exception;`).
        try { ThrowFromAlpha(o.Exception); } catch { }
        var later = p.TryExecuteAsync(static (int s, CancellationToken _) => ValueTask.FromResult(s), 1, default).GetAwaiter().GetResult();
        Console.WriteLine($"- after ONE user `throw outcome.Exception`: every later Outcome.Exception carries that stack: {Has(later.Exception!.StackTrace, "ThrowFromAlpha")}");
        Console.WriteLine($"- GetResultOrThrow() instead: thrown is sentinel={ReferenceEquals(Catch(_ => later.GetResultOrThrow(), null!), Rejections.CircuitOpen)}");
    }
}

/// <summary>What every Microsoft.Extensions.Logging formatter does: message + exception.ToString().</summary>
public sealed class CaptureLogger : ILogger
{
    public string Last { get; private set; } = "";
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Last = formatter(state, exception) + Environment.NewLine + exception;
}
