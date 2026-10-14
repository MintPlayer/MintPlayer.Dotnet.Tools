using System.ComponentModel.DataAnnotations;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

public class PipelineTests
{
    [Fact]
    public async Task Empty_RunsTheCallbackOnce()
    {
        var calls = 0;

        (await ResiliencePipeline<int>.Empty.ExecuteAsync(_ => ValueTask.FromResult(++calls))).Should().Be(1);
        ResiliencePipeline<int>.Empty.Execute(() => ++calls).Should().Be(2);
        await ResiliencePipeline.Empty.ExecuteAsync(_ => { calls++; return default; });
        ResiliencePipeline.Empty.Execute(() => { calls++; });

        calls.Should().Be(4);
    }

    [Fact]
    public async Task Empty_PropagatesExceptions()
    {
        Func<Task> act = async () => await ResiliencePipeline<int>.Empty.ExecuteAsync(static _ => throw new FormatException());
        await act.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task Strategies_RunInTheOrderTheyWereAdded_FirstOutermost()
    {
        var log = new List<string>();
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddFallback(new FallbackStrategyOptions<int>
            {
                ShouldHandle = args => { log.Add($"fallback? {args.Outcome.Result}"); return args.Outcome.Result < 0; },
                FallbackAction = _ => Outcome.FromResultAsValueTask(100),
            })
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => { log.Add($"retry? {args.AttemptNumber}"); return args.Outcome.Result < 0; },
            })
            .Build();

        var result = await pipeline.ExecuteAsync(_ => { log.Add("call"); return ValueTask.FromResult(-1); });

