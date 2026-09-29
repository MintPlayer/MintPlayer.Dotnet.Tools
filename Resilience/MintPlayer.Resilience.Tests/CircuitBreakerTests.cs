using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

/// <summary>The circuit breaker through the public pipeline API.</summary>
public class CircuitBreakerTests
{
    private static readonly Func<CancellationToken, ValueTask<int>> Ok = static _ => new ValueTask<int>(1);
    private static readonly Func<CancellationToken, ValueTask<int>> Throw = static _ => ValueTask.FromException<int>(new InvalidOperationException("boom"));

    // Ratio 0.5, minimum throughput 2, sampling 1 s, break 1 s: two failures open the circuit.
    private static CircuitBreakerStrategyOptions<int> Options() => new()
    {
        FailureRatio = 0.5,
        MinimumThroughput = 2,
        SamplingDuration = TimeSpan.FromSeconds(1),
        BreakDuration = TimeSpan.FromSeconds(1),
    };

    private static ResiliencePipeline<int> Pipeline(FakeTimeProvider time, CircuitBreakerStrategyOptions<int> options)
        => new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddCircuitBreaker(options).Build();

    private static async Task Break(ResiliencePipeline<int> pipeline)
    {
        for (var i = 0; i < 2; i++)
        {
            (await pipeline.TryExecuteAsync(Throw)).RawException.Should().BeOfType<InvalidOperationException>();
        }
    }

    // ------------------------------------------------------------------ opening and rejections

    [Fact]
    public async Task Failures_OpenTheCircuit_AndTryExecuteAsync_ReturnsACircuitOpenRejection()
    {
        var time = new FakeTimeProvider();
        var state = new CircuitBreakerStateProvider();
        var options = Options();
        options.StateProvider = state;
        var pipeline = Pipeline(time, options);

        (await pipeline.TryExecuteAsync(Throw)).IsRejected.Should().BeFalse();
        state.CircuitState.Should().Be(CircuitState.Closed); // below the minimum throughput
        (await pipeline.TryExecuteAsync(Throw)).IsRejected.Should().BeFalse();
        state.CircuitState.Should().Be(CircuitState.Open);

        time.Advance(TimeSpan.FromMilliseconds(400));
        var ran = false;
        var outcome = await pipeline.TryExecuteAsync(_ =>
        {
            ran = true;
            return new ValueTask<int>(1);
        });

        ran.Should().BeFalse();
        outcome.IsRejected.Should().BeTrue();
        outcome.Rejection.Should().Be(RejectionKind.CircuitOpen);
        outcome.RetryAfter.Should().Be(TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public async Task ExecuteAsync_WhileOpen_ThrowsAFreshBrokenCircuitException_WithRetryAfterAndTheBreakingException()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline(time, Options());
        await Break(pipeline);

        Func<Task> act = () => pipeline.ExecuteAsync(Ok).AsTask();
        var first = (await act.Should().ThrowAsync<BrokenCircuitException>()).Which;
        var second = (await act.Should().ThrowAsync<BrokenCircuitException>()).Which;

        first.Should().NotBeSameAs(second);
        first.Kind.Should().Be(RejectionKind.CircuitOpen);
        first.RetryAfter.Should().Be(TimeSpan.FromSeconds(1));
        first.InnerException.Should().BeOfType<InvalidOperationException>();
        first.InnerException!.Message.Should().Be("boom");
    }

    [Fact]
    public async Task HandledResults_OpenTheCircuit_AndTheRejectionHasNoInnerException()
    {
        var time = new FakeTimeProvider();
        var options = Options();
        options.ShouldHandle = new PredicateBuilder<int>().HandleResult(-1);
        var pipeline = Pipeline(time, options);

        for (var i = 0; i < 2; i++)
        {
            (await pipeline.ExecuteAsync(static _ => new ValueTask<int>(-1))).Should().Be(-1);
        }

        var outcome = await pipeline.TryExecuteAsync(Ok);
        outcome.Rejection.Should().Be(RejectionKind.CircuitOpen);
        outcome.Exception!.InnerException.Should().BeNull();
    }

    [Fact]
    public void Execute_WhileOpen_ThrowsBrokenCircuitException()
    {
        var time = new FakeTimeProvider();
        var pipeline = Pipeline(time, Options());
        for (var i = 0; i < 2; i++)
        {
            Action fail = () => pipeline.Execute(static () => throw new InvalidOperationException());
            fail.Should().Throw<InvalidOperationException>();
        }

        Action act = () => pipeline.Execute(static () => 1);
        act.Should().Throw<BrokenCircuitException>();
    }

    [Fact]
    public async Task DefaultShouldHandle_IgnoresOperationCanceledException()
    {
        var pipeline = Pipeline(new FakeTimeProvider(), Options());
        for (var i = 0; i < 5; i++)
        {
            await pipeline.TryExecuteAsync(static _ => ValueTask.FromException<int>(new OperationCanceledException()));
        }

        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task SuccessesKeepTheRatioBelowTheThreshold()
    {
        var options = Options();
        options.MinimumThroughput = 4;
        options.FailureRatio = 0.6;
        var pipeline = Pipeline(new FakeTimeProvider(), options);

        await pipeline.TryExecuteAsync(Ok);
        await pipeline.TryExecuteAsync(Ok);
        await pipeline.TryExecuteAsync(Throw);
        await pipeline.TryExecuteAsync(Throw); // 2F / 4 = 0.5 < 0.6

        (await pipeline.TryExecuteAsync(Ok)).IsRejected.Should().BeFalse();
    }

    [Fact]
    public async Task AnOuterRetry_HandlesTheRejection_ByExceptionType_WithoutCreatingIt()
    {
        var time = new FakeTimeProvider();
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder<int>().Handle<BrokenCircuitException>(),
                OnRetry = args =>
                {
                    attempts++;
                    args.Outcome.Rejection.Should().Be(RejectionKind.CircuitOpen);
                    return default;
                },
            })
            .AddCircuitBreaker(Options())
            .Build();

        await pipeline.TryExecuteAsync(Throw);
        await pipeline.TryExecuteAsync(Throw);
        var outcome = await pipeline.TryExecuteAsync(Ok);

        attempts.Should().Be(2);
        outcome.Rejection.Should().Be(RejectionKind.CircuitOpen);
    }

