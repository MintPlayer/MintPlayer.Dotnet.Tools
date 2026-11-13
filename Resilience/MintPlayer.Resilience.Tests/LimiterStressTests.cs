using MintPlayer.Resilience.RateLimiting;
using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

/// <summary>
/// Lock-free correctness of the limiters and the retry budget under real contention (a few seconds
/// each). Filter them out with <c>--filter "Category!=Stress"</c>.
/// </summary>
[Trait("Category", "Stress")]
public class LimiterStressTests
{
    private static int Threads => Math.Clamp(Environment.ProcessorCount, 4, 16);

    private static void RunConcurrently(Action<int> body)
    {
        using var start = new Barrier(Threads);
        var threads = Enumerable.Range(0, Threads).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            body(i);
        })).ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }
    }

    [Fact]
    public void NativeConcurrency_NeverExceedsItsLimit_AndReturnsEveryPermit()
    {
        const int limit = 3;
        var limiter = new NativeConcurrencyLimiter(limit);
        var current = 0;
        var peak = 0;
        var admitted = 0L;

        RunConcurrently(thread =>
        {
            for (var i = 0; i < 200_000; i++)
            {
                if (!limiter.TryAcquire(out _))
                {
                    continue;
                }

                var now = Interlocked.Increment(ref current);
                InterlockedMax(ref peak, now);
                Interlocked.Increment(ref admitted);
                Interlocked.Decrement(ref current);
                limiter.Release();
            }
        });

        peak.Should().BeLessThanOrEqualTo(limit);
        limiter.InFlight.Should().Be(0);
        admitted.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FixedWindow_FrozenClock_AdmitsExactlyThePermitLimit()
    {
        const int limit = 1_000;
        var limiter = new FixedWindowLimiter(new ManualClock(), limit, TimeSpan.FromSeconds(1));
        var admitted = 0;

        RunConcurrently(thread =>
        {
            for (var i = 0; i < 20_000; i++)
            {
                if (limiter.TryAcquire(out _))
                {
                    Interlocked.Increment(ref admitted);
                }
            }
        });

        admitted.Should().Be(limit);
    }

    [Fact]
    public void FixedWindow_MovingClock_NeverAdmitsMoreThanTheLimitPerWindow()
    {
        const int limit = 50;
        var clock = new ManualClock();
        var limiter = new FixedWindowLimiter(clock, limit, TimeSpan.FromMilliseconds(1));
        const int windows = 2_000;
        var perWindow = new int[windows + 2];
        var stop = 0;

        var ticker = new Thread(() =>
        {
            for (var w = 0; w < windows; w++)
            {
                Thread.SpinWait(2_000);
                clock.Advance(TimeSpan.FromMilliseconds(1));
            }

            Volatile.Write(ref stop, 1);
        });
        ticker.Start();

        RunConcurrently(thread =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                // Read the window after admission: the clock only moves forward, so an admission can only
                // be attributed to its own window or a later one, which can only under-count a window.
                if (limiter.TryAcquire(out _))
                {
                    var window = (int)(clock.GetTimestamp() / TimeSpan.TicksPerMillisecond);
                    Interlocked.Increment(ref perWindow[Math.Min(window, windows + 1)]);
                }
            }
        });
        ticker.Join();

        // Attribution may shift an admission into a later window, but each thread can have at most one
        // admission in flight between the limiter's clock read and ours: a window is over by at most that.
        foreach (var count in perWindow)
        {
            count.Should().BeLessThanOrEqualTo(limit + Threads);
        }

        perWindow.Sum().Should().BeGreaterThan(0);
    }

    [Fact]
    public void SlidingWindow_FrozenClock_NeverOverAdmits_AndLeaksNoTentativeCount()
    {
        const int limit = 1_000;
        var limiter = new SlidingWindowLimiter(new ManualClock(), limit, TimeSpan.FromSeconds(1), 10);
        var admitted = 0;

        RunConcurrently(thread =>
        {
            for (var i = 0; i < 20_000; i++)
            {
                if (limiter.TryAcquire(out _))
                {
                    Interlocked.Increment(ref admitted);
                }
            }
        });

        admitted.Should().BeLessThanOrEqualTo(limit);

        // Every refused call took its count back: the rest of the window can still be filled, exactly.
        while (limiter.TryAcquire(out _))
        {
            admitted++;
        }

        admitted.Should().Be(limit);
    }

    [Fact]
    public void SegmentedCounter_ConcurrentIncrementsAcrossSegments_AreAllCounted()
    {
        var counter = new Pipeline.SegmentedCounter(8);
        const int perThread = 100_000;

        RunConcurrently(i =>
        {
            for (var n = 0; n < perThread; n++)
            {
                // Every thread writes segments 100..107 in turn; none is older than the window of segment 107.
                while (!counter.TryIncrement(100 + (n % 8)))
                {
                }
            }
        });

        counter.Sum(107).Should().Be((long)Threads * perThread);
    }

    [Fact]
    public void RetryBudget_ConcurrentDepositsAndWithdrawals_NeverExceedTheAllowance()
    {
        var budget = new RetryBudget(retryRatio: 0.25, minRetriesPerSecond: 0, timeProvider: new ManualClock());
        var granted = 0;
        const int perThread = 50_000;

        RunConcurrently(thread =>
        {
            for (var i = 0; i < perThread; i++)
            {
                budget.Deposit();
                if (budget.TryWithdraw())
                {
                    Interlocked.Increment(ref granted);
                }
            }
        });

        var deposits = Threads * perThread;
        granted.Should().BeLessThanOrEqualTo(deposits / 4);

        // No refused withdrawal leaked: topping up single-threaded reaches the allowance exactly.
        while (budget.TryWithdraw())
        {
            granted++;
        }

        granted.Should().Be(deposits / 4);
    }

    [Fact]
    public void AdaptiveLimiter_UnderContention_StaysWithinBounds_AndReturnsEveryPermit()
    {
        var clock = new ManualClock();
        var controller = new AdaptiveLimitController(clock, AdaptiveConcurrencyAlgorithm.Gradient, 10, 2, 50, 0.9, 0.2, 1.5, 600, null);
        var current = 0;
        var peak = 0;

        RunConcurrently(thread =>
        {
            var random = new Random(thread);
            for (var i = 0; i < 100_000; i++)
            {
                if (!controller.TryAcquire(out var permit))
                {
                    continue;
                }

                var now = Interlocked.Increment(ref current);
                InterlockedMax(ref peak, now);
                if (random.Next(64) == 0)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(random.Next(1, 5)));
                }

                Interlocked.Decrement(ref current);
                controller.Release(permit, handled: random.Next(100) == 0);
            }
        });

        peak.Should().BeLessThanOrEqualTo(50);
        controller.InFlight.Should().Be(0);
        controller.ExactLimit.Should().BeGreaterThanOrEqualTo(2);
        controller.ExactLimit.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task NativeConcurrencyPipeline_ParallelSuspendingCalls_NeverExceedTheLimit()
    {
        const int limit = 4;
        var pipeline = new ResiliencePipelineBuilder<int>().AddNativeConcurrencyLimiter(limit).Build();
        var probe = new ConcurrencyProbe();

        var calls = Enumerable.Range(0, 2_000).Select(_ => Task.Run(async () =>
        {
            var outcome = await pipeline.TryExecuteAsync(
                static async (p, _) =>
                {
                    p.Enter();
                    await Task.Yield();
                    p.Exit();
                    return 1;
                },
                probe);
            return outcome.IsRejected ? 0 : 1;
        })).ToArray();

        var admitted = (await Task.WhenAll(calls)).Sum();

        probe.Peak.Should().BeLessThanOrEqualTo(limit);
        admitted.Should().BeGreaterThan(0);

        // Every permit came back.
        for (var i = 0; i < limit; i++)
        {
            (await pipeline.TryExecuteAsync(static (_, _) => new ValueTask<int>(1), 0)).IsRejected.Should().BeFalse();
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }

    private sealed class ConcurrencyProbe
    {
        private int _current;
        private int _peak;

        public int Peak => Volatile.Read(ref _peak);

        public void Enter() => InterlockedMax(ref _peak, Interlocked.Increment(ref _current));

        public void Exit() => Interlocked.Decrement(ref _current);
    }
}