        result.Should().Be(100);
        log.Should().Equal(["call", "retry? 0", "call", "retry? 1", "fallback? -1"]);
    }

    [Fact]
    public async Task StateOverload_PassesTheState()
    {
        var pipeline = new ResiliencePipelineBuilder<string>().AddRetry(new RetryStrategyOptions<string> { Delay = TimeSpan.Zero }).Build();

        var result = await pipeline.ExecuteAsync(static (state, _) => ValueTask.FromResult(state.Name + state.Id), (Name: "id-", Id: 7));

        result.Should().Be("id-7");
    }

    [Fact]
    public async Task CallerToken_ReachesTheCallback()
    {
        using var cts = new CancellationTokenSource();
        var pipeline = new ResiliencePipelineBuilder<bool>().AddRetry(new RetryStrategyOptions<bool> { Delay = TimeSpan.Zero }).Build();

        (await pipeline.ExecuteAsync(ct => ValueTask.FromResult(ct == cts.Token), cts.Token)).Should().BeTrue();
    }

    [Fact]
    public async Task ContextOverload_PassesTheCallersContext()
    {
        var key = new ResiliencePropertyKey<string>("user");
        var context = ResilienceContextPool.Shared.Get("op-key");
        context.Properties.Set(key, "alice");
        try
        {
            var pipeline = new ResiliencePipelineBuilder<string>().AddRetry(new RetryStrategyOptions<string> { Delay = TimeSpan.Zero }).Build();

            var result = await pipeline.ExecuteAsync(
                static (ctx, suffix) => ValueTask.FromResult(ctx.OperationKey + ":" + ctx.Properties.GetValue(new ResiliencePropertyKey<string>("user"), "?") + suffix),
                context,
                "!");

            result.Should().Be("op-key:alice!");
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public void SyncOverloads_ReturnResults()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(new RetryStrategyOptions<int> { Delay = TimeSpan.Zero }).Build();
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            pipeline.Execute(() => 1).Should().Be(1);
            pipeline.Execute(_ => 2).Should().Be(2);
            pipeline.Execute(static s => s, 3).Should().Be(3);
            pipeline.Execute(static (s, _) => s, 4).Should().Be(4);
            pipeline.Execute(static (ResilienceContext _) => 5, context).Should().Be(5);
            pipeline.Execute(static (_, s) => s, context, 6).Should().Be(6);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public void SyncExecute_ThrowsTheCallbackException()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 1, Delay = TimeSpan.Zero }).Build();

        Action act = () => pipeline.Execute(static int () => throw new FormatException());

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public async Task NonGenericPipeline_RunsVoidAndTypedCallbacks_ThroughTheSameStrategies()
    {
        var retries = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                OnRetry = _ => { retries++; return default; },
            })
            .Build();

        var voidCalls = 0;
        await pipeline.ExecuteAsync(_ => { if (voidCalls++ == 0) throw new InvalidOperationException(); return default; });
        voidCalls.Should().Be(2);

        var stringCalls = 0;
        (await pipeline.ExecuteAsync(_ => ++stringCalls == 1 ? throw new InvalidOperationException() : ValueTask.FromResult("s"))).Should().Be("s");

        var intCalls = 0;
        pipeline.Execute(() => ++intCalls == 1 ? throw new InvalidOperationException() : 9).Should().Be(9);

        pipeline.Execute(static (s, _) => s * 2, 21).Should().Be(42);
        retries.Should().Be(3);
    }

    [Fact]
    public async Task NonGenericPipeline_HandleResult_SeesTheTypedResultAsObject()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder().HandleResult(r => r is "again"),
            })
            .Build();

        var calls = 0;
        var result = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(++calls < 3 ? "again" : "done"));

        result.Should().Be("done");
        calls.Should().Be(3);
    }

    [Fact]
    public async Task NonGenericPipeline_IsSafeUnderConcurrentFirstUseOfManyResultTypes()
    {
        var pipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { Delay = TimeSpan.Zero }).Build();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(async () =>
        {
            switch (i % 4)
            {
                case 0: (await pipeline.ExecuteAsync(_ => ValueTask.FromResult(i))).Should().Be(i); break;
                case 1: (await pipeline.ExecuteAsync(_ => ValueTask.FromResult(i.ToString()))).Should().Be(i.ToString()); break;
                case 2: (await pipeline.ExecuteAsync(_ => ValueTask.FromResult((long)i))).Should().Be(i); break;
                default: (await pipeline.ExecuteAsync(_ => ValueTask.FromResult(i * 0.5))).Should().Be(i * 0.5); break;
            }
        })));
    }

    [Fact]
    public void AddingAfterBuild_Throws()
    {
        var builder = new ResiliencePipelineBuilder<int>().AddRetry(new RetryStrategyOptions<int>());
        builder.Build();

        Action act = () => builder.AddRetry(new RetryStrategyOptions<int>());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task OptionsChangedAfterBuild_DoNotAffectTheBuiltPipeline()
    {
        var options = new RetryStrategyOptions<int> { MaxRetryAttempts = 1, Delay = TimeSpan.Zero, ShouldHandle = static args => args.Outcome.Result < 0 };
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(options).Build();
        options.MaxRetryAttempts = 10;

        var calls = 0;
        await pipeline.ExecuteAsync(_ => { calls++; return ValueTask.FromResult(-1); });

        calls.Should().Be(2);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(3, -1)]
    [InlineData(3, 86_400_001)]
    public void InvalidRetryOptions_AreRejectedWhenAdded(int maxRetryAttempts, int delayMs)
    {
        Action act = () => new ResiliencePipelineBuilder<int>().AddRetry(new RetryStrategyOptions<int>
        {
            MaxRetryAttempts = maxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(delayMs),
        });

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void NullOptions_AreRejected()
    {
        Action act = () => new ResiliencePipelineBuilder<int>().AddRetry(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ConcurrentExecutions_OnOnePipeline_AreIndependent()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 2, Delay = TimeSpan.Zero, ShouldHandle = static args => args.Outcome.Result < 0 })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();

        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            var attempts = 0;
            return await pipeline.ExecuteAsync(async _ =>
            {
                await Task.Yield();
                return ++attempts < 3 ? -1 : i;
            });
        })));

        results.Should().Equal(Enumerable.Range(0, 200));
    }
}