    // ------------------------------------------------------------------ half-open

    [Fact]
    public async Task AfterTheBreak_OneProbeRuns_OthersAreRejected_AndItsSuccessCloses()
    {
        var time = new FakeTimeProvider();
        var state = new CircuitBreakerStateProvider();
        var options = Options();
        options.StateProvider = state;
        var pipeline = Pipeline(time, options);
        await Break(pipeline);
        time.Advance(TimeSpan.FromSeconds(1));

        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = pipeline.TryExecuteAsync(_ => new ValueTask<int>(gate.Task)).AsTask();
        state.CircuitState.Should().Be(CircuitState.HalfOpen);

        var other = await pipeline.TryExecuteAsync(Ok);
        other.Rejection.Should().Be(RejectionKind.CircuitOpen);
        other.RetryAfter.Should().Be(TimeSpan.FromSeconds(1)); // the half-open window

        gate.SetResult(7);
        (await probe).Result.Should().Be(7);
        state.CircuitState.Should().Be(CircuitState.Closed);
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task AFailedProbe_ReopensTheCircuit()
    {
        var time = new FakeTimeProvider();
        var state = new CircuitBreakerStateProvider();
        var options = Options();
        options.StateProvider = state;
        var pipeline = Pipeline(time, options);
        await Break(pipeline);
        time.Advance(TimeSpan.FromSeconds(1));

        (await pipeline.TryExecuteAsync(Throw)).RawException.Should().BeOfType<InvalidOperationException>();
        state.CircuitState.Should().Be(CircuitState.Open);
        (await pipeline.TryExecuteAsync(Ok)).RetryAfter.Should().Be(TimeSpan.FromSeconds(1));
    }

    // ------------------------------------------------------------------ manual control

    [Fact]
    public async Task ManualControl_Isolate_RejectsWithIsolated_AndCloseRestores()
    {
        var time = new FakeTimeProvider();
        var control = new CircuitBreakerManualControl();
        var state = new CircuitBreakerStateProvider();
        var options = Options();
        options.ManualControl = control;
        options.StateProvider = state;
        var pipeline = Pipeline(time, options);

        await control.IsolateAsync();
        state.CircuitState.Should().Be(CircuitState.Isolated);

        time.Advance(TimeSpan.FromDays(10));
        var outcome = await pipeline.TryExecuteAsync(Ok);
        outcome.Rejection.Should().Be(RejectionKind.CircuitIsolated);
        outcome.RetryAfter.Should().NotHaveValue();

        Func<Task> act = () => pipeline.ExecuteAsync(Ok).AsTask();
        var thrown = (await act.Should().ThrowAsync<IsolatedCircuitException>()).Which;
        thrown.Kind.Should().Be(RejectionKind.CircuitIsolated);

        await control.CloseAsync();
        state.CircuitState.Should().Be(CircuitState.Closed);
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task ManualControl_Close_ResetsTheHealthWindow()
    {
        var control = new CircuitBreakerManualControl();
        var state = new CircuitBreakerStateProvider();
        var options = Options();
        options.ManualControl = control;
        options.StateProvider = state;
        options.MinimumThroughput = 3;
        var pipeline = Pipeline(new FakeTimeProvider(), options);

        await pipeline.TryExecuteAsync(Throw);
        await pipeline.TryExecuteAsync(Throw);
        await control.CloseAsync();
        await pipeline.TryExecuteAsync(Throw);

        state.CircuitState.Should().Be(CircuitState.Closed); // 1F since the close, not 3F
    }

    [Fact]
    public async Task ManualControl_Close_AfterABreak_ClosesTheCircuit()
    {
        var control = new CircuitBreakerManualControl();
        var options = Options();
        options.ManualControl = control;
        var pipeline = Pipeline(new FakeTimeProvider(), options);
        await Break(pipeline);

        await control.CloseAsync();
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task ManualControl_CreatedIsolated_IsolatesTheBreakerWhenBuilt()
    {
        var control = new CircuitBreakerManualControl(isIsolated: true);
        var options = Options();
        options.ManualControl = control;
        var pipeline = Pipeline(new FakeTimeProvider(), options);

        (await pipeline.TryExecuteAsync(Ok)).Rejection.Should().Be(RejectionKind.CircuitIsolated);
        await control.CloseAsync();
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
    }

    [Fact]
    public async Task ManualControl_DrivesEveryAttachedBreaker()
    {
        var control = new CircuitBreakerManualControl();
        var a = Options();
        a.ManualControl = control;
        var b = Options();
        b.ManualControl = control;
        var first = Pipeline(new FakeTimeProvider(), a);
        var second = Pipeline(new FakeTimeProvider(), b);

        await control.IsolateAsync();
        (await first.TryExecuteAsync(Ok)).Rejection.Should().Be(RejectionKind.CircuitIsolated);
        (await second.TryExecuteAsync(Ok)).Rejection.Should().Be(RejectionKind.CircuitIsolated);

        await control.CloseAsync();
        (await first.TryExecuteAsync(Ok)).IsSuccess.Should().BeTrue();
        (await second.TryExecuteAsync(Ok)).IsSuccess.Should().BeTrue();
    }

    // ------------------------------------------------------------------ state provider

    [Fact]
    public void StateProvider_BeforeBuild_ReportsClosed()
        => new CircuitBreakerStateProvider().CircuitState.Should().Be(CircuitState.Closed);

    [Fact]
    public void StateProvider_CannotServeTwoBreakers()
    {
        var state = new CircuitBreakerStateProvider();
        var a = Options();
        a.StateProvider = state;
        var b = Options();
        b.StateProvider = state;
        Pipeline(new FakeTimeProvider(), a);

        Action act = () => Pipeline(new FakeTimeProvider(), b);
        act.Should().Throw<InvalidOperationException>();
    }

    // ------------------------------------------------------------------ events

    [Fact]
    public async Task Events_AreRaisedInTransitionOrder_WithPollysArguments()
    {
        var time = new FakeTimeProvider();
        var control = new CircuitBreakerManualControl();
        var log = new List<string>();
        var options = Options();
        options.ManualControl = control;
        options.OnOpened = args =>
        {
            log.Add($"opened manual={args.IsManual} break={args.BreakDuration} outcome={(args.IsManual ? args.Outcome.Result.ToString() : args.Outcome.Exception?.Message)}");
            return default;
        };
        options.OnHalfOpened = args =>
        {
            (args.Context is not null).Should().BeTrue();
            log.Add("half-opened");
            return default;
        };
        options.OnClosed = args =>
        {
            log.Add($"closed manual={args.IsManual} outcome={args.Outcome.Result}");
            return default;
        };
        var pipeline = Pipeline(time, options);

        await Break(pipeline);
        time.Advance(TimeSpan.FromSeconds(1));
        await pipeline.TryExecuteAsync(Throw); // failed probe
        time.Advance(TimeSpan.FromSeconds(1));
        await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(5)); // successful probe
        await control.IsolateAsync();
        await control.CloseAsync();
        await control.CloseAsync(); // already closed: no event

        log.Should().Equal(
        [
            "opened manual=False break=00:00:01 outcome=boom",
            "half-opened",
            "opened manual=False break=00:00:01 outcome=boom",
            "half-opened",
            "closed manual=False outcome=5",
            $"opened manual=True break={TimeSpan.MaxValue} outcome=0",
            "closed manual=True outcome=0",
        ]);
    }

    [Fact]
    public async Task Events_OfConcurrentTransitions_RunOneAtATime_InOrder()
    {
        var time = new FakeTimeProvider();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new List<string>();
        var options = Options();
        options.OnOpened = async _ =>
        {
            lock (log)
            {
                log.Add("opened-start");
            }

            await gate.Task;
            lock (log)
            {
                log.Add("opened-end");
            }
        };
        options.OnHalfOpened = _ =>
        {
            lock (log)
            {
                log.Add("half-opened");
            }

            return default;
        };
        options.OnClosed = _ =>
        {
            lock (log)
            {
                log.Add("closed");
            }

            return default;
        };
        var pipeline = Pipeline(time, options);

        await pipeline.TryExecuteAsync(Throw);
        var breaking = pipeline.TryExecuteAsync(Throw).AsTask(); // opens, then waits in OnOpened
        breaking.IsCompleted.Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(1));
        var probeRan = false;
        var probe = pipeline.TryExecuteAsync(_ =>
        {
            probeRan = true;
            return new ValueTask<int>(1);
        }).AsTask();

        await Task.Delay(50);
        probeRan.Should().BeFalse(); // OnHalfOpened waits for OnOpened, and the probe for OnHalfOpened
        lock (log)
        {
            log.Should().Equal(["opened-start"]);
        }

        gate.SetResult();
        await breaking;
        (await probe).Result.Should().Be(1);

        log.Should().Equal(["opened-start", "opened-end", "half-opened", "closed"]);
    }

    [Fact]
    public async Task OnOpened_ThatThrows_FailsTheCallThatOpened_ButTheCircuitIsOpen()
    {
        var time = new FakeTimeProvider();
        var state = new CircuitBreakerStateProvider();
        var options = Options();
        options.StateProvider = state;
        options.OnOpened = _ => throw new FormatException("event");
        var pipeline = Pipeline(time, options);

        await pipeline.TryExecuteAsync(Throw);
        (await pipeline.TryExecuteAsync(Throw)).RawException.Should().BeOfType<FormatException>();
        state.CircuitState.Should().Be(CircuitState.Open);

        // The sequencer was released: the next transition's event is not blocked.
        time.Advance(TimeSpan.FromSeconds(1));
        (await pipeline.TryExecuteAsync(Ok)).Result.Should().Be(1);
        state.CircuitState.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task OnHalfOpened_ThatThrows_FailsTheProbe_AndReopensTheCircuit()
    {
        var time = new FakeTimeProvider();
        var state = new CircuitBreakerStateProvider();
        var ran = false;
        var options = Options();
        options.StateProvider = state;
        options.OnHalfOpened = _ => throw new FormatException("event");
        var pipeline = Pipeline(time, options);
        await Break(pipeline);
        time.Advance(TimeSpan.FromSeconds(1));

        var outcome = await pipeline.TryExecuteAsync(_ =>
        {
            ran = true;
            return new ValueTask<int>(1);
        });

        ran.Should().BeFalse();
        outcome.Exception.Should().BeOfType<FormatException>();
        state.CircuitState.Should().Be(CircuitState.Open); // not wedged half-open (a deviation from Polly)
    }

    [Fact]
    public async Task BreakDurationGenerator_SetsTheBreak_AndIsReportedToOnOpened()
    {
        var time = new FakeTimeProvider();
        TimeSpan? reported = null;
        BreakDurationGeneratorArguments? seen = null;
        var options = Options();
        options.BreakDurationGenerator = args =>
        {
            seen = args;
            return TimeSpan.FromSeconds(10);
        };
        options.OnOpened = args =>
        {
            reported = args.BreakDuration;
            return default;
        };
        var pipeline = Pipeline(time, options);

        await Break(pipeline);
        reported.Should().Be(TimeSpan.FromSeconds(10));
        seen!.Value.FailureRate.Should().Be(1.0);
        seen.Value.FailureCount.Should().Be(2);
        seen.Value.HalfOpenAttempts.Should().Be(0);

        time.Advance(TimeSpan.FromSeconds(9));
        (await pipeline.TryExecuteAsync(Ok)).RetryAfter.Should().Be(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));
        (await pipeline.TryExecuteAsync(Ok)).IsSuccess.Should().BeTrue();
    }

    // ------------------------------------------------------------------ slow calls (beyond Polly)

    [Fact]
    public async Task SlowCalls_OpenTheCircuit_EvenWhenTheySucceed()
    {
        var time = new FakeTimeProvider();
        var state = new CircuitBreakerStateProvider();
        var options = Options();
        options.StateProvider = state;
        options.SlowCallDurationThreshold = TimeSpan.FromMilliseconds(100);
        options.SlowCallRatio = 0.5;
        var pipeline = Pipeline(time, options);

        Func<CancellationToken, ValueTask<int>> slow = _ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(150));
            return new ValueTask<int>(1);
        };

