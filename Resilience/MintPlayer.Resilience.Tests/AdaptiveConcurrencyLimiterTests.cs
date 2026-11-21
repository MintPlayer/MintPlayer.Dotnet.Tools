using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.RateLimiting;

namespace MintPlayer.Resilience.Tests;

/// <summary>The adaptive concurrency limiter: admission, the two algorithms, and convergence under a simulated service.</summary>
public class AdaptiveConcurrencyLimiterTests
{
    private static readonly Func<TaskCompletionSource<int>, CancellationToken, ValueTask<int>> Gated = static (gate, _) => new ValueTask<int>(gate.Task);

    private static ValueTask<int> Ok(CancellationToken _) => new(1);

    private static AdaptiveLimitController Controller(
        AdaptiveConcurrencyAlgorithm algorithm = AdaptiveConcurrencyAlgorithm.Aimd,
        int initial = 10,
        int min = 1,
        int max = 1000,
        TimeSpan? threshold = null,
        TimeProvider? time = null)
        => new(time ?? new FakeTimeProvider(), algorithm, initial, min, max, backoffRatio: 0.9, smoothing: 0.2, tolerance: 1.5, latencyWindow: 600, threshold);

    // ---- The controller ----

    [Fact]
    public void Admission_StopsAtTheLimit_AndReleaseFreesAPermit()
    {
        var controller = Controller(initial: 3);
        var permits = new List<AdaptivePermit>();
        for (var i = 0; i < 3; i++)
        {
            controller.TryAcquire(out var permit).Should().BeTrue();
            permits.Add(permit);
        }

        controller.TryAcquire(out _).Should().BeFalse();
        controller.InFlight.Should().Be(3);

        controller.ReleaseWithoutSample();
        controller.InFlight.Should().Be(2);
        controller.TryAcquire(out _).Should().BeTrue();
    }

    [Fact]
    public void Aimd_ABurstOfDrops_BacksOffOnce()
    {
        var controller = Controller(initial: 10);
        var permits = new AdaptivePermit[10];
        for (var i = 0; i < 10; i++)
        {
            controller.TryAcquire(out permits[i]).Should().BeTrue();
        }

        foreach (var permit in permits)
        {
            controller.Release(permit, handled: true);
        }

        controller.ExactLimit.Should().Be(9);
        controller.Generation.Should().Be(1);
        controller.InFlight.Should().Be(0);

        // A call admitted after the backoff can back off again.
        controller.TryAcquire(out var next).Should().BeTrue();
        controller.Release(next, handled: true);
        controller.ExactLimit.Should().Be(9 * 0.9);
        controller.Generation.Should().Be(2);
    }

    [Fact]
    public void Aimd_Successes_AddOneOverLimit_WhileAtLeastHalfUsed()
    {
        var controller = Controller(initial: 4);
        var permits = new AdaptivePermit[4];
        for (var i = 0; i < 4; i++)
        {
            controller.TryAcquire(out permits[i]);
        }

        foreach (var permit in permits)
        {
            controller.Release(permit, handled: false);
        }

        // 4 → +1/4 → +1/4.25 → +1/4.485 → +1/4.708: about +0.92 for a full limit's worth.
        var expected = 4.0;
        for (var i = 0; i < 4; i++)
        {
            expected += 1 / expected;
        }

        controller.ExactLimit.Should().Be(expected);
    }

    [Fact]
    public void Successes_WhileApplicationLimited_DoNotRaiseTheLimit()
    {
        var controller = Controller(initial: 10);
        for (var i = 0; i < 100; i++)
        {
            controller.TryAcquire(out var permit);
            controller.Release(permit, handled: false);
        }

        controller.ExactLimit.Should().Be(10);
    }

    [Fact]
    public void Limit_StaysWithinMinAndMax()
    {
        var controller = Controller(initial: 2, min: 2, max: 3);
        for (var i = 0; i < 50; i++)
        {
            controller.Sample(1000, dropped: false, inFlight: 3, generation: controller.Generation);
        }

        controller.ExactLimit.Should().Be(3);

        for (var i = 0; i < 50; i++)
        {
            controller.Sample(1000, dropped: true, inFlight: 3, generation: controller.Generation);
        }

        controller.ExactLimit.Should().Be(2);
    }

