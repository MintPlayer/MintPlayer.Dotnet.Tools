using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.RateLimiting;

namespace MintPlayer.Resilience.Tests;

/// <summary>The lease-free limiters: fixed window, sliding window and concurrency.</summary>
public class NativeLimiterTests
{
    private static readonly Func<TaskCompletionSource<int>, CancellationToken, ValueTask<int>> Gated = static (gate, _) => new ValueTask<int>(gate.Task);

    private static ValueTask<int> Ok(CancellationToken _) => new(1);

    private static async Task<int> Admitted(ResiliencePipeline<int> pipeline, int attempts)
    {
        var admitted = 0;
        for (var i = 0; i < attempts; i++)
        {
            if (!(await pipeline.TryExecuteAsync(Ok)).IsRejected)
            {
                admitted++;
            }
        }

        return admitted;
    }

    // ---- Fixed window ----

    [Fact]
    public async Task FixedWindow_AdmitsPermitLimitPerWindow_AndReportsTheRestOfTheWindow()
    {
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddFixedWindowLimiter(2, TimeSpan.FromSeconds(1)).Build();

        (await Admitted(pipeline, 2)).Should().Be(2);
        var rejected = await pipeline.TryExecuteAsync(Ok);
        rejected.Rejection.Should().Be(RejectionKind.RateLimited);
        rejected.RetryAfter.Should().Be(TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromMilliseconds(400));
        (await pipeline.TryExecuteAsync(Ok)).RetryAfter.Should().Be(TimeSpan.FromMilliseconds(600));

        time.Advance(TimeSpan.FromMilliseconds(599));
        (await pipeline.TryExecuteAsync(Ok)).RetryAfter.Should().Be(TimeSpan.FromMilliseconds(1));

        // The boundary: a new window with a full set of permits.
        time.Advance(TimeSpan.FromMilliseconds(1));
        (await Admitted(pipeline, 5)).Should().Be(2);
    }