        (await pipeline.TryExecuteAsync(Ok)).IsSuccess.Should().BeTrue();
        (await pipeline.TryExecuteAsync(slow)).IsSuccess.Should().BeTrue();
        state.CircuitState.Should().Be(CircuitState.Open); // 1 slow / 2 >= 0.5

        time.Advance(TimeSpan.FromSeconds(1));
        (await pipeline.TryExecuteAsync(slow)).IsSuccess.Should().BeTrue(); // the probe runs, but slowly
        state.CircuitState.Should().Be(CircuitState.Open);

        time.Advance(TimeSpan.FromSeconds(1));
        (await pipeline.TryExecuteAsync(Ok)).IsSuccess.Should().BeTrue();
        state.CircuitState.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task SlowCalls_TheDurationExcludesTheHalfOpenEvent()
    {
        var time = new FakeTimeProvider();
        var state = new CircuitBreakerStateProvider();
        var options = Options();
        options.StateProvider = state;
        options.SlowCallDurationThreshold = TimeSpan.FromMilliseconds(100);
        options.OnHalfOpened = _ =>
        {
            time.Advance(TimeSpan.FromSeconds(5)); // a slow event must not make the probe slow
            return default;
        };
        var pipeline = Pipeline(time, options);
        await Break(pipeline);
        time.Advance(TimeSpan.FromSeconds(1));

        (await pipeline.TryExecuteAsync(Ok)).IsSuccess.Should().BeTrue();
        state.CircuitState.Should().Be(CircuitState.Closed);
    }

