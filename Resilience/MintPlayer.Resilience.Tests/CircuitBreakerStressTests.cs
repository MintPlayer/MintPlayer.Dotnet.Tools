using MintPlayer.Resilience.CircuitBreaker;

namespace MintPlayer.Resilience.Tests;

/// <summary>A clock that only moves when told to; safe to advance from many threads.</summary>
internal sealed class ManualClock : TimeProvider
{
    private static readonly long Epoch = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Volatile.Read(ref _ticks);

    public override DateTimeOffset GetUtcNow() => new(Epoch + Volatile.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>
/// The race fuzzers of spike S4 at a bounded size (a few seconds each). Filter them out with
/// <c>--filter "Category!=Stress"</c>.
/// </summary>
[Trait("Category", "Stress")]
public class CircuitBreakerStressTests
{
    private static int Threads => Math.Clamp(Environment.ProcessorCount, 4, 16);

    private static CircuitController Std(TimeProvider time, int stripes = 0) => new(time, 0.5, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), stripes: stripes);

    private static void Trip(CircuitController breaker)
    {
        for (var i = 0; i < 4; i++)
        {
            breaker.TryEnter(out _);
            breaker.RecordFailure(null, CircuitController.NotMeasured, null);
        }
    }

    /// <summary>
    /// Fuzz A. Each round: trip the breaker, move time exactly to the end of the break, release every
    /// thread through a barrier, each hammering TryEnter. Exactly one probe and no closed admission.
    /// </summary>
    [Fact]
    public void HalfOpenInstant_AdmitsExactlyOneProbe()
    {
        const int Rounds = 2_000;
        const int PerThread = 8;
        var n = Threads;
        var clock = new ManualClock();
        CircuitController breaker = null!;
        int probes = 0, closedAdmits = 0;
        var bad = new List<string>();
        using var barrier = new Barrier(n + 1);
        var workers = Enumerable.Range(0, n).Select(_ => new Thread(() =>
        {
            for (var r = 0; r < Rounds; r++)
            {
                barrier.SignalAndWait();
                var b = Volatile.Read(ref breaker);
                for (var i = 0; i < PerThread; i++)
                {
                    var a = b.TryEnter(out var _);
                    if (a == Admission.Probe)
                    {
                        Interlocked.Increment(ref probes);
                    }
                    else if (a == Admission.Closed)
                    {
                        Interlocked.Increment(ref closedAdmits);
                    }
                }

                barrier.SignalAndWait();
            }
        })
        { IsBackground = true }).ToList();
        workers.ForEach(w => w.Start());

        for (var r = 0; r < Rounds; r++)
        {
            var b = Std(clock);
            Trip(b);
            clock.Advance(TimeSpan.FromSeconds(1)); // exactly the half-open instant
            probes = 0;
            closedAdmits = 0;
            Volatile.Write(ref breaker, b);
            barrier.SignalAndWait(); // go
            barrier.SignalAndWait(); // done
            if (probes != 1 || closedAdmits != 0 || b.State != CircuitState.HalfOpen)
            {
                bad.Add($"round {r}: {probes} probes, {closedAdmits} closed admissions, state {b.State}");
            }
        }

        workers.ForEach(w => w.Join());
        bad.Should().Equal([]);
    }

    /// <summary>The same race through the pipeline: many concurrent executions at the half-open instant run the callback once.</summary>
    [Fact]
    public async Task HalfOpenInstant_ThroughThePipeline_RunsOneProbeCallback()
    {
        const int Rounds = 200;
        var n = Threads * 4;
        var bad = new List<string>();
        for (var round = 0; round < Rounds; round++)
        {
            var clock = new ManualClock();
            var halfOpened = 0;
            var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = clock }
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 2,
                    BreakDuration = TimeSpan.FromSeconds(1),
                    OnHalfOpened = _ =>
                    {
                        Interlocked.Increment(ref halfOpened);
                        return default;
                    },
                })
                .Build();
            for (var i = 0; i < 2; i++)
            {
                await pipeline.TryExecuteAsync(static _ => ValueTask.FromException<int>(new InvalidOperationException()));
            }