    [Fact]
    public void LatencyThreshold_MakesASlowSuccessADrop()
    {
        var time = new FakeTimeProvider();
        var controller = Controller(initial: 10, threshold: TimeSpan.FromMilliseconds(100), time: time);

        controller.TryAcquire(out var permit);
        time.Advance(TimeSpan.FromMilliseconds(101));
        controller.Release(permit, handled: false);

        controller.ExactLimit.Should().Be(9);
    }

    [Fact]
    public void Gradient_GrowsWhileLatencyIsAtItsBaseline_AndShrinksWhenItRises()
    {
        var controller = Controller(AdaptiveConcurrencyAlgorithm.Gradient, initial: 16);

        controller.Sample(1000, dropped: false, inFlight: 16, generation: 0);
        controller.NoLoadLatencyMicros.Should().Be(1000);
        controller.ExactLimit.Should().Be(16 + (Math.Sqrt(16) * 0.2 / 16));      // gradient 1: target = limit + √limit

        var before = controller.ExactLimit;
        controller.Sample(4000, dropped: false, inFlight: 16, generation: 0);    // gradient clamp(1.5 / 4) = 0.5
        var target = before * 0.5 + Math.Sqrt(before);
        controller.ExactLimit.Should().Be(before + (target - before) * 0.2 / before);
        controller.NoLoadLatencyMicros.Should().Be(1000);                        // loaded samples never raise it
    }

    [Fact]
    public void Gradient_NoLoadLatency_FollowsALowerBaselineAtOnce()
    {
        var controller = Controller(AdaptiveConcurrencyAlgorithm.Gradient, initial: 16);
        controller.Sample(5000, dropped: false, inFlight: 16, generation: 0);
        controller.Sample(2000, dropped: false, inFlight: 16, generation: 0);
        controller.NoLoadLatencyMicros.Should().Be(2000);
    }

    [Fact]
    public void Gradient_AtItsFloor_ForgetsTheNoLoadLatencySlowly()
    {
        var controller = Controller(AdaptiveConcurrencyAlgorithm.Gradient, initial: 4, min: 4, max: 4);
        controller.Sample(1000, dropped: false, inFlight: 4, generation: 0);
        controller.Sample(3000, dropped: false, inFlight: 4, generation: 0);
        controller.NoLoadLatencyMicros.Should().Be(1000 * (1 + 1.0 / 600));
    }

    // ---- Convergence under a simulated service ----

    /// <summary>
    /// A service of capacity 20: up to 20 concurrent calls take 10 ms; beyond that the calls queue and
    /// latency grows linearly (10 ms × n / 20). Each round offers 100 calls at once, admits what the
    /// limiter allows, advances the clock by the latency, and completes them all. Returns the number
    /// admitted per round.
    /// </summary>
    private static async Task<List<int>> Simulate(AdaptiveConcurrencyLimiterStrategyOptions<int> options, int rounds)
    {
        const int capacity = 20;
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddAdaptiveConcurrencyLimiter(options).Build();
        var admittedPerRound = new List<int>();
        for (var round = 0; round < rounds; round++)
        {
            var gate = new TaskCompletionSource<int>();
            var running = new List<Task<Outcome<int>>>();
            for (var i = 0; i < 100; i++)
            {
                var call = pipeline.TryExecuteAsync(Gated, gate).AsTask();
                if (call.IsCompleted && call.Result.IsRejected)
                {
                    call.Result.Rejection.Should().Be(RejectionKind.RateLimited);
                    continue;
                }

                running.Add(call);
            }

            var latency = running.Count <= capacity ? 10.0 : 10.0 * running.Count / capacity;
            time.Advance(TimeSpan.FromMilliseconds(latency));
            gate.SetResult(1);
            await Task.WhenAll(running);
            admittedPerRound.Add(running.Count);
        }

        return admittedPerRound;
    }