    [Fact]
    public async Task FixedWindow_RejectionsDoNotConsumePermits_AndIdleWindowsAreSkipped()
    {
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddFixedWindowLimiter(3, TimeSpan.FromSeconds(1)).Build();

        (await Admitted(pipeline, 10)).Should().Be(3);
        time.Advance(TimeSpan.FromSeconds(7.5));
        (await Admitted(pipeline, 10)).Should().Be(3);
        (await pipeline.TryExecuteAsync(Ok)).RetryAfter.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task FixedWindow_ExecuteAsync_ThrowsRateLimiterRejectedExceptionWithRetryAfter()
    {
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddFixedWindowLimiter(1, TimeSpan.FromMinutes(1)).Build();
        await pipeline.ExecuteAsync(Ok);

        Func<Task> act = () => pipeline.ExecuteAsync(Ok).AsTask();
        var thrown = (await act.Should().ThrowAsync<RateLimiterRejectedException>()).Which;
        thrown.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
        thrown.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task FixedWindow_OnRejected_GetsTheRetryAfter()
    {
        var time = new FakeTimeProvider();
        TimeSpan? seen = null;
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddFixedWindowLimiter(new FixedWindowLimiterStrategyOptions
            {
                PermitLimit = 1,
                Window = TimeSpan.FromSeconds(10),
                OnRejected = args =>
                {
                    seen = args.RetryAfter;
                    return default;
                },
            })
            .Build();

        await pipeline.ExecuteAsync(Ok);
        time.Advance(TimeSpan.FromSeconds(4));
        (await pipeline.TryExecuteAsync(Ok)).Rejection.Should().Be(RejectionKind.RateLimited);

        seen.Should().Be(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task FixedWindow_NonGenericPipeline_SharesTheWindowAcrossResultTypes()
    {
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = time }.AddFixedWindowLimiter(2, TimeSpan.FromSeconds(1)).Build();

        (await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1))).IsRejected.Should().BeFalse();
        (await pipeline.TryExecuteAsync(static _ => new ValueTask<string>("a"))).IsRejected.Should().BeFalse();
        (await pipeline.TryExecuteAsync(static _ => new ValueTask<bool>(true))).Rejection.Should().Be(RejectionKind.RateLimited);
    }

    [Fact]
    public async Task FixedWindow_TwoPipelines_HaveTheirOwnState()
    {
        var time = new FakeTimeProvider();
        var builder = () => new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddFixedWindowLimiter(1, TimeSpan.FromSeconds(1)).Build();
        var a = builder();
        var b = builder();

        (await Admitted(a, 3)).Should().Be(1);
        (await Admitted(b, 3)).Should().Be(1);
    }

    // ---- Sliding window ----

    [Fact]
    public async Task SlidingWindow_PermitsComeBackOneSegmentAtATime()
    {
        // 4 permits per 1 s, in 4 segments of 250 ms.
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddSlidingWindowLimiter(4, TimeSpan.FromSeconds(1), 4).Build();

        (await Admitted(pipeline, 2)).Should().Be(2);          // segment 0
        time.Advance(TimeSpan.FromMilliseconds(500));
        (await Admitted(pipeline, 2)).Should().Be(2);          // segment 2

        var rejected = await pipeline.TryExecuteAsync(Ok);
        rejected.Rejection.Should().Be(RejectionKind.RateLimited);
        rejected.RetryAfter.Should().Be(TimeSpan.FromMilliseconds(500)); // segment 0 leaves at 1 s

        // Refusals take their tentative count back.
        (await Admitted(pipeline, 10)).Should().Be(0);

        time.Advance(TimeSpan.FromMilliseconds(499));           // 999 ms: segment 3, segment 0 still counts
        var stillFull = await pipeline.TryExecuteAsync(Ok);
        stillFull.Rejection.Should().Be(RejectionKind.RateLimited);
        stillFull.RetryAfter.Should().Be(TimeSpan.FromMilliseconds(1));

        time.Advance(TimeSpan.FromMilliseconds(1));             // 1000 ms: segment 4, segment 0 expired
        (await Admitted(pipeline, 10)).Should().Be(2);

        // Now segments 2 and 4 hold 2 each; segment 2 leaves at 1.5 s.
        (await pipeline.TryExecuteAsync(Ok)).RetryAfter.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task SlidingWindow_DoesNotAllowADoubleBurstAcrossAWindowBoundary()
    {
        // A fixed window of 10/s admits 20 within 2 ms across a boundary; a sliding window does not.
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddSlidingWindowLimiter(10, TimeSpan.FromSeconds(1), 10).Build();

        time.Advance(TimeSpan.FromMilliseconds(999));
        (await Admitted(pipeline, 20)).Should().Be(10);
        time.Advance(TimeSpan.FromMilliseconds(2));
        (await Admitted(pipeline, 20)).Should().Be(0);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        (await Admitted(pipeline, 20)).Should().Be(10);
    }

    [Fact]
    public async Task SlidingWindow_SingleSegment_BehavesAsAFixedWindow()
    {
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddSlidingWindowLimiter(3, TimeSpan.FromSeconds(1), 1).Build();

        (await Admitted(pipeline, 5)).Should().Be(3);
        time.Advance(TimeSpan.FromMilliseconds(300));
        (await pipeline.TryExecuteAsync(Ok)).RetryAfter.Should().Be(TimeSpan.FromMilliseconds(700));
        time.Advance(TimeSpan.FromMilliseconds(700));
        (await Admitted(pipeline, 5)).Should().Be(3);
    }

    [Fact]
    public async Task SlidingWindow_LongIdle_ForgetsEverything()
    {
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddSlidingWindowLimiter(5, TimeSpan.FromSeconds(1), 5).Build();

        (await Admitted(pipeline, 5)).Should().Be(5);
        time.Advance(TimeSpan.FromHours(3));
        (await Admitted(pipeline, 9)).Should().Be(5);
    }

    // ---- Concurrency ----

    [Fact]
    public async Task NativeConcurrency_RejectsBeyondThePermitLimit_WithoutRetryAfter()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddNativeConcurrencyLimiter(2).Build();
        var gate = new TaskCompletionSource<int>();
        var first = pipeline.ExecuteAsync(Gated, gate).AsTask();
        var second = pipeline.ExecuteAsync(Gated, gate).AsTask();

        var rejected = await pipeline.TryExecuteAsync(Ok);
        rejected.Rejection.Should().Be(RejectionKind.RateLimited);
        rejected.RetryAfter.Should().NotHaveValue();

        gate.SetResult(1);
        await first;
        await second;
        (await Admitted(pipeline, 2)).Should().Be(2);
    }

    [Fact]
    public async Task NativeConcurrency_ReleasesOnException_Cancellation_AndSynchronousThrow()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddNativeConcurrencyLimiter(1).Build();

        (await pipeline.TryExecuteAsync(static _ => ValueTask.FromException<int>(new InvalidOperationException()))).IsRejected.Should().BeFalse();
        (await pipeline.TryExecuteAsync(static _ => ValueTask.FromException<int>(new OperationCanceledException()))).IsRejected.Should().BeFalse();
        (await pipeline.TryExecuteAsync(static _ => { throw new InvalidOperationException(); })).IsRejected.Should().BeFalse();

        using var cts = new CancellationTokenSource();
        var cancelled = pipeline.TryExecuteAsync(static async ct =>
        {
            await Task.Delay(global::System.Threading.Timeout.Infinite, ct);
            return 0;
        }, cts.Token).AsTask();
        (await pipeline.TryExecuteAsync(Ok)).Rejection.Should().Be(RejectionKind.RateLimited);
        cts.Cancel();
        (await cancelled).Exception.Should().BeAssignableTo<OperationCanceledException>();

        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public void NativeConcurrency_SynchronousExecute_ReleasesThePermit()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddNativeConcurrencyLimiter(1).Build();
        for (var i = 0; i < 5; i++)
        {
            pipeline.Execute(static () => 1).Should().Be(1);
        }
    }

