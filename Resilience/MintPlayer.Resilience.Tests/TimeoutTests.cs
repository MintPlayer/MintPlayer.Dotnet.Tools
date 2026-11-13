using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Timeout;

namespace MintPlayer.Resilience.Tests;

public class TimeoutTests
{
    private static async ValueTask<int> WaitForCancellation(CancellationToken cancellationToken)
    {
        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        return 1;
    }

    private static ResiliencePipeline<int> Pipeline(FakeTimeProvider time, TimeoutStrategyOptions options)
        => new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddTimeout(options).Build();

    [Fact]
    public async Task ElapsedTimeout_ThrowsTimeoutRejectedException_WithTheTimeoutAndTheCancellationAsInner()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline(time, new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(2) });

        var execution = pipeline.ExecuteAsync(WaitForCancellation).AsTask();
        time.Advance(TimeSpan.FromSeconds(2));

        Func<Task> act = () => execution;
        var thrown = (await act.Should().ThrowAsync<TimeoutRejectedException>()).Which;
        thrown.Timeout.Should().Be(TimeSpan.FromSeconds(2));
        thrown.Kind.Should().Be(RejectionKind.Timeout);
        thrown.InnerException.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task ElapsedTimeout_TryExecuteAsync_ReturnsATimeoutRejection()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline(time, new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) });

        var execution = pipeline.TryExecuteAsync(WaitForCancellation).AsTask();
        time.Advance(TimeSpan.FromSeconds(1));
        var outcome = await execution;

        outcome.IsRejected.Should().BeTrue();
        outcome.Rejection.Should().Be(RejectionKind.Timeout);
        outcome.RetryAfter.Should().NotHaveValue();
        outcome.Exception.Should().BeOfType<TimeoutRejectedException>();
    }

    [Fact]
    public async Task BeforeTheTimeout_TheCallbackIsNotCancelled()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline(time, new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) });

        var execution = pipeline.ExecuteAsync(WaitForCancellation).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(999));

        execution.IsCompleted.Should().BeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));
        Func<Task> act = () => execution;
        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public async Task CallerCancellation_IsNotATimeout()
    {
        var time = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();
        var pipeline = Pipeline(time, new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) });

        var execution = pipeline.ExecuteAsync(WaitForCancellation, cts.Token).AsTask();
        await cts.CancelAsync();

        Func<Task> act = () => execution;
        var thrown = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        thrown.Should().NotBeOfType<TimeoutRejectedException>();
    }

    [Fact]
    public async Task CallerCancellation_AfterTheTimeoutFired_IsNotATimeout()
    {
        var time = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();
        var pipeline = Pipeline(time, new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) });

        var execution = pipeline.TryExecuteAsync(async ct =>
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                await cts.CancelAsync(); // the caller also gives up before the callback reports back
                throw;
            }

            return 1;
        }, cts.Token).AsTask();
        time.Advance(TimeSpan.FromSeconds(1));
        var outcome = await execution;

        outcome.IsRejected.Should().BeFalse();
        outcome.Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task CallbackIgnoringTheToken_ReturnsItsResult()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline(time, new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(1) });

        var result = await pipeline.ExecuteAsync(_ =>
        {
            time.Advance(TimeSpan.FromSeconds(5)); // the timeout fires, but the callback does not observe it
            return ValueTask.FromResult(7);
        });

        result.Should().Be(7);
    }

    [Fact]
    public async Task OnTimeout_IsRaised_WithTheTimeout()
    {
        var time = new FakeTimeProvider();
        var raised = new List<TimeSpan>();
        var pipeline = Pipeline(time, new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(3),
            OnTimeout = args => { raised.Add(args.Timeout); return default; },
        });

        var execution = pipeline.TryExecuteAsync(WaitForCancellation).AsTask();
        time.Advance(TimeSpan.FromSeconds(3));
        await execution;

        raised.Should().Equal([TimeSpan.FromSeconds(3)]);
    }

    [Fact]
    public async Task TimeoutGenerator_OverridesTheTimeout_AndZeroMeansNone()
    {
        var time = new FakeTimeProvider();
        var generated = TimeSpan.FromMilliseconds(500);
        var pipeline = Pipeline(time, new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(30), TimeoutGenerator = _ => generated });

        var first = pipeline.TryExecuteAsync(WaitForCancellation).AsTask();
        time.Advance(TimeSpan.FromMilliseconds(500));
        (await first).Rejection.Should().Be(RejectionKind.Timeout);

        generated = TimeSpan.Zero;
        var tokens = new List<bool>();
        await pipeline.ExecuteAsync(ct => { tokens.Add(ct.CanBeCanceled); return ValueTask.FromResult(1); });
        tokens.Should().Equal([false]);
    }

    [Fact]
    public async Task TheCallbackToken_IsLinkedToTheCallerToken_AndTheContextTokenIsRestored()
    {
        var time = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();
        var pipeline = Pipeline(time, new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) });
        var context = ResilienceContextPool.Shared.Get(cts.Token);
        try
        {
            CancellationToken seen = default;
            await pipeline.ExecuteAsync(ctx =>
            {
                seen = ctx.CancellationToken;
                return ValueTask.FromResult(1);
            }, context);

            (seen == cts.Token).Should().BeFalse();
            seen.CanBeCanceled.Should().BeTrue();
            (context.CancellationToken == cts.Token).Should().BeTrue();

            var linked = pipeline.ExecuteAsync(async ctx =>
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ctx.CancellationToken);
                return 1;
            }, context).AsTask();
            await cts.CancelAsync();
            Func<Task> act = () => linked;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public async Task InnerTimeout_UnderRetry_IsRetried_ByTheDefaultPredicate()
    {
        var time = new FakeTimeProvider();
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 2, Delay = TimeSpan.Zero })
            .AddTimeout(TimeSpan.FromSeconds(1))
            .Build();

        var execution = pipeline.ExecuteAsync(async ct =>
        {
            if (++attempts < 3)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
            }

            return attempts;
        }).AsTask();

        // Each Advance fires the attempt timeout that is armed at that moment.
        while (!execution.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }

        (await execution).Should().Be(3);
    }

    [Fact]
    public async Task Handle_TimeoutRejectedException_MatchesATimeoutRejection()
    {
        var time = new FakeTimeProvider();
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder<int>().Handle<TimeoutRejectedException>(),
            })
            .AddTimeout(TimeSpan.FromSeconds(1))
            .Build();

        var execution = pipeline.TryExecuteAsync(async ct =>
        {
            attempts++;
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return 1;
        }).AsTask();

        while (!execution.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }

        (await execution).Rejection.Should().Be(RejectionKind.Timeout);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task NonGenericPipeline_TimesOutVoidCallbacks()
    {
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = time }.AddTimeout(TimeSpan.FromSeconds(1)).Build();

        var execution = pipeline.ExecuteAsync(static async ct => await Task.Delay(System.Threading.Timeout.Infinite, ct)).AsTask();
        time.Advance(TimeSpan.FromSeconds(1));

        Func<Task> act = () => execution;
        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public void InvalidTimeout_IsRejectedWhenAdded()
    {
        Action act = () => new ResiliencePipelineBuilder<int>().AddTimeout(TimeSpan.FromMilliseconds(5));
        act.Should().Throw<System.ComponentModel.DataAnnotations.ValidationException>();
    }
}
