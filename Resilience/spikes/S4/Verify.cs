using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace S4;

public static class Verify
{
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        if (ok) return;
        _failures++;
        if (_failures <= 30) Console.WriteLine($"  FAIL: {what}");
    }

    public static async Task<int> RunAll(bool quick)
    {
        var sw = Stopwatch.StartNew();
        Deterministic("LockFree", (o, t) => new LockFreeCircuitBreaker(o, t));
        Deterministic("Locked", (o, t) => new LockedCircuitBreaker(o, t));
        SlowCalls();
        var lap = sw.Elapsed;
        void Lap() { Console.WriteLine($"    ({(sw.Elapsed - lap).TotalSeconds:F1} s)"); lap = sw.Elapsed; }
        await Parity(quick ? 300 : 3000); Lap();
        HalfOpenInstantFuzz(quick ? 2_000 : 20_000); Lap();
        ContinuousFuzz(quick ? 10 : 40); Lap();
        CounterFuzz(quick ? 3 : 12); Lap();
        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? $"ALL CHECKS PASSED ({sw.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ deterministic transitions

    private static readonly CbOptions Std = new(0.5, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

    private static void Deterministic(string name, Func<CbOptions, TimeProvider, ICircuitBreaker> make)
    {
        var before = _failures;
        void Fail(ICircuitBreaker b, int n) { for (var i = 0; i < n; i++) { b.TryEnter(); b.RecordFailure(); } }
        void Succeed(ICircuitBreaker b, int n) { for (var i = 0; i < n; i++) { b.TryEnter(); b.RecordSuccess(); } }

        {   // closed -> open -> half-open -> closed, single probe, metrics reset on close
            var t = new FakeTimeProvider(); var b = make(Std, t);
            Check(b.State == CbState.Closed, $"{name} T1 initial closed");
            Fail(b, 3);
            Check(b.State == CbState.Closed, $"{name} T1 below minimum throughput stays closed");
            Fail(b, 1);
            Check(b.State == CbState.Open, $"{name} T1 opens at min throughput + ratio");
            Check(b.TryEnter() == Admission.Rejected, $"{name} T1 open rejects");
            t.Advance(TimeSpan.FromMilliseconds(999));
            Check(b.TryEnter() == Admission.Rejected, $"{name} T1 rejects 1 ms before break ends");
            t.Advance(TimeSpan.FromMilliseconds(1));
            Check(b.TryEnter() == Admission.Probe, $"{name} T1 exactly at break end -> probe");
            Check(b.State == CbState.HalfOpen, $"{name} T1 half-open");
            Check(b.TryEnter() == Admission.Rejected, $"{name} T1 second caller rejected while probe outstanding");
            b.RecordSuccess();
            Check(b.State == CbState.Closed, $"{name} T1 probe success closes");
            Fail(b, 3); Succeed(b, 1);
            Check(b.State == CbState.Closed, $"{name} T1 window was reset on close (3F+1S, no break on success)");
            Fail(b, 1);
            Check(b.State == CbState.Open, $"{name} T1 4F/5 opens");
        }
        {   // half-open failure re-opens with a fresh break
            var t = new FakeTimeProvider(); var b = make(Std, t);
            Fail(b, 4); t.Advance(TimeSpan.FromSeconds(1));
            Check(b.TryEnter() == Admission.Probe, $"{name} T2 probe");
            t.Advance(TimeSpan.FromMilliseconds(300));
            b.RecordFailure();
            Check(b.State == CbState.Open, $"{name} T2 probe failure re-opens");
            t.Advance(TimeSpan.FromMilliseconds(999));
            Check(b.TryEnter() == Admission.Rejected, $"{name} T2 fresh break counted from the probe failure");
            t.Advance(TimeSpan.FromMilliseconds(1));
            Check(b.TryEnter() == Admission.Probe, $"{name} T2 next probe");
        }
        {   // sliding window: events older than the sampling duration drop out
            var t = new FakeTimeProvider(); var b = make(Std, t);
            Fail(b, 3); t.Advance(TimeSpan.FromMilliseconds(1000)); Fail(b, 1);
            Check(b.State == CbState.Closed, $"{name} T3 failures 1000 ms old expired");
            var t2 = new FakeTimeProvider(); var b2 = make(Std, t2);
            Fail(b2, 3); t2.Advance(TimeSpan.FromMilliseconds(999)); Fail(b2, 1);
            Check(b2.State == CbState.Open, $"{name} T3 failures 999 ms old still count");
            // Bucket anchoring (Polly starts a window at the first event, not on a 100 ms grid):
            // 2F @0, then 1F+2S @150 (window anchored at 150), then 1F @1120. The window @0 expired;
            // the one @150 is alive until 1150 (a grid bucket [100,200) would be gone at 1100) => 2F/4 opens.
            var t3 = new FakeTimeProvider(); var b3 = make(Std, t3);
            Fail(b3, 2); t3.Advance(TimeSpan.FromMilliseconds(150)); Fail(b3, 1); Succeed(b3, 2);
            t3.Advance(TimeSpan.FromMilliseconds(970)); Fail(b3, 1);
            Check(b3.State == CbState.Open, $"{name} T3 event-anchored window @150 still alive at 1120 ms");
        }
        {   // isolate / manual close
            var t = new FakeTimeProvider(); var b = make(Std, t);
            b.Isolate();
            Check(b.State == CbState.Isolated, $"{name} T4 isolated");
            t.Advance(TimeSpan.FromHours(1));
            Check(b.TryEnter() == Admission.Rejected, $"{name} T4 isolated rejects forever");
            b.Close();
            Check(b.State == CbState.Closed && b.TryEnter() == Admission.Closed, $"{name} T4 manual close");
            Fail(b, 4); b.Close(); Fail(b, 3);
            Check(b.State == CbState.Closed, $"{name} T4 manual close resets window");
        }
        {   // late success during half-open closes (Polly semantics); results during open change nothing
            var t = new FakeTimeProvider(); var b = make(Std, t);
            Fail(b, 4);
            b.RecordSuccess(); b.RecordFailure();
            Check(b.State == CbState.Open, $"{name} T5 late results while open: no transition");
            t.Advance(TimeSpan.FromSeconds(1));
            Check(b.TryEnter() == Admission.Probe, $"{name} T5 probe");
            b.RecordSuccess(); // a call admitted before the break finishing now
            Check(b.State == CbState.Closed, $"{name} T5 late success closes half-open circuit");
        }
        Console.WriteLine($"deterministic transitions [{name}]: {(_failures == before ? "pass" : "FAIL")}");
    }

    private static void SlowCalls()
    {
        var before = _failures;
        var o = Std with { SlowCallThreshold = TimeSpan.FromMilliseconds(100), SlowCallRatio = 0.5 };
        var t = new FakeTimeProvider(); var b = new LockFreeCircuitBreaker(o, t);
        for (var i = 0; i < 3; i++) { b.TryEnter(); b.RecordSuccess(TimeSpan.FromMilliseconds(200)); }
        Check(b.State == CbState.Closed, "slow: below min throughput");
        b.TryEnter(); b.RecordSuccess(TimeSpan.FromMilliseconds(10));
        Check(b.State == CbState.Closed, "slow: fast success never trips");
        b.TryEnter(); b.RecordSuccess(TimeSpan.FromMilliseconds(100));
        Check(b.State == CbState.Open, "slow: 4 slow of 5 (>= threshold is slow) trips on slow SUCCESSES");
        t.Advance(TimeSpan.FromSeconds(1));
        Check(b.TryEnter() == Admission.Probe, "slow: probe");
        b.RecordSuccess(TimeSpan.FromMilliseconds(500));
        Check(b.State == CbState.Open, "slow: slow probe = failed probe");
        t.Advance(TimeSpan.FromSeconds(1));
        b.TryEnter(); b.RecordSuccess(TimeSpan.FromMilliseconds(5));
        Check(b.State == CbState.Closed, "slow: fast probe closes");
        Check(b.Health() == (0, 0, 0), "slow: window reset on close");
        Console.WriteLine($"slow-call extension (lock-free): {(_failures == before ? "pass" : "FAIL")}");
    }

    // ------------------------------------------------------------------ parity with Polly

    private static async Task Parity(int scripts)
    {
        var before = _failures;
        long ops = 0, transitions = 0;
        for (var seed = 0; seed < scripts; seed++)
        {
            var rng = new Random(seed);
            var o = new CbOptions(
                FailureRatio: new[] { 0.1, 0.5, 0.75, 1.0 }[rng.Next(4)],
                MinimumThroughput: new[] { 2, 4, 10 }[rng.Next(3)],
                SamplingDuration: TimeSpan.FromMilliseconds(new[] { 500, 1000, 3000 }[rng.Next(3)]),
                BreakDuration: TimeSpan.FromMilliseconds(new[] { 500, 1000 }[rng.Next(2)]));
            var time = new FakeTimeProvider();
            var polly = new PollyBreaker(o, time);
            var free = new LockFreeCircuitBreaker(o, time, stripes: 1 + rng.Next(8));
            var locked = new LockedCircuitBreaker(o, time);
            var inflight = new List<(int PollyId, bool FreeIn, bool LockedIn)>();
            var trace = new List<string>();
            var last = CbState.Closed;
            var failP = 30 + rng.Next(50);

            for (var step = 0; step < 250; step++)
            {
                ops++;
                var r = rng.Next(100);
                string op;
                if (r < 55)
                {
                    var ok = rng.Next(100) >= failP;
                    op = ok ? "call S" : "call F";
                    var p = await polly.Call(ok);
                    var f = Enter(free, ok); var l = Enter(locked, ok);
                    Check(p == f && p == l, $"parity seed {seed} step {step}: admitted polly={p} free={f} locked={l}");
                }
                else if (r < 63 && inflight.Count < 3)
                {
                    op = "begin";
                    var id = await polly.Begin();
                    var f = free.TryEnter() != Admission.Rejected; var l = locked.TryEnter() != Admission.Rejected;
                    Check((id >= 0) == f && f == l, $"parity seed {seed} step {step}: begin admitted polly={id >= 0} free={f} locked={l}");
                    if (id >= 0 && f && l) inflight.Add((id, f, l));
                }
                else if (r < 71 && inflight.Count > 0)
                {
                    var ok = rng.Next(100) >= failP;
                    op = ok ? "end S" : "end F";
                    var i = rng.Next(inflight.Count);
                    await polly.End(inflight[i].PollyId, ok);
                    if (ok) { free.RecordSuccess(); locked.RecordSuccess(); } else { free.RecordFailure(); locked.RecordFailure(); }
                    inflight.RemoveAt(i);
                }
                else if (r < 97)
                {
                    var ms = rng.Next(4) switch { 0 => rng.Next(1, 20), 1 => rng.Next(20, 200), 2 => (int)o.BreakDuration.TotalMilliseconds, _ => rng.Next(200, 1500) };
                    op = $"+{ms}ms";
                    time.Advance(TimeSpan.FromMilliseconds(ms));
                }
                else if (r < 98) { op = "isolate"; await polly.Isolate(); free.Isolate(); locked.Isolate(); }
                else { op = "close"; await polly.Close(); free.Close(); locked.Close(); }

                trace.Add(op);
                var ps = polly.State;
                if (ps != last) { transitions++; last = ps; }
                if (ps != free.State || ps != locked.State)
                {
                    Check(false, $"parity seed {seed} step {step} after '{op}': polly={ps} free={free.State} locked={locked.State}; last ops: {string.Join(", ", trace.TakeLast(12))}");
                    break;
                }
            }

            foreach (var x in inflight) await polly.End(x.PollyId, true); // drain
        }
        Console.WriteLine($"parity vs Polly 8.8.0: {scripts} scripts, {ops:N0} ops, {transitions:N0} observed state changes: {(_failures == before ? "identical state + admission sequence" : "FAIL")}");

        static bool Enter(ICircuitBreaker b, bool ok)
        {
            if (b.TryEnter() == Admission.Rejected) return false;
            if (ok) b.RecordSuccess(); else b.RecordFailure();
            return true;
        }
    }

    // ------------------------------------------------------------------ fuzz A: the half-open instant

    private static int Threads => Math.Clamp(Environment.ProcessorCount, 4, 16);

    /// <summary>
    /// Each round: trip the breaker, move time exactly to the end of the break, release all threads at
    /// once through a barrier, each hammering TryEnter. Exactly one Probe, zero Closed admissions.
    /// </summary>
    private static void HalfOpenInstantFuzz(int rounds)
    {
        var before = _failures;
        const int PerThread = 8;
        var n = Threads;
        var clock = new ManualClock();
        LockFreeCircuitBreaker breaker = null!;
        int probes = 0, closedAdmits = 0;
        using var barrier = new Barrier(n + 1);
        var workers = Enumerable.Range(0, n).Select(_ => new Thread(() =>
        {
            for (var r = 0; r < rounds; r++)
            {
                barrier.SignalAndWait();
                var b = Volatile.Read(ref breaker);
                for (var i = 0; i < PerThread; i++)
                {
                    var a = b.TryEnter();
                    if (a == Admission.Probe) Interlocked.Increment(ref probes);
                    else if (a == Admission.Closed) Interlocked.Increment(ref closedAdmits);
                }
                barrier.SignalAndWait();
            }
        })).ToList();
        workers.ForEach(w => w.Start());

        long calls = 0;
        for (var r = 0; r < rounds; r++)
        {
            var b = new LockFreeCircuitBreaker(Std, clock);
            for (var i = 0; i < 4; i++) { b.TryEnter(); b.RecordFailure(); }
            clock.Advance(Std.BreakDuration); // exactly the half-open instant
            probes = 0; closedAdmits = 0;
            Volatile.Write(ref breaker, b);
            barrier.SignalAndWait(); // go
            barrier.SignalAndWait(); // done
            calls += n * PerThread;
            Check(probes == 1, $"fuzzA round {r}: {probes} probes admitted");
            Check(closedAdmits == 0, $"fuzzA round {r}: {closedAdmits} closed admissions during half-open");
            Check(b.State == CbState.HalfOpen, $"fuzzA round {r}: state {b.State}");
        }
        workers.ForEach(w => w.Join());
        Console.WriteLine($"fuzz A (half-open instant): {rounds:N0} rounds x {n} threads x {PerThread} = {calls:N0} racing TryEnter: {(_failures == before ? "exactly 1 probe every round" : "FAIL")}");
    }

    // ------------------------------------------------------------------ fuzz B: continuous chaos + transition chain

    /// <summary>
    /// Threads hammer TryEnter/Record with 55 % failures, random delays (late outcomes), clock jumps from
    /// every thread, and rare Isolate/Close. Per round every successful CAS is logged (from, to) by its
    /// winner; afterwards the log must form ONE unbroken chain from the round's start word to its end
    /// word (no lost, duplicated or phantom transitions), every step must be legal with gen+1, probes ==
    /// Open->HalfOpen transitions, and the circuit may not be left HalfOpen (every probe recorded, so a
    /// lingering HalfOpen would be a lost transition).
    /// </summary>
    private static void ContinuousFuzz(int rounds)
    {
        var before = _failures;
        var n = Threads;
        const int OpsPerThread = 4_000;
        var o = new CbOptions(0.5, 4, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        var clock = new ManualClock();
        var breaker = new LockFreeCircuitBreaker(o, clock);
        var logs = new ThreadLocal<List<(long From, long To)>>(() => new(), trackAllValues: true);
        breaker.OnTransition = (f, t) => logs.Value!.Add((f, t));
        long totalOps = 0, totalTransitions = 0, totalProbes = 0, maxRound = 0;

        for (var round = 0; round < rounds; round++)
        {
            foreach (var l in logs.Values) l.Clear();
            var startWord = breaker.StateWord;
            long probes = 0;
            var threads = Enumerable.Range(0, n).Select(i => new Thread(() =>
            {
                var rng = new Random(round * 1000 + i);
                for (var k = 0; k < OpsPerThread; k++)
                {
                    var r = rng.Next(10_000);
                    if (r < 300) { clock.Advance(TimeSpan.FromMilliseconds(rng.Next(0, 300))); continue; }
                    if (r == 300) { breaker.Isolate(); continue; }
                    if (r < 310) { breaker.Close(); continue; }

                    var a = breaker.TryEnter();
                    if (a == Admission.Rejected) continue;
                    if (a == Admission.Probe) Interlocked.Increment(ref probes);
                    if (rng.Next(4) == 0) Thread.SpinWait(rng.Next(1, 200)); // late outcome
                    if (rng.Next(100) < 55) breaker.RecordFailure(); else breaker.RecordSuccess();
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            var endWord = breaker.StateWord;
            var all = logs.Values.SelectMany(l => l).ToList();
            totalOps += (long)n * OpsPerThread; totalTransitions += all.Count; totalProbes += probes;
            maxRound = Math.Max(maxRound, all.Count);
            Check(all.Count < 16_000, $"fuzzB round {round}: {all.Count} transitions (gen wraps at 16384; chain check would be ambiguous)");

            var byFrom = new Dictionary<long, long>();
            foreach (var (f, t) in all)
            {
                Check(byFrom.TryAdd(f, t), $"fuzzB round {round}: two transitions out of the same word {Describe(f)}");
                Check(LockFreeCircuitBreaker.GenOf(t) == ((LockFreeCircuitBreaker.GenOf(f) + 1) & 0x3FFF), $"fuzzB round {round}: gen not +1");
                Check(Legal(LockFreeCircuitBreaker.StateOf(f), LockFreeCircuitBreaker.StateOf(t)), $"fuzzB round {round}: illegal {Describe(f)} -> {Describe(t)}");
            }
            var w = startWord; var steps = 0;
            while (byFrom.TryGetValue(w, out var next) && steps <= all.Count) { w = next; steps++; }
            Check(steps == all.Count && w == endWord, $"fuzzB round {round}: chain broken ({steps}/{all.Count} steps, ended {Describe(w)} vs final {Describe(endWord)})");
            var halfOpens = all.Count(x => LockFreeCircuitBreaker.StateOf(x.From) == CbState.Open && LockFreeCircuitBreaker.StateOf(x.To) == CbState.HalfOpen);
            Check(halfOpens == probes, $"fuzzB round {round}: {probes} probes vs {halfOpens} Open->HalfOpen transitions");
            Check(LockFreeCircuitBreaker.StateOf(endWord) != CbState.HalfOpen, $"fuzzB round {round}: left HalfOpen after all probes recorded (lost transition)");
        }
        Console.WriteLine($"fuzz B (continuous): {rounds} rounds x {n} threads, {totalOps:N0} ops, {totalTransitions:N0} transitions ({totalProbes:N0} probes, max {maxRound:N0}/round): {(_failures == before ? "one unbroken legal chain per round" : "FAIL")}");

        static bool Legal(CbState f, CbState t) => (f, t) switch
        {
            (_, CbState.Isolated) => true,  // manual
            (_, CbState.Closed) => true,    // HalfOpen -> Closed, or manual close from anything
            (CbState.Closed, CbState.Open) => true,
            (CbState.HalfOpen, CbState.Open) => true,
            (CbState.Open, CbState.HalfOpen) => true,
            _ => false,
        };
        static string Describe(long w) => $"{LockFreeCircuitBreaker.StateOf(w)}#{LockFreeCircuitBreaker.GenOf(w)}";
    }

    // ------------------------------------------------------------------ fuzz C: counters across window roll-overs

    /// <summary>
    /// Threads record outcomes while another thread moves time through 9 window boundaries (sampling 1 s,
    /// windows 100 ms, total advance 850 ms so nothing expires). Minimum throughput is unreachable, so the
    /// circuit never trips: the window totals must equal exactly what was recorded - no increment lost to
    /// a concurrent window start.
    /// </summary>
    private static void CounterFuzz(int rounds)
    {
        var before = _failures;
        var n = Threads;
        const int PerThread = 25_000;
        long total = 0;
        for (var round = 0; round < rounds; round++)
        {
            var clock = new ManualClock();
            var o = new CbOptions(1.0, int.MaxValue, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            var b = new LockFreeCircuitBreaker(o, clock, stripes: round % 2 == 0 ? 1 : 0); // 1 stripe = every thread on one counter
            long succ = 0, fail = 0;
            var go = new ManualResetEventSlim();
            var threads = Enumerable.Range(0, n).Select(i => new Thread(() =>
            {
                var rng = new Random(round * 100 + i); long s = 0, f = 0;
                go.Wait();
                for (var k = 0; k < PerThread; k++)
                    if (rng.Next(10) == 0) { b.RecordFailure(); f++; } else { b.RecordSuccess(); s++; }
                Interlocked.Add(ref succ, s); Interlocked.Add(ref fail, f);
            })).ToList();
            var advancer = new Thread(() =>
            {
                go.Wait();
                for (var ms = 0; ms < 850; ms += 5) { clock.Advance(TimeSpan.FromMilliseconds(5)); Thread.SpinWait(2_000); }
            });
            threads.ForEach(t => t.Start()); advancer.Start();
            go.Set();
            threads.ForEach(t => t.Join()); advancer.Join();
            var (hs, hf, _) = b.Health();
            Check(hs == succ && hf == fail, $"fuzzC round {round}: recorded {succ}S/{fail}F, window holds {hs}S/{hf}F");
            Check(b.State == CbState.Closed, $"fuzzC round {round}: tripped?");
            total += succ + fail;
        }
        Console.WriteLine($"fuzz C (counters across window starts): {rounds} rounds (half with 1 stripe), {total:N0} records: {(_failures == before ? "no lost increment" : "FAIL")}");
    }
}
