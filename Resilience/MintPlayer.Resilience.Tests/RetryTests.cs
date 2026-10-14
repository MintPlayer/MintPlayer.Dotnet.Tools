using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

public class RetryTests
{
    private static RetryStrategyOptions<int> NoDelay(int maxRetries = 3) => new()
    {
        MaxRetryAttempts = maxRetries,
        Delay = TimeSpan.Zero,
        ShouldHandle = new PredicateBuilder<int>().Handle<InvalidOperationException>().HandleResult(-1),
    };

    [Fact]
    public async Task AlwaysFailing_MakesMaxRetryAttemptsPlusOneAttempts_ThenThrowsTheLastException()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(NoDelay(3)).Build();
        var attempts = 0;

        Func<Task> act = async () => await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException($"attempt {attempts}");
        });

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("attempt 4");
        attempts.Should().Be(4);
    }

    [Fact]
    public async Task SucceedingOnTheSecondAttempt_StopsRetrying()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(NoDelay(5)).Build();
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ => ++attempts < 2 ? ValueTask.FromResult(-1) : ValueTask.FromResult(42));

        result.Should().Be(42);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task HandledResult_OnTheLastAttempt_IsReturned()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(NoDelay(2)).Build();
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ => { attempts++; return ValueTask.FromResult(-1); });

        result.Should().Be(-1);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task UnhandledException_IsNotRetried()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(NoDelay(3)).Build();
        var attempts = 0;

        Func<Task> act = async () => await pipeline.ExecuteAsync(_ => { attempts++; throw new ArgumentException("no"); });

        await act.Should().ThrowAsync<ArgumentException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task DefaultPredicate_RetriesExceptions_ButNotCancellation()
    {
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 2, Delay = TimeSpan.Zero })
            .Build();

        var failures = 0;
        Func<Task> fails = async () => await pipeline.ExecuteAsync(_ => { failures++; throw new InvalidOperationException(); });
        await fails.Should().ThrowAsync<InvalidOperationException>();
        failures.Should().Be(3);

        var cancellations = 0;
        Func<Task> cancels = async () => await pipeline.ExecuteAsync(_ => { cancellations++; throw new OperationCanceledException(); });
        await cancels.Should().ThrowAsync<OperationCanceledException>();
        cancellations.Should().Be(1);
    }

    [Fact]
    public async Task OnRetry_ReceivesZeroBasedAttemptNumbers_TheOutcome_AndTheDelay()
    {
        var events = new List<(int Attempt, int Result, TimeSpan Delay)>();
        var options = NoDelay(3);
        options.OnRetry = args =>
        {
            events.Add((args.AttemptNumber, args.Outcome.Result, args.RetryDelay));
            return default;
        };
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(options).Build();

        await pipeline.ExecuteAsync(static _ => ValueTask.FromResult(-1));

        events.Should().Equal([(0, -1, TimeSpan.Zero), (1, -1, TimeSpan.Zero), (2, -1, TimeSpan.Zero)]);
    }

    [Fact]
    public async Task PredicateSeesEveryAttempt_IncludingTheLast()
    {
        var seen = new List<int>();
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => { seen.Add(args.AttemptNumber); return true; },
            })
            .Build();

        await pipeline.ExecuteAsync(static _ => ValueTask.FromResult(1));

        seen.Should().Equal([0, 1, 2]);
    }

    [Theory]
    [InlineData(DelayBackoffType.Constant, new[] { 200, 200, 200 })]
    [InlineData(DelayBackoffType.Linear, new[] { 200, 400, 600 })]
    [InlineData(DelayBackoffType.Exponential, new[] { 200, 400, 800 })]
    public async Task Backoff_WaitsTheComputedDelay_OnTheTimeProvider(DelayBackoffType backoff, int[] expectedMs)
    {
        var time = new DelayRecordingTimeProvider();
        var attemptTimes = new List<DateTimeOffset>();
        var builder = new ResiliencePipelineBuilder<int> { TimeProvider = time };
        var pipeline = builder
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 3,
                BackoffType = backoff,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = static args => args.Outcome.Result < 0,
            })
            .Build();

        var execution = pipeline.ExecuteAsync(_ =>
        {
            attemptTimes.Add(time.GetUtcNow());
            return ValueTask.FromResult(-1);
        }).AsTask();

        var delays = new List<TimeSpan>();
        for (var i = 0; i < expectedMs.Length; i++)
        {
            delays.Add(await time.AdvanceNextDelayAsync());
        }

        (await execution).Should().Be(-1);
        delays.Should().Equal(expectedMs.Select(ms => TimeSpan.FromMilliseconds(ms)));
        attemptTimes.Should().HaveCount(4);
        for (var i = 0; i < expectedMs.Length; i++)
        {
            (attemptTimes[i + 1] - attemptTimes[i]).Should().Be(TimeSpan.FromMilliseconds(expectedMs[i]));
        }
    }

    [Fact]
    public async Task NextAttempt_DoesNotStart_BeforeTheDelayElapses()
    {
        var time = new DelayRecordingTimeProvider();
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 1, Delay = TimeSpan.FromSeconds(1), ShouldHandle = static args => args.Outcome.Result < 0 })
            .Build();

        var execution = pipeline.ExecuteAsync(_ => ValueTask.FromResult(++attempts == 1 ? -1 : 7)).AsTask();
        var delay = await time.NextDelayAsync();

        delay.Should().Be(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromMilliseconds(999));
        execution.IsCompleted.Should().BeFalse();
        attempts.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(1));
        (await execution).Should().Be(7);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task MaxDelay_CapsTheComputedDelay()
    {
        var delays = new List<TimeSpan>();
        var time = new DelayRecordingTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(3),
                ShouldHandle = static args => args.Outcome.Result < 0,
                OnRetry = args => { delays.Add(args.RetryDelay); return default; },
            })
            .Build();

        var execution = pipeline.ExecuteAsync(static _ => ValueTask.FromResult(-1)).AsTask();
        for (var i = 0; i < 4; i++)
        {
            await time.AdvanceNextDelayAsync();
        }

        await execution;
        delays.Should().Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)]);
    }

    [Fact]
    public async Task DelayGenerator_OverridesTheBackoff_NullAndNegativeKeepIt()
    {
        var delays = new List<TimeSpan>();
        var generated = new TimeSpan?[] { TimeSpan.FromMilliseconds(5), null, TimeSpan.FromMilliseconds(-1) };
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                ShouldHandle = static args => args.Outcome.Result < 0,
                DelayGenerator = args => generated[args.AttemptNumber],
                OnRetry = args => { delays.Add(args.RetryDelay); return default; },
            })
            .Build();

        await pipeline.ExecuteAsync(static _ => ValueTask.FromResult(-1));

        delays.Should().Equal([TimeSpan.FromMilliseconds(5), TimeSpan.Zero, TimeSpan.Zero]);
    }

    [Fact]
    public async Task Jitter_UsesTheRandomizer_WithinPollysBounds()
    {
        // Constant backoff with jitter: delay * (0.75 + 0.5 * random).
        var time = new DelayRecordingTimeProvider();
        var randomizer = new SequenceRandomizer(0.0, 1.0, 0.5);
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                UseJitter = true,
                Randomizer = randomizer.Next,
                ShouldHandle = static args => args.Outcome.Result < 0,
            })
            .Build();

        var execution = pipeline.ExecuteAsync(static _ => ValueTask.FromResult(-1)).AsTask();
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 3; i++)
        {
            delays.Add(await time.AdvanceNextDelayAsync());
        }

        await execution;
        delays.Should().Equal([TimeSpan.FromMilliseconds(750), TimeSpan.FromMilliseconds(1250), TimeSpan.FromMilliseconds(1000)]);
    }

    [Fact]
    public async Task ExponentialJitter_IsPollysDecorrelatedJitter_CarriedAcrossRetries()
    {
        var time = new DelayRecordingTimeProvider();
        var reference = new SequenceRandomizer(0.3, 0.7, 0.1, 0.9);
        var expected = new List<TimeSpan>();
        double prev = 0;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            expected.Add(PollyDecorrelatedJitter(attempt, TimeSpan.FromMilliseconds(100), ref prev, reference.Next()));
        }

        var jittered = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(100),
                UseJitter = true,
                Randomizer = new SequenceRandomizer(0.3, 0.7, 0.1, 0.9).Next,
                ShouldHandle = static args => args.Outcome.Result < 0,
            })
            .Build();

        var execution = jittered.ExecuteAsync(static _ => ValueTask.FromResult(-1)).AsTask();
        var observed = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            observed.Add(await time.AdvanceNextDelayAsync());
        }

        await execution;
        observed.Should().Equal(expected);
    }

    // Polly 8.8.0 RetryHelper.DecorrelatedJitterBackoffV2, restated independently.
    private static TimeSpan PollyDecorrelatedJitter(int attempt, TimeSpan baseDelay, ref double prev, double random)
    {
        var t = attempt + random;
        var next = Math.Pow(2.0, t) * Math.Tanh(Math.Sqrt(4.0 * t));
        var ticks = (long)Math.Min((next - prev) * (1 / 1.4d) * baseDelay.Ticks, (double)TimeSpan.MaxValue.Ticks - 1_000);
        prev = next;
        return TimeSpan.FromTicks(ticks);
    }

    [Fact]
    public async Task CancellationDuringTheDelay_EndsWithOperationCanceled()
    {
        var time = new DelayRecordingTimeProvider();
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 3, Delay = TimeSpan.FromSeconds(1), ShouldHandle = static args => args.Outcome.Result < 0 })
            .Build();

        var execution = pipeline.ExecuteAsync(_ => { attempts++; return ValueTask.FromResult(-1); }, cts.Token).AsTask();
        await time.NextDelayAsync();
        await cts.CancelAsync();

        Func<Task> act = () => execution;
        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task RetriedResult_IsDisposed_TheReturnedOneIsNot()
    {
        var results = new List<DisposableResult>();
        var pipeline = new ResiliencePipelineBuilder<DisposableResult>()
            .AddRetry(new RetryStrategyOptions<DisposableResult> { MaxRetryAttempts = 2, Delay = TimeSpan.Zero, ShouldHandle = static args => args.Outcome.Result!.Value < 0 })
            .Build();

        var final = await pipeline.ExecuteAsync(_ =>
        {
            var result = new DisposableResult(results.Count < 2 ? -1 : 1);
            results.Add(result);
            return ValueTask.FromResult(result);
        });

        results.Should().HaveCount(3);
        results[0].IsDisposed.Should().BeTrue();
        results[1].IsDisposed.Should().BeTrue();
        final.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task OnRetry_CanUseTheContext_AndTheCallbackSeesItsProperties()
    {
        var key = new ResiliencePropertyKey<int>("retries");
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            var options = NoDelay(2);
            options.OnRetry = args =>
            {
                args.Context.Properties.Set(key, args.AttemptNumber + 1);
                return default;
            };
            var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(options).Build();

            var seen = new List<int>();
            await pipeline.ExecuteAsync(ctx =>
            {
                seen.Add(ctx.Properties.GetValue(key, 0));
                return ValueTask.FromResult(-1);
            }, context);

            seen.Should().Equal([0, 1, 2]);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public async Task OnRetry_ReadingTheContext_WithoutPassingOne_GetsAWorkingContext()
    {
        var tokens = new List<bool>();
        using var cts = new CancellationTokenSource();
        var options = NoDelay(1);
        options.OnRetry = args =>
        {
            tokens.Add(args.Context.CancellationToken == cts.Token);
            return default;
        };
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(options).Build();

        await pipeline.ExecuteAsync(static _ => ValueTask.FromResult(-1), cts.Token);

        tokens.Should().Equal([true]);
    }

    [Fact]
    public void Sync_Execute_Retries()
    {
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(NoDelay(3)).Build();

        var result = pipeline.Execute(() => ++attempts < 3 ? -1 : 3);

        result.Should().Be(3);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task NonGenericPipeline_RetriesAnyResultType_AndVoidCallbacks()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>().HandleResult(r => r is -1),
            })
            .Build();

        var intAttempts = 0;
        (await pipeline.ExecuteAsync(_ => ValueTask.FromResult(++intAttempts < 3 ? -1 : 5))).Should().Be(5);
        intAttempts.Should().Be(3);

        var voidAttempts = 0;
        Func<Task> act = async () => await pipeline.ExecuteAsync(_ =>
        {
            voidAttempts++;
            throw new InvalidOperationException();
        });
        await act.Should().ThrowAsync<InvalidOperationException>();
        voidAttempts.Should().Be(3);

        var stringAttempts = 0;
        pipeline.Execute(() => { stringAttempts++; return "ok"; }).Should().Be("ok");
        stringAttempts.Should().Be(1);
    }

    [Fact]
    public async Task NonGenericOptions_DefaultPredicate_WorksForValueTypes()
    {
        var pipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.Zero }).Build();
        var attempts = 0;

        Func<Task> act = async () => await pipeline.ExecuteAsync<int>(_ => { attempts++; throw new TimeoutException(); });

        await act.Should().ThrowAsync<TimeoutException>();
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task InfiniteRetries_KeepGoing()
    {
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = int.MaxValue, Delay = TimeSpan.Zero, ShouldHandle = static args => args.Outcome.Result < 0 })
            .Build();

        (await pipeline.ExecuteAsync(_ => ValueTask.FromResult(++attempts < 100 ? -1 : 100))).Should().Be(100);
    }
}
