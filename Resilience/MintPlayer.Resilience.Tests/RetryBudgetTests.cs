using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

/// <summary>The retry budget: its arithmetic, its window, and its use by retry strategies in several pipelines.</summary>
public class RetryBudgetTests
{
    private static int Withdrawals(RetryBudget budget, int max = 10_000)
    {
        var granted = 0;
        while (granted < max && budget.TryWithdraw())
        {
            granted++;
        }

        return granted;
    }

    private static void Deposit(RetryBudget budget, int count)
    {
        for (var i = 0; i < count; i++)
        {
            budget.Deposit();
        }
    }

    [Fact]
    public void Ratio_AllowsThatShareOfTheRequests()
    {
        var budget = new RetryBudget(retryRatio: 0.5, minRetriesPerSecond: 0, timeProvider: new FakeTimeProvider());
        Deposit(budget, 10);

        budget.Balance.Should().Be(5);
        Withdrawals(budget).Should().Be(5);
        budget.Balance.Should().Be(0);
        budget.TryWithdraw().Should().BeFalse();
    }

    [Fact]
    public void FractionalAllowance_RoundsDown()
    {
        var budget = new RetryBudget(retryRatio: 0.2, minRetriesPerSecond: 0, timeProvider: new FakeTimeProvider());
        Deposit(budget, 14);                                    // 2.8 retries

        Withdrawals(budget).Should().Be(2);
        budget.Deposit();                                       // 3.0
        Withdrawals(budget).Should().Be(1);
    }

    [Fact]
    public void Reserve_IsMinRetriesPerSecondTimesTheTimeToLive_WhateverTheTraffic()
    {
        var budget = new RetryBudget(retryRatio: 0, minRetriesPerSecond: 2, timeToLive: TimeSpan.FromSeconds(10), timeProvider: new FakeTimeProvider());

        Withdrawals(budget).Should().Be(20);
        Deposit(budget, 1000);
        budget.TryWithdraw().Should().BeFalse();
    }

    [Fact]
    public void ReserveAndRatio_Add()
    {
        var budget = new RetryBudget(retryRatio: 0.1, minRetriesPerSecond: 1, timeToLive: TimeSpan.FromSeconds(5), timeProvider: new FakeTimeProvider());
        Deposit(budget, 50);

        Withdrawals(budget).Should().Be(10);                    // 5 reserve + 0.1 × 50
    }

    [Fact]
    public void ZeroBudget_AllowsNoRetry()
    {
        var budget = new RetryBudget(retryRatio: 0, minRetriesPerSecond: 0, timeProvider: new FakeTimeProvider());
        Deposit(budget, 100);

        budget.TryWithdraw().Should().BeFalse();
        budget.Balance.Should().Be(0);
    }

    [Fact]
    public void RequestsAndRetries_ExpireAfterTheTimeToLive_OneSegmentAtATime()
    {
        // TTL 10 s in 10 segments of 1 s.
        var time = new FakeTimeProvider();
        var budget = new RetryBudget(retryRatio: 1, minRetriesPerSecond: 0, timeToLive: TimeSpan.FromSeconds(10), timeProvider: time);

        Deposit(budget, 4);                                     // segment 0
        time.Advance(TimeSpan.FromSeconds(5));
        Deposit(budget, 2);                                     // segment 5
        Withdrawals(budget).Should().Be(6);                     // 6 retries in segment 5

        time.Advance(TimeSpan.FromMilliseconds(4999));          // 9.999 s: segment 9, everything still counts
        budget.TryWithdraw().Should().BeFalse();

        time.Advance(TimeSpan.FromMilliseconds(1));             // 10 s: segment 10, segment 0 (4 requests) expired
        budget.Balance.Should().Be(0);                          // 2 requests, 6 retries
        Deposit(budget, 5);                                     // 7 requests, 6 retries
        Withdrawals(budget).Should().Be(1);

        time.Advance(TimeSpan.FromSeconds(5));                  // 15 s: segment 5 expired
        Withdrawals(budget).Should().Be(4);                     // 5 requests (segment 10), 1 retry (segment 10)
    }

    [Fact]
    public void LongIdle_ForgetsEverything()
    {
        var time = new FakeTimeProvider();
        var budget = new RetryBudget(retryRatio: 1, minRetriesPerSecond: 0, timeProvider: time);
        Deposit(budget, 3);
        Withdrawals(budget).Should().Be(3);

        time.Advance(TimeSpan.FromDays(3));
        budget.TryWithdraw().Should().BeFalse();
        Deposit(budget, 2);
        Withdrawals(budget).Should().Be(2);
    }

    [Fact]
    public void Properties_ReflectTheConstructorArguments_AndDefaults()
    {
        var budget = new RetryBudget();
        budget.RetryRatio.Should().Be(0.2);
        budget.MinRetriesPerSecond.Should().Be(10);
        budget.TimeToLive.Should().Be(TimeSpan.FromSeconds(10));
        budget.Balance.Should().Be(100);
    }

