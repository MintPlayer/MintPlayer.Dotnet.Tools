using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

/// <summary>The S2 contract: pooled by default (misuse fails loudly), Task-backed after <c>UsePooledAsync(false)</c>.</summary>
public class PooledValueTaskTests
{
    private static readonly Func<CancellationToken, ValueTask<int>> Suspending = static async _ =>
    {
        await Task.Yield();
        return 42;
    };

    private static ResiliencePipeline<int> Pipeline(bool pooled) => new ResiliencePipelineBuilder<int>()
        .AddRetry(new RetryStrategyOptions<int> { Delay = TimeSpan.Zero })
        .UsePooledAsync(pooled)
        .Build();

    [Fact]
    public async Task Pooled_AwaitingTheSameTaskTwice_Throws()
    {
        var task = Pipeline(pooled: true).ExecuteAsync(Suspending);

        (await task).Should().Be(42);
        Func<Task> second = async () => await task;

        await second.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task NotPooled_AwaitingTheSameTaskTwice_Works()
    {
        var task = Pipeline(pooled: false).ExecuteAsync(Suspending);

        (await task).Should().Be(42);
        (await task).Should().Be(42);
    }

    [Fact]
    public async Task NotPooled_TaskCanBeObservedFromTwoPlaces()
    {
        var task = Pipeline(pooled: false).ExecuteAsync(Suspending);

        var results = await Task.WhenAll(Awaiter(task), Awaiter(task));

        results.Should().Equal([42, 42]);

        static async Task<int> Awaiter(ValueTask<int> t) => await t;
    }

    [Fact]
    public async Task Pooled_AsTask_CanBeAwaitedManyTimes()
    {
        var task = Pipeline(pooled: true).ExecuteAsync(Suspending).AsTask();

        (await task).Should().Be(42);
        (await task).Should().Be(42);
    }

    [Fact]
    public async Task NotPooled_VoidExecution_CanBeAwaitedTwice()
    {
        var pipeline = new ResiliencePipelineBuilder().UsePooledAsync(false).AddRetry(new RetryStrategyOptions { Delay = TimeSpan.Zero }).Build();
        var calls = 0;

        var task = pipeline.ExecuteAsync(async _ => { await Task.Yield(); calls++; });

        await task;
        await task;
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Pooled_ManyConcurrentSuspendingExecutions_AllReturnTheirOwnResult()
    {
        var pipeline = Pipeline(pooled: true);

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(worker => Task.Run(async () =>
        {
            var wrong = 0;
            for (var i = 0; i < 500; i++)
            {
                var value = worker * 1_000 + i;
                var result = await pipeline.ExecuteAsync(static async (v, _) => { await Task.Yield(); return v; }, value);
                if (result != value)
                {
                    wrong++;
                }
            }

            return wrong;
        })));

        results.Sum().Should().Be(0);
    }

    [Fact]
    public async Task Pooled_TryExecuteAsync_SuspendingCallback_ReturnsTheOutcome()
    {
        var outcome = await Pipeline(pooled: true).TryExecuteAsync(Suspending);

        outcome.Result.Should().Be(42);
    }
}
