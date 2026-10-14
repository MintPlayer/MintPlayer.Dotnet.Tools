using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

/// <summary>
/// The sync-completing happy path allocates nothing: no frame, context, CTS, state machine box or
/// argument boxing, through retry + timeout + fallback. Measured per thread, after a warm-up that fills
/// the pools and the timer of the pooled CTS. Runs alone: the CTS pool of the system clock is shared, and a
/// concurrent test growing its queue could charge a segment allocation to this thread.
/// </summary>
[Collection(nameof(AllocationTests))]
public class AllocationTests
{
    private const int Warmup = 200;
    private const int Iterations = 2_000;

    private static readonly Func<int, CancellationToken, ValueTask<int>> Callback = static (state, _) => new ValueTask<int>(state + 1);

    private static ResiliencePipeline<int> FullPipeline() => new ResiliencePipelineBuilder<int>()
        .AddFallback(new FallbackStrategyOptions<int>
        {
            ShouldHandle = new PredicateBuilder<int>().Handle<InvalidOperationException>().HandleResult(-1),
            FallbackAction = static _ => Outcome.FromResultAsValueTask(0),
        })
        .AddTimeout(TimeSpan.FromSeconds(10))
        .AddRetry(new RetryStrategyOptions<int>
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(100),
            ShouldHandle = new PredicateBuilder<int>().Handle<InvalidOperationException>().HandleResult(-1),
        })
        .AddTimeout(TimeSpan.FromSeconds(2))
        .Build();

    private static long Measure(Action action)
    {
        for (var i = 0; i < Warmup; i++)
        {
            action();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            action();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void ExecuteAsync_SyncCompletingHappyPath_AllocatesNothing()
    {
        var pipeline = FullPipeline();

        var bytes = Measure(() =>
        {
            var task = pipeline.ExecuteAsync(Callback, 41);
            if (!task.IsCompletedSuccessfully || task.Result != 42)
            {
                throw new InvalidOperationException("The execution did not complete synchronously with the expected result.");
            }
        });

        bytes.Should().Be(0);
    }

    [Fact]
    public void TryExecuteAsync_SyncCompletingHappyPath_AllocatesNothing()
    {
        var pipeline = FullPipeline();

        var bytes = Measure(() =>
        {
            var task = pipeline.TryExecuteAsync(Callback, 41);
            if (!task.IsCompletedSuccessfully || task.Result.Result != 42)
            {
                throw new InvalidOperationException("The execution did not complete synchronously with the expected result.");
            }
        });

        bytes.Should().Be(0);
    }

    [Fact]
    public void Execute_SyncHappyPath_AllocatesNothing()
    {
        var pipeline = FullPipeline();

        var bytes = Measure(() =>
        {
            if (pipeline.Execute(static (state, _) => state + 1, 41) != 42)
            {
                throw new InvalidOperationException("Unexpected result.");
            }
        });

        bytes.Should().Be(0);
    }

    [Fact]
    public void NonGenericPipeline_VoidAndTypedHappyPaths_AllocateNothing()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(10))
            .AddRetry(new RetryStrategyOptions { Delay = TimeSpan.Zero })
            .Build();

        var bytes = Measure(() =>
        {
            var typed = pipeline.ExecuteAsync(static (state, _) => new ValueTask<int>(state), 1);
            var untyped = pipeline.ExecuteAsync(static (_, _) => default, 0);
            if (!typed.IsCompletedSuccessfully || typed.Result != 1 || !untyped.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("The execution did not complete synchronously.");
            }
        });

        bytes.Should().Be(0);
    }

    [Fact]
    public void RetryThatSucceedsOnTheSecondAttempt_WithZeroDelay_AllocatesNothing()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int> { Delay = TimeSpan.Zero, ShouldHandle = static args => args.Outcome.Result < 0 })
            .Build();

        var bytes = Measure(() =>
        {
            var attempts = new StrongBox(0);
            var task = pipeline.ExecuteAsync(static (box, _) => new ValueTask<int>(++box.Value < 2 ? -1 : box.Value), attempts);
            if (!task.IsCompletedSuccessfully || task.Result != 2)
            {
                throw new InvalidOperationException("Unexpected result.");
            }
        });

        // Only the test's own StrongBox (one object per iteration).
        bytes.Should().BeLessThanOrEqualTo(Iterations * 32L);
    }

    [Fact]
    public void RejectedOutcome_OfAnInnerTimeout_IsNotAllocatedByTheOutcome()
    {
        // TryExecuteAsync returning a rejection does not create an exception: the kind is a value.
        var outcome = Outcome.Rejected<int>(RejectionKind.Timeout, TimeSpan.FromSeconds(1).Ticks);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var rejected = outcome.IsRejected && outcome.Rejection == RejectionKind.Timeout && outcome.RetryAfter is null;
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
        rejected.Should().BeTrue();
    }

    private static ResiliencePipeline<int> Breaker(Action<CircuitBreakerStrategyOptions<int>>? configure = null)
    {
        var options = new CircuitBreakerStrategyOptions<int>
        {
            FailureRatio = 0.5,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromDays(1),
            ShouldHandle = static args => args.Outcome.Result < 0,
        };
        configure?.Invoke(options);
        return new ResiliencePipelineBuilder<int>().AddCircuitBreaker(options).Build();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedCircuit_SuccessesAndFailuresThatDoNotTrip_AllocateNothing(bool trackSlowCalls)
    {
        var pipeline = Breaker(options =>
        {
            options.FailureRatio = 1.0;
            options.MinimumThroughput = int.MaxValue;
            options.SlowCallDurationThreshold = trackSlowCalls ? TimeSpan.FromSeconds(10) : null;
        });

        var bytes = Measure(() =>
        {
            var ok = pipeline.TryExecuteAsync(static (state, _) => new ValueTask<int>(state), 1);
            var failed = pipeline.TryExecuteAsync(static (state, _) => new ValueTask<int>(state), -1);
            if (!ok.IsCompletedSuccessfully || ok.Result.Result != 1 || !failed.IsCompletedSuccessfully || failed.Result.Result != -1)
            {
                throw new InvalidOperationException("Unexpected result.");
            }
        });

        bytes.Should().Be(0);
    }

    [Fact]
    public void OpenCircuit_TryExecuteAsyncRejection_AllocatesNothing()
    {
        var pipeline = Breaker();
        for (var i = 0; i < 2; i++)
        {
            pipeline.Execute(static () => -1);
        }

        var bytes = Measure(() =>
        {
            var task = pipeline.TryExecuteAsync(static (state, _) => new ValueTask<int>(state), 1);
            if (!task.IsCompletedSuccessfully || task.Result.Rejection != RejectionKind.CircuitOpen || task.Result.RetryAfter is null)
            {
                throw new InvalidOperationException("The circuit did not reject synchronously.");
            }
        });

        bytes.Should().Be(0);
    }

    [Fact]
    public void IsolatedCircuit_TryExecuteAsyncRejection_AllocatesNothing()
    {
        var control = new CircuitBreakerManualControl(isIsolated: true);
        var pipeline = Breaker(options => options.ManualControl = control);

        var bytes = Measure(() =>
        {
            var task = pipeline.TryExecuteAsync(static (state, _) => new ValueTask<int>(state), 1);
            if (!task.IsCompletedSuccessfully || task.Result.Rejection != RejectionKind.CircuitIsolated)
            {
                throw new InvalidOperationException("The circuit did not reject synchronously.");
            }
        });

        bytes.Should().Be(0);
    }

    [Fact]
    public void OpenCircuit_NonGenericPipelineRejection_AllocatesNothing()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromDays(1),
                ShouldHandle = static args => args.Outcome.Result is -1,
            })
            .Build();
        for (var i = 0; i < 2; i++)
        {
            pipeline.Execute(static () => -1);
        }

        var bytes = Measure(() =>
        {
            var task = pipeline.TryExecuteAsync(static (state, _) => new ValueTask<int>(state), 1);
            if (!task.IsCompletedSuccessfully || task.Result.Rejection != RejectionKind.CircuitOpen)
            {
                throw new InvalidOperationException("The circuit did not reject synchronously.");
            }
        });

        bytes.Should().Be(0);
    }

    private sealed class StrongBox(int value)
    {
        public int Value = value;
    }
}

/// <summary>Allocation measurements run with nothing else in parallel.</summary>
[CollectionDefinition(nameof(AllocationTests), DisableParallelization = true)]
public sealed class AllocationTestsCollection
{
}