    [Fact]
    public async Task Aimd_ConvergesToASawtoothBelowTheLatencyThreshold()
    {
        // Threshold 15 ms = 1.5 × the no-load latency, i.e. 30 concurrent calls.
        var admitted = await Simulate(
            new AdaptiveConcurrencyLimiterStrategyOptions<int>
            {
                Algorithm = AdaptiveConcurrencyAlgorithm.Aimd,
                InitialLimit = 5,
                MaxLimit = 200,
                LatencyThreshold = TimeSpan.FromMilliseconds(15),
            },
            rounds: 300);

        var settled = admitted.Skip(200).ToArray();
        settled.Min().Should().BeGreaterThanOrEqualTo(25);
        settled.Max().Should().BeLessThanOrEqualTo(32);
    }

    [Fact]
    public async Task Gradient_ConvergesNearToleranceTimesCapacity()
    {
        // The fixed point is n = Tolerance × capacity + √n = 30 + √n ≈ 36.
        var admitted = await Simulate(
            new AdaptiveConcurrencyLimiterStrategyOptions<int>
            {
                Algorithm = AdaptiveConcurrencyAlgorithm.Gradient,
                InitialLimit = 5,
                MaxLimit = 200,
            },
            rounds: 300);

        var settled = admitted.Skip(200).ToArray();
        settled.Min().Should().BeGreaterThanOrEqualTo(30);
        settled.Max().Should().BeLessThanOrEqualTo(42);
    }

    [Fact]
    public async Task Aimd_StartingFarAboveCapacity_BacksOffOncePerRound_ToTheSameSawtooth()
    {
        var admitted = await Simulate(
            new AdaptiveConcurrencyLimiterStrategyOptions<int>
            {
                Algorithm = AdaptiveConcurrencyAlgorithm.Aimd,
                InitialLimit = 100,
                MaxLimit = 200,
                LatencyThreshold = TimeSpan.FromMilliseconds(15),
            },
            rounds: 300);

        // A whole round of drops backs off once (× 0.9), not once per dropped call.
        admitted[0].Should().Be(100);
        admitted[1].Should().Be(90);
        admitted[2].Should().Be(81);

        var settled = admitted.Skip(200).ToArray();
        settled.Min().Should().BeGreaterThanOrEqualTo(25);
        settled.Max().Should().BeLessThanOrEqualTo(32);
    }

    // ---- The strategy ----

    [Fact]
    public async Task Rejection_IsRateLimited_WithoutRetryAfter_AndRaisesOnRejected()
    {
        var raised = 0;
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions<int>
            {
                InitialLimit = 1,
                MaxLimit = 1,
                OnRejected = args =>
                {
                    raised++;
                    args.RetryAfter.Should().NotHaveValue();
                    return default;
                },
            })
            .Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(Gated, gate).AsTask();

        var rejected = await pipeline.TryExecuteAsync(Ok);
        rejected.Rejection.Should().Be(RejectionKind.RateLimited);
        rejected.RetryAfter.Should().NotHaveValue();
        raised.Should().Be(1);

        Func<Task> act = () => pipeline.ExecuteAsync(Ok).AsTask();
        await act.Should().ThrowAsync<RateLimiterRejectedException>();

