using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.RateLimiting;
using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

/// <summary>The <c>System.Threading.RateLimiting</c>-backed strategy: <c>AddRateLimiter</c> / <c>AddConcurrencyLimiter</c>.</summary>
public class RateLimiterTests
{
    private static readonly Func<TaskCompletionSource<int>, CancellationToken, ValueTask<int>> Gated = static (gate, _) => new ValueTask<int>(gate.Task);

    private static ValueTask<int> Ok(CancellationToken _) => new(1);

    [Fact]
    public async Task ConcurrencyLimiter_RejectsWhileThePermitIsHeld_AndAdmitsAfterRelease()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddConcurrencyLimiter(1).Build();
        var gate = new TaskCompletionSource<int>();

        var held = pipeline.TryExecuteAsync(Gated, gate).AsTask();
        held.IsCompleted.Should().BeFalse();

        var rejected = await pipeline.TryExecuteAsync(Ok);
        rejected.IsRejected.Should().BeTrue();
        rejected.Rejection.Should().Be(RejectionKind.RateLimited);

        gate.SetResult(7);
        (await held).Result.Should().Be(7);

        var admitted = await pipeline.TryExecuteAsync(Ok);
        admitted.Result.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Rejected_ThrowsAFreshRateLimiterRejectedException()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddConcurrencyLimiter(1).Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(Gated, gate).AsTask();

        Func<Task> act = () => pipeline.ExecuteAsync(Ok).AsTask();
        var first = (await act.Should().ThrowAsync<RateLimiterRejectedException>()).Which;
        var second = (await act.Should().ThrowAsync<RateLimiterRejectedException>()).Which;

        first.Should().NotBeSameAs(second);
        first.Kind.Should().Be(RejectionKind.RateLimited);
        first.RetryAfter.Should().NotHaveValue();