            clock.Advance(TimeSpan.FromSeconds(1));
            var ran = 0;
            var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var start = new ManualResetEventSlim();
            var calls = Enumerable.Range(0, n).Select(_ => Task.Run(async () =>
            {
                start.Wait();
                return await pipeline.TryExecuteAsync(async _ =>
                {
                    Interlocked.Increment(ref ran);
                    return await gate.Task;
                });
            })).ToArray();
            start.Set();

            // Every rejected call completes on its own; the probe waits for the gate. (A second probe
            // would also wait: the deadline turns that into a failed round instead of a hang.)
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (calls.Count(c => c.IsCompleted) < n - 1 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(1);
            }

            gate.SetResult(1);
            var outcomes = await Task.WhenAll(calls);
            var admitted = outcomes.Count(o => o.IsSuccess);
            if (ran != 1 || admitted != 1 || halfOpened != 1 || outcomes.Count(o => o.Rejection == RejectionKind.CircuitOpen) != n - 1)
            {
                bad.Add($"round {round}: ran {ran}, admitted {admitted}, half-opened {halfOpened}");
            }
        }

        bad.Should().Equal([]);
    }

    /// <summary>
    /// Fuzz B. Threads hammer TryEnter/Record with 55 % failures, random delays (late outcomes), clock
    /// jumps from every thread, and rare Isolate/Close. Every successful CAS is logged (from, to) by its
    /// winner; the log must form ONE unbroken chain from the round's start word to its end word, every
    /// step legal with gen + 1, probes == Open → HalfOpen transitions, and the circuit not left half-open.
    /// </summary>
    [Fact]
    public void ContinuousChaos_FormsOneLegalChainOfTransitions()
    {
        const int Rounds = 10;
        const int OpsPerThread = 4_000;
        var n = Threads;
        var clock = new ManualClock();
        var breaker = new CircuitController(clock, 0.5, 4, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        using var logs = new ThreadLocal<List<(long From, long To)>>(() => [], trackAllValues: true);
        breaker.OnTransition = (f, t) => logs.Value!.Add((f, t));
        var bad = new List<string>();

        for (var round = 0; round < Rounds; round++)
        {
            foreach (var l in logs.Values)
            {
                l.Clear();
            }

            var startWord = breaker.StateWord;
            long probes = 0;
            var threads = Enumerable.Range(0, n).Select(i => new Thread(() =>
            {
                var rng = new Random((round * 1000) + i);
                for (var k = 0; k < OpsPerThread; k++)
                {
                    var r = rng.Next(10_000);
                    if (r < 300)
                    {
                        clock.Advance(TimeSpan.FromMilliseconds(rng.Next(0, 300)));
                        continue;
                    }

                    if (r == 300)
                    {
                        breaker.Isolate();
                        continue;
                    }

                    if (r < 310)
                    {
                        breaker.Close();
                        continue;
                    }

                    var a = breaker.TryEnter(out _);
                    if (a == Admission.Rejected)
                    {
                        continue;
                    }

                    if (a == Admission.Probe)
                    {
                        Interlocked.Increment(ref probes);
                    }

                    if (rng.Next(4) == 0)
                    {
                        Thread.SpinWait(rng.Next(1, 200)); // late outcome
                    }

                    if (rng.Next(100) < 55)
                    {
                        breaker.RecordFailure(null, CircuitController.NotMeasured, null);
                    }
                    else
                    {
                        breaker.RecordSuccess(CircuitController.NotMeasured, null);
                    }
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            var endWord = breaker.StateWord;
            var all = logs.Values.SelectMany(l => l).ToList();
            if (all.Count >= 16_000)
            {
                bad.Add($"round {round}: {all.Count} transitions (the generation wraps at 16384; the chain check would be ambiguous)");
                continue;
            }

            var byFrom = new Dictionary<long, long>();
            foreach (var (f, t) in all)
            {
                if (!byFrom.TryAdd(f, t))
                {
                    bad.Add($"round {round}: two transitions out of {Describe(f)}");
                }

                if (CircuitController.GenOf(t) != ((CircuitController.GenOf(f) + 1) & CircuitController.GenMask))
                {
                    bad.Add($"round {round}: generation not +1 in {Describe(f)} -> {Describe(t)}");
                }

                if (!Legal(CircuitController.StateOf(f), CircuitController.StateOf(t)))
                {
                    bad.Add($"round {round}: illegal {Describe(f)} -> {Describe(t)}");
                }
            }

            var w = startWord;
            var steps = 0;
            while (byFrom.TryGetValue(w, out var next) && steps <= all.Count)
            {
                w = next;
                steps++;
            }

            if (steps != all.Count || w != endWord)
            {
                bad.Add($"round {round}: chain broken ({steps}/{all.Count} steps, ended {Describe(w)} vs final {Describe(endWord)})");
            }

            var halfOpens = all.Count(x => CircuitController.StateOf(x.From) == CircuitState.Open && CircuitController.StateOf(x.To) == CircuitState.HalfOpen);
            if (halfOpens != probes)
            {
                bad.Add($"round {round}: {probes} probes vs {halfOpens} Open -> HalfOpen transitions");
            }

            if (CircuitController.StateOf(endWord) == CircuitState.HalfOpen)
            {
                bad.Add($"round {round}: left half-open after every probe recorded (a lost transition)");
            }
        }

        bad.Should().Equal([]);

        static bool Legal(CircuitState from, CircuitState to) => (from, to) switch
        {
            (_, CircuitState.Isolated) => true, // manual
            (CircuitState.Closed, CircuitState.Closed) => false, // a close from closed is not a transition
            (_, CircuitState.Closed) => true, // HalfOpen -> Closed, or a manual close
            (CircuitState.Closed, CircuitState.Open) => true,
            (CircuitState.HalfOpen, CircuitState.Open) => true,
            (CircuitState.Open, CircuitState.HalfOpen) => true,
            _ => false,
        };

        static string Describe(long word) => $"{CircuitController.StateOf(word)}#{CircuitController.GenOf(word)}";
    }

    /// <summary>
    /// Fuzz C. Threads record outcomes while another thread moves time through 9 window starts (sampling
    /// 1 s, windows 100 ms, total advance 850 ms, so nothing expires). The minimum throughput is
    /// unreachable, so the circuit never trips: the window must hold exactly what was recorded.
    /// </summary>
    [Fact]
    public void CountersAcrossWindowStarts_LoseNoIncrement()
    {
        const int Rounds = 4;
        const int PerThread = 25_000;
        var n = Threads;
        var bad = new List<string>();
        for (var round = 0; round < Rounds; round++)
        {
            var clock = new ManualClock();
            var b = new CircuitController(clock, 1.0, int.MaxValue, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), stripes: round % 2 == 0 ? 1 : 0);
            long succ = 0, fail = 0;
            using var go = new ManualResetEventSlim();
            var threads = Enumerable.Range(0, n).Select(i => new Thread(() =>
            {
                var rng = new Random((round * 100) + i);
                long s = 0, f = 0;
                go.Wait();
                for (var k = 0; k < PerThread; k++)
                {
                    if (rng.Next(10) == 0)
                    {
                        b.RecordFailure(null, CircuitController.NotMeasured, null);
                        f++;
                    }
                    else
                    {
                        b.RecordSuccess(CircuitController.NotMeasured, null);
                        s++;
                    }
                }

                Interlocked.Add(ref succ, s);
                Interlocked.Add(ref fail, f);
            })).ToList();
            var advancer = new Thread(() =>
            {
                go.Wait();
                for (var ms = 0; ms < 850; ms += 5)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(5));
                    Thread.SpinWait(2_000);
                }
            });
            threads.ForEach(t => t.Start());
            advancer.Start();
            go.Set();
            threads.ForEach(t => t.Join());
            advancer.Join();

            var (hs, hf, _) = b.Health();
            if (hs != succ || hf != fail || b.State != CircuitState.Closed)
            {
                bad.Add($"round {round}: recorded {succ}S/{fail}F, window holds {hs}S/{hf}F, state {b.State}");
            }
        }

        bad.Should().Equal([]);
    }
}