    // ------------------------------------------------------------------ non-generic pipeline

    [Fact]
    public async Task NonGenericPipeline_SharesOneCircuitAcrossResultTypes()
    {
        var time = new FakeTimeProvider();
        var opened = new List<object?>();
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = time }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 2,
                ShouldHandle = new PredicateBuilder().HandleResult(static r => r is -1),
                OnOpened = args =>
                {
                    opened.Add(args.Outcome.Result);
                    return default;
                },
            })
            .Build();

        (await pipeline.ExecuteAsync(static _ => new ValueTask<int>(-1))).Should().Be(-1);
        (await pipeline.ExecuteAsync(static _ => new ValueTask<int>(-1))).Should().Be(-1);

        opened.Should().Equal([-1]);
        var outcome = await pipeline.TryExecuteAsync(static _ => new ValueTask<string>("x"));
        outcome.Rejection.Should().Be(RejectionKind.CircuitOpen);
    }

    [Fact]
    public async Task NonGenericPipeline_AttachesTheStateProviderOnce()
    {
        var state = new CircuitBreakerStateProvider();
        var control = new CircuitBreakerManualControl();
        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions { StateProvider = state, ManualControl = control })
            .Build();

        pipeline.Execute(static () => 1).Should().Be(1);
        pipeline.Execute(static () => "a").Should().Be("a");
        await control.IsolateAsync();
        state.CircuitState.Should().Be(CircuitState.Isolated);
    }

    // ------------------------------------------------------------------ options

    [Fact]
    public void Options_HavePollysDefaults()
    {
        var options = new CircuitBreakerStrategyOptions<int>();
        options.Name.Should().Be("CircuitBreaker");
        options.FailureRatio.Should().Be(0.1);
        options.MinimumThroughput.Should().Be(100);
        options.SamplingDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.BreakDuration.Should().Be(TimeSpan.FromSeconds(5));
        options.SlowCallDurationThreshold.Should().NotHaveValue();
        options.SlowCallRatio.Should().Be(1.0);
    }

    public static TheoryData<string> InvalidOptions() =>
    [
        "FailureRatio=-0.1", "FailureRatio=1.1", "FailureRatio=NaN", "MinimumThroughput=1",
        "SamplingDuration=499ms", "SamplingDuration=2d", "BreakDuration=499ms", "BreakDuration=2d",
        "ShouldHandle=null", "SlowCallDurationThreshold=0", "SlowCallDurationThreshold=2d",
        "SlowCallRatio=0", "SlowCallRatio=1.1",
    ];

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void InvalidOptions_ThrowValidationException_WhenAdded(string change)
    {
        var options = new CircuitBreakerStrategyOptions<int>();
        switch (change)
        {
            case "FailureRatio=-0.1": options.FailureRatio = -0.1; break;
            case "FailureRatio=1.1": options.FailureRatio = 1.1; break;
            case "FailureRatio=NaN": options.FailureRatio = double.NaN; break;
            case "MinimumThroughput=1": options.MinimumThroughput = 1; break;
            case "SamplingDuration=499ms": options.SamplingDuration = TimeSpan.FromMilliseconds(499); break;
            case "SamplingDuration=2d": options.SamplingDuration = TimeSpan.FromDays(2); break;
            case "BreakDuration=499ms": options.BreakDuration = TimeSpan.FromMilliseconds(499); break;
            case "BreakDuration=2d": options.BreakDuration = TimeSpan.FromDays(2); break;
            case "ShouldHandle=null": options.ShouldHandle = null!; break;
            case "SlowCallDurationThreshold=0": options.SlowCallDurationThreshold = TimeSpan.Zero; break;
            case "SlowCallDurationThreshold=2d": options.SlowCallDurationThreshold = TimeSpan.FromDays(2); break;
            case "SlowCallRatio=0": options.SlowCallRatio = 0; break;
            case "SlowCallRatio=1.1": options.SlowCallRatio = 1.1; break;
        }

        Action act = () => new ResiliencePipelineBuilder<int>().AddCircuitBreaker(options);
        act.Should().Throw<ValidationException>();
    }

    [Theory]
    [InlineData(0.0, 2)]
    [InlineData(1.0, int.MaxValue)]
    public void BoundaryOptions_AreValid(double ratio, int throughput)
    {
        var options = new CircuitBreakerStrategyOptions<int>
        {
            FailureRatio = ratio,
            MinimumThroughput = throughput,
            SamplingDuration = TimeSpan.FromMilliseconds(500),
            BreakDuration = TimeSpan.FromDays(1),
            SlowCallDurationThreshold = TimeSpan.FromMilliseconds(1),
            SlowCallRatio = 1.0,
        };

        Action act = () => new ResiliencePipelineBuilder<int>().AddCircuitBreaker(options).Build();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Options_AreSnapshotAtBuild()
    {
        var options = Options();
        var pipeline = Pipeline(new FakeTimeProvider(), options);
        options.MinimumThroughput = 1000;

        await Break(pipeline);
        (await pipeline.TryExecuteAsync(Ok)).Rejection.Should().Be(RejectionKind.CircuitOpen);
    }
}
