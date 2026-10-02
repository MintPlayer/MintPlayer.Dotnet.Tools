using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Hedging;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Timeout;
using ResilienceSamples;
using static MintPlayer.Resilience.SourceGenerator.Tests.Behaviour.Script;

namespace MintPlayer.Resilience.SourceGenerator.Tests.Behaviour;

/// <summary>The generated and interpreter pipelines share static state (the samples, the journal); these tests run alone.</summary>
[CollectionDefinition(nameof(GeneratedPipelines), DisableParallelization = true)]
public sealed class GeneratedPipelines;

/// <summary>
/// Behavioural parity (plan M4): a generated pipeline and the equivalent runtime-builder pipeline, driven by the same
/// scripted callbacks on the same fake clock, produce identical outcomes, attempt counts and events, line for line.
/// </summary>
/// <remarks>
/// Each runtime twin is built with the SAME hook methods as the sample, so any difference is the generator's: the
/// inlined retry, timeout and fallback against their interpreter strategies, and the breaker and limiters driven
/// through their own hooks in a different control flow.
/// </remarks>
[Collection(nameof(GeneratedPipelines))]
public class ParityTests
{
    private static async Task<List<string>> RunAsync(IEnumerable<Script> scripts, Func<Script, ValueTask<Outcome<int>>> execute, FakeTimeProvider time, Action? between = null)
    {
        EventJournal.Shared.Take();
        var lines = new List<string>();
        foreach (var script in scripts)
        {
            var outcome = await FakeClock.DriveAsync(time, execute(script));
            lines.Add(Transcript.Of(outcome, script.Attempts));
            lines.AddRange(EventJournal.Shared.Take());
            between?.Invoke();
        }

        return lines;
    }

    private static Script[] RetryTimeoutScripts() =>
    [
        new(Ok(1)),
        new(Fail, Ok(2)),
        new(Ok(-1)),
        new(Hang, Ok(3)),
        new(Fail),
        new(Unhandled),
        new(Hang),
        new(Fail, Hang, Ok(4)),
    ];