        gate.SetResult(1);
        await held;
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task Permit_IsReleased_OnExceptionAndCancellation()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions<int> { InitialLimit = 1, MinLimit = 1, MaxLimit = 1 })
            .Build();

        (await pipeline.TryExecuteAsync(static _ => ValueTask.FromException<int>(new InvalidOperationException()))).IsRejected.Should().BeFalse();
        (await pipeline.TryExecuteAsync(static _ => ValueTask.FromException<int>(new OperationCanceledException()))).IsRejected.Should().BeFalse();
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task ShouldHandle_ThatThrows_StillReleasesThePermit()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions<int>
            {
                InitialLimit = 1,
                MaxLimit = 1,
                ShouldHandle = static _ => throw new InvalidOperationException("predicate"),
            })
            .Build();

        (await pipeline.TryExecuteAsync(Ok)).Exception!.Message.Should().Be("predicate");
        (await pipeline.TryExecuteAsync(Ok)).Exception!.Message.Should().Be("predicate");
    }

    [Fact]
    public async Task HandledOutcomes_BackOff_ViaPredicateBuilder()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions<int>
            {
                Algorithm = AdaptiveConcurrencyAlgorithm.Aimd,
                InitialLimit = 2,
                MinLimit = 1,
                MaxLimit = 2,
                BackoffRatio = 0.5,
                ShouldHandle = new PredicateBuilder<int>().HandleResult(-1),
            })
            .Build();

        await pipeline.ExecuteAsync(static _ => new ValueTask<int>(-1));   // 2 → 1

        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(Gated, gate).AsTask();
        (await pipeline.TryExecuteAsync(Ok)).Rejection.Should().Be(RejectionKind.RateLimited);
        gate.SetResult(1);
        await held;
    }

    [Fact]
    public async Task NonGenericPipeline_SharesOneLimitAcrossResultTypes()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions { InitialLimit = 1, MaxLimit = 1 })
            .Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(Gated, gate).AsTask();

        (await pipeline.TryExecuteAsync(static _ => new ValueTask<string>("x"))).Rejection.Should().Be(RejectionKind.RateLimited);

        gate.SetResult(1);
        await held;
        (await pipeline.TryExecuteAsync(static _ => new ValueTask<string>("x"))).Result.Should().Be("x");
    }

    [Fact]
    public async Task NonGenericPipeline_CustomPredicate_IsAdaptedToEachResultType()
    {
        var seen = new List<object?>();
        var pipeline = new ResiliencePipelineBuilder()
            .AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions
            {
                ShouldHandle = args =>
                {
                    seen.Add(args.Outcome.Result);
                    return false;
                },
            })
            .Build();

        await pipeline.ExecuteAsync(static _ => new ValueTask<int>(4));
        await pipeline.ExecuteAsync(static _ => new ValueTask<string>("s"));

        seen.Should().Equal(4, "s");
    }

    // ---- Options ----

    public static TheoryData<string> InvalidOptions() => new()
    {
        "min-0", "max-below-min", "initial-below-min", "initial-above-max", "algorithm", "backoff-low", "backoff-1",
        "smoothing-0", "smoothing-high", "tolerance-low", "tolerance-high", "window-low", "window-high", "threshold", "should-handle",
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void InvalidOptions_ThrowValidationException_WhenAdded(string change)
    {
        var options = new AdaptiveConcurrencyLimiterStrategyOptions<int>();
        switch (change)
        {
            case "min-0": options.MinLimit = 0; break;
            case "max-below-min": options.MinLimit = 5; options.MaxLimit = 4; options.InitialLimit = 5; break;
            case "initial-below-min": options.MinLimit = 5; options.InitialLimit = 4; break;
            case "initial-above-max": options.MaxLimit = 5; options.InitialLimit = 6; break;
            case "algorithm": options.Algorithm = (AdaptiveConcurrencyAlgorithm)42; break;
            case "backoff-low": options.BackoffRatio = 0.49; break;
            case "backoff-1": options.BackoffRatio = 1; break;
            case "smoothing-0": options.Smoothing = 0; break;
            case "smoothing-high": options.Smoothing = 1.01; break;
            case "tolerance-low": options.Tolerance = 0.99; break;
            case "tolerance-high": options.Tolerance = 10.5; break;
            case "window-low": options.LatencyWindow = 9; break;
            case "window-high": options.LatencyWindow = 100_001; break;
            case "threshold": options.LatencyThreshold = TimeSpan.Zero; break;
            case "should-handle": options.ShouldHandle = null!; break;
        }

        Action act = () => new ResiliencePipelineBuilder<int>().AddAdaptiveConcurrencyLimiter(options);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Options_Defaults()
    {
        var options = new AdaptiveConcurrencyLimiterStrategyOptions<int>();
        options.Name.Should().Be("AdaptiveConcurrencyLimiter");
        options.Algorithm.Should().Be(AdaptiveConcurrencyAlgorithm.Gradient);
        options.InitialLimit.Should().Be(20);
        options.MinLimit.Should().Be(1);
        options.MaxLimit.Should().Be(1000);
        options.BackoffRatio.Should().Be(0.9);
        options.Smoothing.Should().Be(0.2);
        options.Tolerance.Should().Be(1.5);
        options.LatencyWindow.Should().Be(600);
        options.LatencyThreshold.Should().NotHaveValue();
    }
}