    [Theory]
    [InlineData(-0.1, 10, 10)]
    [InlineData(1000.5, 10, 10)]
    [InlineData(double.NaN, 10, 10)]
    [InlineData(0.2, -1, 10)]
    [InlineData(0.2, 1_000_001, 10)]
    [InlineData(0.2, 10, 0.5)]
    [InlineData(0.2, 10, 3601)]
    public void InvalidArguments_Throw(double ratio, int minPerSecond, double ttlSeconds)
    {
        Action act = () => _ = new RetryBudget(ratio, minPerSecond, TimeSpan.FromSeconds(ttlSeconds));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- In a retry strategy ----

    private static ResiliencePipeline<int> Retrying(RetryBudget budget, int maxRetries = 3, Func<OnRetryBudgetExhaustedArguments<int>, ValueTask>? onExhausted = null) =>
        new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.Zero,
                ShouldHandle = static args => args.Outcome.Result < 0,
                Budget = budget,
                OnBudgetExhausted = onExhausted,
            })
            .Build();

    private static async Task<int> Attempts(ResiliencePipeline<int> pipeline, bool fail)
    {
        var attempts = 0;
        await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return new ValueTask<int>(fail ? -1 : 1);
        });
        return attempts;
    }

    [Fact]
    public async Task ExhaustedBudget_StopsRetrying_AndReturnsTheLastOutcome()
    {
        var budget = new RetryBudget(retryRatio: 0, minRetriesPerSecond: 0, timeProvider: new FakeTimeProvider());
        var exhausted = new List<(int Attempt, int Result)>();
        var pipeline = Retrying(budget, onExhausted: args =>
        {
            exhausted.Add((args.AttemptNumber, args.Outcome.Result));
            return default;
        });

        var attempts = 0;
        var outcome = await pipeline.TryExecuteAsync(_ => new ValueTask<int>(-(++attempts)));

        attempts.Should().Be(1);
        outcome.Result.Should().Be(-1);
        exhausted.Should().Equal((0, -1));
    }

    [Fact]
    public async Task EveryExecution_DepositsOnce_AndEveryRetryWithdrawsOnce()
    {
        var budget = new RetryBudget(retryRatio: 0.5, minRetriesPerSecond: 0, timeProvider: new FakeTimeProvider());
        var pipeline = Retrying(budget, maxRetries: 10);

        for (var i = 0; i < 6; i++)
        {
            (await Attempts(pipeline, fail: false)).Should().Be(1);
        }

        // 7 requests (this one included) allow 3.5 retries: 3 retries, 4 attempts.
        (await Attempts(pipeline, fail: true)).Should().Be(4);
        budget.Balance.Should().Be(0);
    }

    [Fact]
    public async Task Budget_IsSharedAcrossPipelines()
    {
        var budget = new RetryBudget(retryRatio: 0.5, minRetriesPerSecond: 0, timeProvider: new FakeTimeProvider());
        var a = Retrying(budget);
        var b = Retrying(budget);

        for (var i = 0; i < 4; i++)
        {
            await Attempts(a, fail: false);
        }

        // b: 5 requests → 2.5 retries → 2 retries (3 attempts), then out of budget.
        (await Attempts(b, fail: true)).Should().Be(3);

        // a: 6 requests → 3 retries allowed, 2 used → 1 more (2 attempts).
        (await Attempts(a, fail: true)).Should().Be(2);

        // b again: 7 requests → 3.5, 3 used → none.
        (await Attempts(b, fail: true)).Should().Be(1);
    }

    [Fact]
    public async Task Budget_DoesNotLimitBelowMaxRetryAttempts()
    {
        var budget = new RetryBudget(retryRatio: 0, minRetriesPerSecond: 100, timeProvider: new FakeTimeProvider());
        var pipeline = Retrying(budget, maxRetries: 2);

        (await Attempts(pipeline, fail: true)).Should().Be(3);
        budget.Balance.Should().Be(998);
    }

    [Fact]
    public async Task NonRetriedOutcomes_DoNotWithdraw()
    {
        var budget = new RetryBudget(retryRatio: 0, minRetriesPerSecond: 1, timeToLive: TimeSpan.FromSeconds(1), timeProvider: new FakeTimeProvider());
        var pipeline = Retrying(budget);

        for (var i = 0; i < 5; i++)
        {
            await Attempts(pipeline, fail: false);
        }

        budget.Balance.Should().Be(1);
    }

    [Fact]
    public async Task NonGenericRetryOptions_UseTheBudget()
    {
        var budget = new RetryBudget(retryRatio: 0, minRetriesPerSecond: 1, timeToLive: TimeSpan.FromSeconds(1), timeProvider: new FakeTimeProvider());
        var exhausted = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                Delay = TimeSpan.Zero,
                ShouldHandle = static args => args.Outcome.Exception is InvalidOperationException,
                Budget = budget,
                OnBudgetExhausted = args =>
                {
                    exhausted++;
                    args.AttemptNumber.Should().Be(1);
                    return default;
                },
            })
            .Build();

        var attempts = 0;
        var outcome = await pipeline.TryExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        attempts.Should().Be(2);                                // one retry from the reserve of 1
        outcome.Exception.Should().BeOfType<InvalidOperationException>();
        exhausted.Should().Be(1);
    }
}
