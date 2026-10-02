using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

public class FallbackTests
{
    private static ResiliencePipeline<int> Pipeline(FallbackStrategyOptions<int> options) => new ResiliencePipelineBuilder<int>().AddFallback(options).Build();

    [Fact]
    public async Task HandledException_IsReplacedByTheFallbackResult()
    {
        var pipeline = Pipeline(new FallbackStrategyOptions<int> { FallbackAction = static _ => Outcome.FromResultAsValueTask(99) });

        var result = await pipeline.ExecuteAsync(static _ => throw new InvalidOperationException());

        result.Should().Be(99);
    }

    [Fact]
    public async Task HandledResult_IsReplaced_UnhandledResultIsKept()
    {
        var pipeline = Pipeline(new FallbackStrategyOptions<int>
        {
            ShouldHandle = new PredicateBuilder<int>().HandleResult(-1),
            FallbackAction = static _ => Outcome.FromResultAsValueTask(0),
        });

        (await pipeline.ExecuteAsync(static _ => ValueTask.FromResult(-1))).Should().Be(0);
        (await pipeline.ExecuteAsync(static _ => ValueTask.FromResult(5))).Should().Be(5);
    }

    [Fact]
    public async Task FallbackAction_SeesTheOriginalOutcome()
    {
        Exception? seen = null;
        var pipeline = Pipeline(new FallbackStrategyOptions<int>
        {
            FallbackAction = args =>
            {
                seen = args.Outcome.Exception;
                return Outcome.FromResultAsValueTask(1);
            },
        });

        var original = new InvalidOperationException("original");
        await pipeline.ExecuteAsync(_ => throw original);

        seen.Should().BeSameAs(original);
    }

    [Fact]
    public async Task OnFallback_RunsBeforeTheAction()
    {
        var order = new List<string>();
        var pipeline = Pipeline(new FallbackStrategyOptions<int>
        {
            OnFallback = args => { order.Add($"on:{args.Outcome.Exception?.Message}"); return default; },
            FallbackAction = _ => { order.Add("action"); return Outcome.FromResultAsValueTask(1); },
        });

        await pipeline.ExecuteAsync(static _ => throw new InvalidOperationException("boom"));

        order.Should().Equal(["on:boom", "action"]);
    }

    [Fact]
    public async Task ThrowingFallbackAction_BecomesTheOutcome()
    {
        var pipeline = Pipeline(new FallbackStrategyOptions<int> { FallbackAction = static _ => throw new FormatException("from fallback") });

        var outcome = await pipeline.TryExecuteAsync(static _ => throw new InvalidOperationException());

        outcome.Exception.Should().BeOfType<FormatException>();
    }

    [Fact]
    public async Task FallbackAction_MayReturnAnException()
    {
        var pipeline = Pipeline(new FallbackStrategyOptions<int> { FallbackAction = static _ => Outcome.FromExceptionAsValueTask<int>(new TimeoutException("replaced")) });

        Func<Task> act = async () => await pipeline.ExecuteAsync(static _ => throw new InvalidOperationException());

        await act.Should().ThrowAsync<TimeoutException>().WithMessage("replaced");
    }

    [Fact]
    public async Task AsyncFallbackAction_ThatSuspends_Completes()
    {
        var pipeline = Pipeline(new FallbackStrategyOptions<int>
        {
            FallbackAction = static async _ =>
            {
                await Task.Yield();
                return Outcome.FromResult(3);
            },
        });

        (await pipeline.ExecuteAsync(static _ => throw new InvalidOperationException())).Should().Be(3);
    }

    [Fact]
    public async Task Cancellation_IsNotHandledByTheDefaultPredicate()
    {
        var pipeline = Pipeline(new FallbackStrategyOptions<int> { FallbackAction = static _ => Outcome.FromResultAsValueTask(1) });

        Func<Task> act = async () => await pipeline.ExecuteAsync(static _ => throw new OperationCanceledException());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Fallback_HandlesAnInnerTimeoutRejection()
    {
        var time = new FakeTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddFallback(new FallbackStrategyOptions<int>
            {
                ShouldHandle = static args => args.Outcome.Rejection == RejectionKind.Timeout,
                FallbackAction = static _ => Outcome.FromResultAsValueTask(-5),
            })
            .AddTimeout(TimeSpan.FromSeconds(1))
            .Build();

        var execution = pipeline.ExecuteAsync(static async ct =>
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return 1;
        }).AsTask();
        time.Advance(TimeSpan.FromSeconds(1));

        (await execution).Should().Be(-5);
    }

    [Fact]
    public async Task FallbackOutsideRetry_SeesOnlyTheFinalOutcome()
    {
        var attempts = 0;
        var fallbacks = 0;
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddFallback(new FallbackStrategyOptions<int>
            {
                FallbackAction = _ => { fallbacks++; return Outcome.FromResultAsValueTask(0); },
            })
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 2, Delay = TimeSpan.Zero })
            .Build();

        var result = await pipeline.ExecuteAsync(_ => { attempts++; throw new InvalidOperationException(); });

        result.Should().Be(0);
        attempts.Should().Be(3);
        fallbacks.Should().Be(1);
    }

    [Fact]
    public void MissingFallbackAction_IsRejectedWhenAdded()
    {
        Action act = () => new ResiliencePipelineBuilder<int>().AddFallback(new FallbackStrategyOptions<int>());
        act.Should().Throw<System.ComponentModel.DataAnnotations.ValidationException>();
    }
}