    [Fact]
    public async Task NativeConcurrency_InsideRetry_TakesAPermitPerAttempt()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new Retry.RetryStrategyOptions<int> { MaxRetryAttempts = 3, Delay = TimeSpan.Zero, ShouldHandle = static args => args.Outcome.Result < 0 })
            .AddNativeConcurrencyLimiter(1)
            .Build();

        var attempts = 0;
        var result = await pipeline.ExecuteAsync(_ => new ValueTask<int>(++attempts < 3 ? -1 : attempts));
        result.Should().Be(3);

        // Every attempt handed its permit back.
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task NativeConcurrency_OnRejected_IsRaised()
    {
        var raised = 0;
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddNativeConcurrencyLimiter(new NativeConcurrencyLimiterStrategyOptions
            {
                PermitLimit = 1,
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

        (await pipeline.TryExecuteAsync(Ok)).IsRejected.Should().BeTrue();
        raised.Should().Be(1);

        gate.SetResult(1);
        await held;
    }

    [Fact]
    public async Task OnRejected_ThatThrows_ReplacesTheRejection()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddNativeConcurrencyLimiter(new NativeConcurrencyLimiterStrategyOptions
            {
                PermitLimit = 1,
                OnRejected = static _ => ValueTask.FromException(new InvalidOperationException("hook")),
            })
            .Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(Gated, gate).AsTask();

        var outcome = await pipeline.TryExecuteAsync(Ok);
        outcome.IsRejected.Should().BeFalse();
        outcome.Exception!.Message.Should().Be("hook");

        gate.SetResult(1);
        await held;
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    // ---- Options ----

    public static TheoryData<string> InvalidOptions() => new()
    {
        "fixed-permits",
        "fixed-window-short",
        "fixed-window-long",
        "sliding-permits",
        "sliding-segments-0",
        "sliding-segments-101",
        "sliding-window",
        "concurrency-permits",
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void InvalidOptions_ThrowValidationException_WhenAdded(string change)
    {
        var builder = new ResiliencePipelineBuilder<int>();
        Action act = change switch
        {
            "fixed-permits" => () => builder.AddFixedWindowLimiter(0, TimeSpan.FromSeconds(1)),
            "fixed-window-short" => () => builder.AddFixedWindowLimiter(1, TimeSpan.FromTicks(9_999)),
            "fixed-window-long" => () => builder.AddFixedWindowLimiter(1, TimeSpan.FromDays(2)),
            "sliding-permits" => () => builder.AddSlidingWindowLimiter(0, TimeSpan.FromSeconds(1)),
            "sliding-segments-0" => () => builder.AddSlidingWindowLimiter(1, TimeSpan.FromSeconds(1), 0),
            "sliding-segments-101" => () => builder.AddSlidingWindowLimiter(1, TimeSpan.FromSeconds(1), 101),
            "sliding-window" => () => builder.AddSlidingWindowLimiter(1, TimeSpan.Zero),
            "concurrency-permits" => () => builder.AddNativeConcurrencyLimiter(0),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Options_Defaults()
    {
        var fixedWindow = new FixedWindowLimiterStrategyOptions();
        fixedWindow.Name.Should().Be("FixedWindowLimiter");
        fixedWindow.PermitLimit.Should().Be(1000);
        fixedWindow.Window.Should().Be(TimeSpan.FromSeconds(1));

        var sliding = new SlidingWindowLimiterStrategyOptions();
        sliding.Name.Should().Be("SlidingWindowLimiter");
        sliding.SegmentsPerWindow.Should().Be(10);

        new NativeConcurrencyLimiterStrategyOptions().Name.Should().Be("NativeConcurrencyLimiter");
    }

    [Fact]
    public async Task OptionsChangedAfterBuild_DoNotAffectThePipeline()
    {
        var time = new FakeTimeProvider();
        var options = new FixedWindowLimiterStrategyOptions { PermitLimit = 1, Window = TimeSpan.FromSeconds(1) };
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddFixedWindowLimiter(options).Build();
        options.PermitLimit = 100;

        pipeline.Execute(static () => 1);
        (await pipeline.TryExecuteAsync(Ok)).Rejection.Should().Be(RejectionKind.RateLimited);
    }
}
