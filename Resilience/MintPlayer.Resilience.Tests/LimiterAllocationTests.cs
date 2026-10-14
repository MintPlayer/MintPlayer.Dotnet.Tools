using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.RateLimiting;
using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

/// <summary>
/// The lease-free limiters, the adaptive limiter and the retry budget allocate nothing, neither when they
/// admit nor when they reject (plan S5). Runs in the allocation collection, alone.
/// </summary>
[Collection(nameof(AllocationTests))]
public class LimiterAllocationTests
{
    private const int Warmup = 200;
    private const int Iterations = 2_000;

    private static readonly Func<int, CancellationToken, ValueTask<int>> Callback = static (state, _) => new ValueTask<int>(state);

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

    private static void ExpectAdmitted(ResiliencePipeline<int> pipeline)
    {
        var task = pipeline.TryExecuteAsync(Callback, 1);
        if (!task.IsCompletedSuccessfully || task.Result.IsRejected || task.Result.Result != 1)
        {
            throw new InvalidOperationException("The call was not admitted synchronously.");
        }
    }

    private static void ExpectRejected(ResiliencePipeline<int> pipeline, bool hasRetryAfter)
    {
        var task = pipeline.TryExecuteAsync(Callback, 1);
        if (!task.IsCompletedSuccessfully || task.Result.Rejection != RejectionKind.RateLimited || task.Result.RetryAfter.HasValue != hasRetryAfter)
        {
            throw new InvalidOperationException("The call was not rejected synchronously.");
        }
    }

    public static TheoryData<string> WindowLimiters() => new() { "fixed", "sliding" };

    private static ResiliencePipelineBuilder<int> Window(string kind, int permits, TimeSpan window, TimeProvider time)
    {
        var builder = new ResiliencePipelineBuilder<int> { TimeProvider = time };
        return kind == "fixed" ? builder.AddFixedWindowLimiter(permits, window) : builder.AddSlidingWindowLimiter(permits, window);
    }

    [Theory]
    [MemberData(nameof(WindowLimiters))]
    public void WindowLimiter_Admission_AllocatesNothing(string kind)
    {
        var pipeline = Window(kind, int.MaxValue, TimeSpan.FromDays(1), new FakeTimeProvider()).Build();
        Measure(() => ExpectAdmitted(pipeline)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(WindowLimiters))]
    public void WindowLimiter_Rejection_AllocatesNothing(string kind)
    {
        var pipeline = Window(kind, 1, TimeSpan.FromDays(1), new FakeTimeProvider()).Build();
        ExpectAdmitted(pipeline);
        Measure(() => ExpectRejected(pipeline, hasRetryAfter: true)).Should().Be(0);
    }

    [Fact]
    public void WindowLimiter_AcrossWindows_AllocatesNothing()
    {
        var time = new ManualClock(); // Interlocked.Add only: advancing it cannot allocate
        var fixedWindow = Window("fixed", 1, TimeSpan.FromMilliseconds(10), time).Build();
        var sliding = Window("sliding", 1, TimeSpan.FromMilliseconds(10), time).Build();

        Measure(() =>
        {
            time.Advance(TimeSpan.FromMilliseconds(10));
            ExpectAdmitted(fixedWindow);
            ExpectAdmitted(sliding);
            ExpectRejected(fixedWindow, hasRetryAfter: true);
            ExpectRejected(sliding, hasRetryAfter: true);
        }).Should().Be(0);
    }

    [Fact]
    public void NativeConcurrencyLimiter_AdmissionAndRelease_AllocateNothing()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddNativeConcurrencyLimiter(1).Build();
        Measure(() => ExpectAdmitted(pipeline)).Should().Be(0);
    }

    [Fact]
    public async Task NativeConcurrencyLimiter_Rejection_AllocatesNothing()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddNativeConcurrencyLimiter(1).Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(static (g, _) => new ValueTask<int>(g.Task), gate).AsTask();

        Measure(() => ExpectRejected(pipeline, hasRetryAfter: false)).Should().Be(0);

        gate.SetResult(1);
        await held;
    }

    [Theory]
    [InlineData(AdaptiveConcurrencyAlgorithm.Aimd)]
    [InlineData(AdaptiveConcurrencyAlgorithm.Gradient)]
    public void AdaptiveConcurrencyLimiter_AdmissionAndSample_AllocateNothing(AdaptiveConcurrencyAlgorithm algorithm)
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions<int> { Algorithm = algorithm, InitialLimit = 1, MaxLimit = 4 })
            .Build();
        Measure(() => ExpectAdmitted(pipeline)).Should().Be(0);
    }

    [Fact]
    public async Task AdaptiveConcurrencyLimiter_Rejection_AllocatesNothing()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions<int> { InitialLimit = 1, MaxLimit = 1 })
            .Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(static (g, _) => new ValueTask<int>(g.Task), gate).AsTask();

        Measure(() => ExpectRejected(pipeline, hasRetryAfter: false)).Should().Be(0);

        gate.SetResult(1);
        await held;
    }

    [Fact]
    public void NonGenericPipeline_NativeLimiterRejection_AllocatesNothing()
    {
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = new FakeTimeProvider() }.AddFixedWindowLimiter(1, TimeSpan.FromDays(1)).Build();
        pipeline.Execute(static () => 1);

        Measure(() =>
        {
            var task = pipeline.TryExecuteAsync(Callback, 1);
            if (!task.IsCompletedSuccessfully || task.Result.Rejection != RejectionKind.RateLimited)
            {
                throw new InvalidOperationException("The call was not rejected synchronously.");
            }
        }).Should().Be(0);
    }

    [Fact]
    public void RetryBudget_DepositAndWithdraw_AllocateNothing()
    {
        var budget = new RetryBudget(retryRatio: 0.5, minRetriesPerSecond: 0, timeProvider: new FakeTimeProvider());
        var granted = 0;
        var bytes = Measure(() =>
        {
            budget.Deposit();
            budget.Deposit();
            if (budget.TryWithdraw())
            {
                granted++;
            }

            _ = budget.Balance;
        });

        bytes.Should().Be(0);
        granted.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RetryWithBudget_HappyPathAndExhaustedBudget_AllocateNothing()
    {
        var budget = new RetryBudget(retryRatio: 0, minRetriesPerSecond: 0, timeProvider: new FakeTimeProvider());
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int> { Delay = TimeSpan.Zero, Budget = budget, ShouldHandle = static args => args.Outcome.Result < 0 })
            .Build();

        Measure(() =>
        {
            var ok = pipeline.TryExecuteAsync(Callback, 1);
            var refused = pipeline.TryExecuteAsync(Callback, -1);
            if (!ok.IsCompletedSuccessfully || ok.Result.Result != 1 || !refused.IsCompletedSuccessfully || refused.Result.Result != -1)
            {
                throw new InvalidOperationException("Unexpected result.");
            }
        }).Should().Be(0);
    }
}