        gate.SetResult(1);
        await held;
    }

    [Fact]
    public async Task QueueLimit_QueuedCallWaitsForThePermit_ThenRuns()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddConcurrencyLimiter(1, queueLimit: 1).Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(Gated, gate).AsTask();

        var queued = pipeline.ExecuteAsync(static _ => new ValueTask<int>(2)).AsTask();
        queued.IsCompleted.Should().BeFalse();

        // The queue is full: a third call is rejected at once.
        var third = await pipeline.TryExecuteAsync(Ok);
        third.Rejection.Should().Be(RejectionKind.RateLimited);

        gate.SetResult(1);
        (await held).Should().Be(1);
        (await queued).Should().Be(2);
    }

    [Fact]
    public async Task QueuedCall_Cancelled_SurfacesTheCancellation_AndFreesItsQueueSlot()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddConcurrencyLimiter(1, queueLimit: 1).Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(Gated, gate).AsTask();

        using var cts = new CancellationTokenSource();
        var queued = pipeline.TryExecuteAsync(Ok, cts.Token).AsTask();
        cts.Cancel();
        var outcome = await queued;
        outcome.IsRejected.Should().BeFalse();
        outcome.Exception.Should().BeAssignableTo<OperationCanceledException>();

        // The cancelled waiter left the queue: another call can queue and runs after the holder.
        var next = pipeline.ExecuteAsync(static _ => new ValueTask<int>(3)).AsTask();
        next.IsCompleted.Should().BeFalse();
        gate.SetResult(1);
        await held;
        (await next).Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lease_IsDisposed_WhenTheCallbackThrowsOrIsCancelled(bool cancel)
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddConcurrencyLimiter(1).Build();

        var failed = await pipeline.TryExecuteAsync(
            static (cancel, _) => cancel ? ValueTask.FromException<int>(new OperationCanceledException()) : throw new InvalidOperationException("boom"),
            cancel);
        failed.IsRejected.Should().BeFalse();

        // The permit came back.
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task Rejection_CarriesRetryAfter_FromTheLeaseMetadata()
    {
        var limiter = new StubLimiter { RetryAfter = TimeSpan.FromSeconds(42) };
        var pipeline = new ResiliencePipelineBuilder<int>().AddRateLimiter(limiter).Build();

        var outcome = await pipeline.TryExecuteAsync(Ok);

        outcome.Rejection.Should().Be(RejectionKind.RateLimited);
        outcome.RetryAfter.Should().Be(TimeSpan.FromSeconds(42));
        limiter.Leases.Should().HaveCount(1);
        limiter.Leases[0].IsDisposed.Should().BeTrue();

        Func<Task> act = () => pipeline.ExecuteAsync(Ok).AsTask();
        var thrown = (await act.Should().ThrowAsync<RateLimiterRejectedException>()).Which;
        thrown.RetryAfter.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task FixedWindowRateLimiter_RejectionHasARetryAfter()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = false,
        });
        var pipeline = new ResiliencePipelineBuilder<int>().AddRateLimiter(limiter).Build();

        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
        var rejected = await pipeline.TryExecuteAsync(Ok);

        rejected.Rejection.Should().Be(RejectionKind.RateLimited);
        (rejected.RetryAfter is not null).Should().BeTrue();
    }

    [Fact]
    public async Task AcquiredLease_IsDisposedWhenTheCallFinishes()
    {
        var limiter = new StubLimiter { Acquire = true };
        var pipeline = new ResiliencePipelineBuilder<int>().AddRateLimiter(limiter).Build();
        var gate = new TaskCompletionSource<int>();

        var running = pipeline.ExecuteAsync(Gated, gate).AsTask();
        limiter.Leases[0].IsDisposed.Should().BeFalse();

        gate.SetResult(5);
        (await running).Should().Be(5);
        limiter.Leases[0].IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task OnRejected_SeesTheLease_BeforeItIsDisposed()
    {
        var limiter = new StubLimiter { RetryAfter = TimeSpan.FromSeconds(3) };
        var sawUndisposed = false;
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                RateLimiter = args => limiter.AcquireAsync(1, args.CancellationToken),
                OnRejected = args =>
                {
                    sawUndisposed = !((StubLease)args.Lease).IsDisposed && args.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) && ra == TimeSpan.FromSeconds(3);
                    return default;
                },
            })
            .Build();

        var outcome = await pipeline.TryExecuteAsync(Ok);

        outcome.Rejection.Should().Be(RejectionKind.RateLimited);
        sawUndisposed.Should().BeTrue();
        limiter.Leases[0].IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task RateLimiterDelegate_GetsTheExecutionsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                RateLimiter = args =>
                {
                    seen = args.CancellationToken;
                    return new ValueTask<RateLimitLease>(new StubLease(acquired: true, retryAfter: null));
                },
            })
            .Build();

        (await pipeline.ExecuteAsync(Ok, cts.Token)).Should().Be(1);
        (seen == cts.Token).Should().BeTrue();
    }

    [Fact]
    public async Task RateLimiterDelegate_CompletingAsynchronously_IsAwaited()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                RateLimiter = static async _ =>
                {
                    await Task.Yield();
                    return new StubLease(acquired: false, retryAfter: TimeSpan.FromSeconds(1));
                },
            })
            .Build();

        var outcome = await pipeline.TryExecuteAsync(Ok);
        outcome.Rejection.Should().Be(RejectionKind.RateLimited);
        outcome.RetryAfter.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task NonGenericPipeline_SharesOneDefaultLimiterAcrossResultTypes()
    {
        var pipeline = new ResiliencePipelineBuilder().AddConcurrencyLimiter(1).Build();
        var gate = new TaskCompletionSource<int>();
        var held = pipeline.ExecuteAsync(Gated, gate).AsTask();

        var otherType = await pipeline.TryExecuteAsync(static _ => new ValueTask<string>("x"));
        otherType.Rejection.Should().Be(RejectionKind.RateLimited);

        gate.SetResult(1);
        await held;
        (await pipeline.TryExecuteAsync(static _ => new ValueTask<string>("x"))).Result.Should().Be("x");
    }

    [Fact]
    public async Task Rejection_IsHandledByRetry_AndByPredicateBuilderHandle()
    {
        var time = new FakeTimeProvider();
        var limiter = new StubLimiter { RetryAfter = TimeSpan.FromSeconds(1) };
        var retries = 0;
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder<int>().Handle<RateLimiterRejectedException>(),
                OnRetry = _ =>
                {
                    retries++;
                    return default;
                },
            })
            .AddRateLimiter(limiter)
            .Build();

        var outcome = await pipeline.TryExecuteAsync(Ok);

        outcome.Rejection.Should().Be(RejectionKind.RateLimited);
        retries.Should().Be(2);
        limiter.Leases.Should().HaveCount(3);
    }

    [Fact]
    public void MissingDefaultOptions_ThrowValidationException_WhenAdded()
    {
        Action act = () => new ResiliencePipelineBuilder<int>().AddRateLimiter(new RateLimiterStrategyOptions { DefaultRateLimiterOptions = null! });
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Options_HavePollysDefaults()
    {
        var options = new RateLimiterStrategyOptions();
        options.Name.Should().Be("RateLimiter");
        options.RateLimiter.Should().BeNull();
        options.DefaultRateLimiterOptions.PermitLimit.Should().Be(1000);
        options.DefaultRateLimiterOptions.QueueLimit.Should().Be(0);
    }

    /// <summary>A limiter that grants or refuses every lease, recording them.</summary>
    private sealed class StubLimiter : RateLimiter
    {
        public bool Acquire { get; set; }

        public TimeSpan? RetryAfter { get; set; }

        public List<StubLease> Leases { get; } = [];

        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount) => Create();

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) => new(Create());

        private StubLease Create()
        {
            var lease = new StubLease(Acquire, Acquire ? null : RetryAfter);
            Leases.Add(lease);
            return lease;
        }
    }

    private sealed class StubLease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public bool IsDisposed { get; private set; }

        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing) => IsDisposed = true;
    }
}