    [Fact]
    public async Task RetryAndTimeouts_MatchTheRuntimePipeline()
    {
        var generatedTime = FakeClock.Create();
        RetryTimeoutPipeline.UseTimeProvider(generatedTime);
        var generated = await RunAsync(RetryTimeoutScripts(), s => RetryTimeoutPipeline.TryExecuteAsync(Callback, s), generatedTime);

        var runtimeTime = FakeClock.Create();
        var runtime = new ResiliencePipelineBuilder<int> { TimeProvider = runtimeTime }
            .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Linear,
                Delay = TimeSpan.FromMilliseconds(100),
                ShouldHandle = args => RetryTimeoutPipeline.Handle(args.Outcome),
                OnRetry = RetryTimeoutPipeline.Retried,
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(1),
                OnTimeout = args =>
                {
                    RetryTimeoutPipeline.AttemptTimedOut(args);
                    return default;
                },
            })
            .Build();
        var expected = await RunAsync(RetryTimeoutScripts(), s => runtime.TryExecuteAsync(Callback, s), runtimeTime);

        generated.Should().Equal(expected);
        generated.Should().Contain("timeout 1000ms", "the scripts must exercise the attempt timeout");
        generated.Should().Contain("result:3 attempts=2");
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhatTheRuntimePipelineThrows()
    {
        var generatedTime = FakeClock.Create();
        RetryTimeoutPipeline.UseTimeProvider(generatedTime);
        var runtimeTime = FakeClock.Create();
        var runtime = new ResiliencePipelineBuilder<int> { TimeProvider = runtimeTime }
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Linear,
                Delay = TimeSpan.FromMilliseconds(100),
                ShouldHandle = args => RetryTimeoutPipeline.Handle(args.Outcome),
            })
            .AddTimeout(TimeSpan.FromSeconds(1))
            .Build();

        var generated = await Capture(() => FakeClock.DriveAsync(generatedTime, RetryTimeoutPipeline.ExecuteAsync(Callback, new Script(Hang))));
        var expected = await Capture(() => FakeClock.DriveAsync(runtimeTime, runtime.ExecuteAsync(Callback, new Script(Hang))));

        generated.Should().Be(expected);
        generated.Should().Be(nameof(TimeoutRejectedException));

        static async Task<string> Capture(Func<Task<int>> run)
        {
            try
            {
                return "result:" + await run();
            }
            catch (Exception e)
            {
                return e.GetType().Name;
            }
        }
    }

    private static ResiliencePipeline<int> BreakerTwin(TimeProvider time) => new ResiliencePipelineBuilder<int> { TimeProvider = time }
        .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 2, Delay = TimeSpan.Zero, ShouldHandle = BreakerPipeline.RetryOn })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
        {
            FailureRatio = 0.5,
            MinimumThroughput = 2,
            SamplingDuration = TimeSpan.FromSeconds(10),
            BreakDuration = TimeSpan.FromSeconds(5),
            ShouldHandle = BreakerPipeline.BreakOn,
            OnOpened = BreakerPipeline.Opened,
            OnClosed = BreakerPipeline.Closed,
            OnHalfOpened = BreakerPipeline.HalfOpened,
        })
        .Build();

    private static Script[] BreakerScripts() =>
    [
        new(Fail),          // retried twice, the breaker opens on the way and rejects the last attempt
        new(Ok(1)),         // still open: rejected
        new(Ok(2)),         // after the break (the test advances 5 s): the half-open probe succeeds and closes
        new(Fail, Ok(3)),
        new(Unhandled),
        new(Ok(4)),
    ];

    [Fact]
    public async Task CircuitBreaker_MatchesTheRuntimePipeline_TransitionForTransition()
    {
        var generatedTime = FakeClock.Create();
        BreakerPipeline.UseTimeProvider(generatedTime);
        var step = 0;
        var generated = await RunAsync(BreakerScripts(), s => BreakerPipeline.TryExecuteAsync(Callback, s), generatedTime,
            () => { if (++step == 2) generatedTime.Advance(TimeSpan.FromSeconds(5)); });

        var runtimeTime = FakeClock.Create();
        var runtime = BreakerTwin(runtimeTime);
        step = 0;
        var expected = await RunAsync(BreakerScripts(), s => runtime.TryExecuteAsync(Callback, s), runtimeTime,
            () => { if (++step == 2) runtimeTime.Advance(TimeSpan.FromSeconds(5)); });

        generated.Should().Equal(expected);
        generated.Should().Contain("opened break=5000ms");
        generated.Should().Contain("half-opened");
        generated.Should().Contain("closed");
        generated.Should().Contain("rejected:CircuitOpen attempts=0", "the second execution meets an open circuit");
    }

    [Fact]
    public void CircuitBreaker_SyncExecute_MatchesTheRuntimePipeline()
    {
        var generatedTime = FakeClock.Create();
        BreakerPipeline.UseTimeProvider(generatedTime);
        var generated = RunSync(s => BreakerPipeline.Execute(static (script, ct) => script.InvokeAsync(ct).AsTask().GetAwaiter().GetResult(), s), generatedTime);

        var runtimeTime = FakeClock.Create();
        var runtime = BreakerTwin(runtimeTime);
        var expected = RunSync(s => runtime.Execute(static (script, ct) => script.InvokeAsync(ct).AsTask().GetAwaiter().GetResult(), s), runtimeTime);

        generated.Should().Equal(expected);

        static List<string> RunSync(Func<Script, int> execute, FakeTimeProvider time)
        {
            EventJournal.Shared.Take();
            var lines = new List<string>();
            var step = 0;
            foreach (var script in BreakerScripts())
            {
                try
                {
                    lines.Add($"result:{execute(script)} attempts={script.Attempts}");
                }
                catch (Exception e)
                {
                    lines.Add($"{e.GetType().Name} attempts={script.Attempts}");
                }

                lines.AddRange(EventJournal.Shared.Take());
                if (++step == 2)
                {
                    time.Advance(TimeSpan.FromSeconds(5));
                }
            }

            return lines;
        }
    }

    [Fact]
    public async Task Fallback_MatchesTheRuntimePipeline()
    {
        Script[] Scripts() => [new(Fail, Fail), new(Ok(-1)), new(Ok(5)), new(Unhandled), new(Fail, Ok(6))];

        var generatedTime = FakeClock.Create();
        FallbackPipeline.UseTimeProvider(generatedTime);
        var generated = await RunAsync(Scripts(), s => FallbackPipeline.TryExecuteAsync(Callback, s), generatedTime);

        var runtimeTime = FakeClock.Create();
        var runtime = new ResiliencePipelineBuilder<int> { TimeProvider = runtimeTime }
            .AddFallback(new FallbackStrategyOptions<int>
            {
                ShouldHandle = args => FallbackPipeline.FallbackOn(args.Outcome),
                FallbackAction = _ => Outcome.FromResultAsValueTask(FallbackPipeline.Substitute()),
                OnFallback = FallbackPipeline.Fell,
            })
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 1, Delay = TimeSpan.Zero })
            .Build();
        var expected = await RunAsync(Scripts(), s => runtime.TryExecuteAsync(Callback, s), runtimeTime);

        generated.Should().Equal(expected);
        generated.Should().Contain("result:0 attempts=2");
    }

    [Fact]
    public async Task DIForm_InstanceHooks_MatchTheRuntimePipeline()
    {
        Script[] Scripts() => [new(Fail, Fail, Fail), new(Ok(1)), new(Unhandled)];

        var generatedTime = FakeClock.Create();
        var pipeline = new InstanceBreakerPipeline(new EventJournal());
        pipeline.UseTimeProvider(generatedTime);
        var generated = await Run(pipeline.Journal, s => pipeline.TryExecuteAsync(Callback, s), generatedTime);

        var runtimeTime = FakeClock.Create();
        var twin = new InstanceBreakerPipeline(new EventJournal());
        var runtime = new ResiliencePipelineBuilder<int> { TimeProvider = runtimeTime }
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => twin.Handle(args.Outcome),
                OnRetry = twin.Retried,
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
            {
                FailureRatio = 0.5,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(5),
                ShouldHandle = args => twin.BreakOn(args.Outcome),
                OnOpened = args =>
                {
                    twin.Opened(args);
                    return default;
                },
            })
            .Build();
        var expected = await Run(twin.Journal, s => runtime.TryExecuteAsync(Callback, s), runtimeTime);

        generated.Should().Equal(expected);
        generated.Should().Contain("opened");

        async Task<List<string>> Run(EventJournal journal, Func<Script, ValueTask<Outcome<int>>> execute, FakeTimeProvider time)
        {
            var lines = new List<string>();
            foreach (var script in Scripts())
            {
                var outcome = await FakeClock.DriveAsync(time, execute(script));
                lines.Add(Transcript.Of(outcome, script.Attempts));
                lines.AddRange(journal.Take());
            }

            return lines;
        }
    }

    [Fact]
    public async Task DIForm_EachInstanceHasItsOwnBreaker()
    {
        var first = new InstanceBreakerPipeline(new EventJournal());
        var second = new InstanceBreakerPipeline(new EventJournal());
        first.UseTimeProvider(FakeClock.Create());
        second.UseTimeProvider(FakeClock.Create());

        (await first.TryExecuteAsync(Callback, new Script(Fail))).Rejection.Should().Be(RejectionKind.CircuitOpen);

        (await second.TryExecuteAsync(Callback, new Script(Ok(1)))).Result.Should().Be(1);
    }

    [Fact]
    public async Task GenericPipeline_MatchesTheNonGenericRuntimePipeline()
    {
        Script[] Scripts() => [new(Ok(1)), new(Fail, Ok(2)), new(Fail), new(Ok(3))];

        var generatedTime = FakeClock.Create();
        GenericPipeline.UseTimeProvider(generatedTime);
        var generated = await RunAsync(Scripts(), s => GenericPipeline.TryExecuteAsync(Callback, s), generatedTime);

        var runtimeTime = FakeClock.Create();
        var runtime = new ResiliencePipelineBuilder { TimeProvider = runtimeTime }
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                OnRetry = args =>
                {
                    GenericPipeline.Retried(args);
                    return default;
                },
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(5),
                ShouldHandle = args => GenericPipeline.Breaks(args.Outcome),
            })
            .AddTimeout(TimeSpan.FromSeconds(1))
            .Build();
        var expected = await RunAsync(Scripts(), s => runtime.TryExecuteAsync(Callback, s), runtimeTime);

        generated.Should().Equal(expected);
    }

    [Fact]
    public async Task Reloadable_MatchesTheRuntimePipeline_BeforeAndAfterAReload()
    {
        Script[] Scripts() => [new(Fail, Ok(1)), new(Fail, Fail, Fail, Fail), new(Ok(2))];

        var generatedTime = FakeClock.Create();
        ReloadablePipeline.UseTimeProvider(generatedTime);
        ReloadablePipeline.TryApply(new ReloadablePipelineOptions(), out _).Should().BeTrue();
        var before = await RunAsync(Scripts(), s => ReloadablePipeline.TryExecuteAsync(Callback, s), generatedTime);
        var twinTime = FakeClock.Create();
        var twin = Twin(maxRetries: 3, twinTime);
        var expectedBefore = await RunAsync(Scripts(), s => twin.TryExecuteAsync(Callback, s), twinTime);

        var options = new ReloadablePipelineOptions();
        options.Retry.MaxRetryAttempts = 1;
        options.Breaker.MinimumThroughput = 100;
        ReloadablePipeline.TryApply(options, out var error).Should().BeTrue(error);
        var after = await RunAsync(Scripts(), s => ReloadablePipeline.TryExecuteAsync(Callback, s), generatedTime);
        // The breaker section changed, so the generated breaker was recreated: the twin after the reload is a new pipeline too.
        var twinAfterTime = FakeClock.Create();
        var twinAfter = Twin(maxRetries: 1, twinAfterTime, minimumThroughput: 100);
        var expectedAfter = await RunAsync(Scripts(), s => twinAfter.TryExecuteAsync(Callback, s), twinAfterTime);

        before.Should().Equal(expectedBefore);
        after.Should().Equal(expectedAfter);
        after.Should().Contain("exception:InvalidOperationException attempts=2", "after the reload one retry is left");

        static ResiliencePipeline<int> Twin(int maxRetries, TimeProvider time, int minimumThroughput = 2) => new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = maxRetries, Delay = TimeSpan.Zero })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<int> { FailureRatio = 0.5, MinimumThroughput = minimumThroughput, BreakDuration = TimeSpan.FromSeconds(5) })
            .AddTimeout(TimeSpan.FromSeconds(2))
            .Build();
    }

    [Fact]
    public async Task Hedging_ForwardsToTheRuntimeHedging()
    {
        var time = FakeClock.Create();
        HedgingPipeline.UseTimeProvider(time);
        EventJournal.Shared.Take();

        // The primary fails, the hedged attempt (after 100 ms) succeeds.
        var outcome = await FakeClock.DriveAsync(time, HedgingPipeline.TryExecuteAsync(Callback, new Script(Ok(-1), Ok(7))));

        outcome.Result.Should().Be(7);
        EventJournal.Shared.Take().Should().NotBeEmpty("the hedged attempt raises OnHedging");
    }
}
