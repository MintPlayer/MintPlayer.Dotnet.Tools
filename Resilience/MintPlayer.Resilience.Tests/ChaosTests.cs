using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Simmy;
using MintPlayer.Resilience.Simmy.Behavior;
using MintPlayer.Resilience.Simmy.Fault;
using MintPlayer.Resilience.Simmy.Latency;
using MintPlayer.Resilience.Simmy.Outcomes;

namespace MintPlayer.Resilience.Tests;

public class ChaosTests
{
    private sealed class Counter
    {
        public int Calls;

        public ValueTask<int> Invoke(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return new ValueTask<int>(1);
        }
    }

    private static ChaosFaultStrategyOptions Fault(double rate, double random) => new()
    {
        InjectionRate = rate,
        Randomizer = () => random,
        FaultGenerator = _ => new InvalidOperationException("injected"),
    };

    // ---- injection decision (shared by every chaos strategy) ----

    [Theory]
    [InlineData(1.0, 0.999, true)]
    [InlineData(0.5, 0.4, true)]
    [InlineData(0.5, 0.5, false)]
    [InlineData(0.5, 0.6, false)]
    [InlineData(0.0, 0.0, false)]
    public async Task InjectionRate_InjectsWhenTheRandomizerIsBelowIt(double rate, double random, bool injected)
    {
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(Fault(rate, random)).Build();

        var outcome = await pipeline.TryExecuteAsync(counter.Invoke);

        (outcome.Exception is InvalidOperationException { Message: "injected" }).Should().Be(injected);
        counter.Calls.Should().Be(injected ? 0 : 1);
    }

    [Fact]
    public async Task InjectionRate_WithASequenceRandomizer_InjectsExactlyTheExpectedCalls()
    {
        var randomizer = new SequenceRandomizer(0.1, 0.9, 0.29, 0.3, 0.05);
        var options = Fault(0.3, 0);
        options.Randomizer = randomizer.Next;
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(options).Build();

        var injected = new List<bool>();
        for (var i = 0; i < 5; i++)
        {
            injected.Add((await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1))).Exception is not null);
        }

        injected.Should().Equal(true, false, true, false, true);
    }

    [Fact]
    public async Task InjectionRateGenerator_OverridesTheRate_AndIsClamped()
    {
        var rates = new Queue<double>([2.0, -1.0]);
        var options = Fault(0, 0.999);
        options.InjectionRateGenerator = _ => rates.Dequeue();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(options).Build();

        (await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1))).Exception.Should().BeOfType<InvalidOperationException>();
        (await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1))).Result.Should().Be(1);
    }

    [Fact]
    public async Task Disabled_NeverInjects_AndDoesNotCallTheRandomizer()
    {
        var randomizerCalls = 0;
        var options = Fault(1, 0);
        options.Enabled = false;
        options.Randomizer = () =>
        {
            randomizerCalls++;
            return 0;
        };
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(options).Build();

        for (var i = 0; i < 10; i++)
        {
            (await pipeline.ExecuteAsync(counter.Invoke)).Should().Be(1);
        }

        counter.Calls.Should().Be(10);
        randomizerCalls.Should().Be(0);
    }

    [Fact]
    public async Task EnabledGenerator_DecidesPerExecution_AndOverridesEnabled()
    {
        var enabled = new Queue<bool>([true, false, true]);
        var options = Fault(1, 0);
        options.Enabled = false;
        options.EnabledGenerator = _ => enabled.Dequeue();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(options).Build();

        var injected = new List<bool>();
        for (var i = 0; i < 3; i++)
        {
            injected.Add((await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1))).Exception is not null);
        }

        injected.Should().Equal(true, false, true);
    }

    [Fact]
    public async Task Generators_CanReadTheCallersContext()
    {
        var key = new ResiliencePropertyKey<bool>("chaos");
        var options = Fault(0, 0.5);
        options.EnabledGenerator = args => args.Context.Properties.GetValue(key, false);
        options.InjectionRateGenerator = args => args.Context.OperationKey == "hot" ? 1 : 0;
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(options).Build();

        var context = ResilienceContextPool.Shared.Get("hot");
        context.Properties.Set(key, true);
        (await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1), context)).Exception.Should().BeOfType<InvalidOperationException>();
        ResilienceContextPool.Shared.Return(context);

        context = ResilienceContextPool.Shared.Get("cold");
        context.Properties.Set(key, true);
        (await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1), context)).Result.Should().Be(1);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task CancelledToken_EndsTheExecutionWithACancellation_WithoutCallingTheCallback()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(Fault(1, 0)).Build();

        var outcome = await pipeline.TryExecuteAsync(counter.Invoke, cts.Token);

        outcome.Exception.Should().BeAssignableTo<OperationCanceledException>();
        counter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void InjectionRate_OutOfRange_IsInvalid(double rate)
    {
        var act = () => new ResiliencePipelineBuilder<int>().AddChaosFault(Fault(rate, 0));

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void RequiredGenerators_AreValidated()
    {
        new Action(() => new ResiliencePipelineBuilder().AddChaosFault(new ChaosFaultStrategyOptions())).Should().Throw<ValidationException>();
        new Action(() => new ResiliencePipelineBuilder<int>().AddChaosOutcome(new ChaosOutcomeStrategyOptions<int>())).Should().Throw<ValidationException>();
        new Action(() => new ResiliencePipelineBuilder().AddChaosBehavior(new ChaosBehaviorStrategyOptions())).Should().Throw<ValidationException>();
        new Action(() => new ResiliencePipelineBuilder().AddChaosFault(new ChaosFaultStrategyOptions { FaultGenerator = _ => null, Randomizer = null! })).Should().Throw<ValidationException>();
    }

    [Fact]
    public void Defaults_MatchPolly()
    {
        var fault = new ChaosFaultStrategyOptions();
        fault.Name.Should().Be("Chaos.Fault");
        fault.InjectionRate.Should().Be(0.001);
        fault.Enabled.Should().BeTrue();
        new ChaosOutcomeStrategyOptions<int>().Name.Should().Be("Chaos.Outcome");
        new ChaosBehaviorStrategyOptions().Name.Should().Be("Chaos.Behavior");
        var latency = new ChaosLatencyStrategyOptions();
        latency.Name.Should().Be("Chaos.Latency");
        latency.Latency.Should().Be(TimeSpan.FromSeconds(30));
    }

    // ---- fault ----

    [Fact]
    public async Task Fault_NullFromTheGenerator_InjectsNothing()
    {
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 1,
            FaultGenerator = _ => null,
        }).Build();

        (await pipeline.ExecuteAsync(counter.Invoke)).Should().Be(1);
        counter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Fault_OnFaultInjected_IsRaisedWithTheFault()
    {
        Exception? seen = null;
        var options = Fault(1, 0);
        options.OnFaultInjected = args =>
        {
            seen = args.Fault;
            return default;
        };
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(options).Build();

        var outcome = await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1));

        seen.Should().NotBeNull();
        outcome.Exception.Should().BeSameAs(seen);
    }

    [Fact]
    public async Task Fault_ShorthandOnTheNonGenericBuilder_FailsVoidAndTypedCalls()
    {
        var pipeline = new ResiliencePipelineBuilder().AddChaosFault(1, () => new TimeoutException("chaos")).Build();

        Func<Task> voidCall = async () => await pipeline.ExecuteAsync(static _ => ValueTask.CompletedTask);
        Func<Task> typedCall = async () => await pipeline.ExecuteAsync(static _ => new ValueTask<string>("x"));

        (await voidCall.Should().ThrowAsync<TimeoutException>()).Which.Message.Should().Be("chaos");
        await typedCall.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Fault_IsRetriedLikeAnyOtherFailure()
    {
        var randomizer = new SequenceRandomizer(0, 0, 0.99);
        var options = Fault(0.5, 0);
        options.Randomizer = randomizer.Next;
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 3, Delay = TimeSpan.Zero })
            .AddChaosFault(options)
            .Build();

        (await pipeline.ExecuteAsync(counter.Invoke)).Should().Be(1);
        counter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(0, typeof(InvalidOperationException))]
    [InlineData(29, typeof(InvalidOperationException))]
    [InlineData(30, typeof(TimeoutException))]
    [InlineData(99, typeof(TimeoutException))]
    public void FaultGenerator_PicksByWeight(int generatedWeight, Type expected)
    {
        var totals = new List<int>();
        Func<FaultGeneratorArguments, Exception?> generator = new FaultGenerator(total =>
        {
            totals.Add(total);
            return generatedWeight;
        }).AddException<InvalidOperationException>(30).AddException<TimeoutException>(70);

        var fault = generator(new FaultGeneratorArguments(ResilienceContextPool.Shared.Get()));

        fault.Should().NotBeNull();
        fault!.GetType().Should().Be(expected);
        totals.Should().Equal(100);
    }

    [Fact]
    public void FaultGenerator_FactoryOverloads_AndTheEmptyGenerator()
    {
        var context = ResilienceContextPool.Shared.Get("key");
        Func<FaultGeneratorArguments, Exception?> plain = new FaultGenerator().AddException(() => new InvalidOperationException("plain"));
        Func<FaultGeneratorArguments, Exception?> withContext = new FaultGenerator().AddException(c => new InvalidOperationException(c.OperationKey));
        Func<FaultGeneratorArguments, Exception?> empty = new FaultGenerator();

        plain(new FaultGeneratorArguments(context))!.Message.Should().Be("plain");
        withContext(new FaultGeneratorArguments(context))!.Message.Should().Be("key");
        empty(new FaultGeneratorArguments(context)).Should().BeNull();
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task FaultGenerator_ConvertsImplicitlyToTheOption()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 1,
            FaultGenerator = new FaultGenerator().AddException<TimeoutException>(),
        }).Build();

        (await pipeline.TryExecuteAsync(static _ => new ValueTask<int>(1))).Exception.Should().BeOfType<TimeoutException>();
    }

    // ---- outcome ----

    [Fact]
    public async Task Outcome_Shorthand_InjectsTheResult_WithoutCallingTheCallback()
    {
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosOutcome(1, () => 42).Build();

        (await pipeline.ExecuteAsync(counter.Invoke)).Should().Be(42);
        counter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Outcome_NullFromTheGenerator_InjectsNothing_AndOnOutcomeInjectedSeesTheOutcome()
    {
        var generated = new Queue<Outcome<int>?>([null, Outcome.FromResult(7)]);
        var seen = new List<int>();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosOutcome(new ChaosOutcomeStrategyOptions<int>
        {
            InjectionRate = 1,
            OutcomeGenerator = _ => generated.Dequeue(),
            OnOutcomeInjected = args =>
            {
                seen.Add(args.Outcome.Result);
                return default;
            },
        }).Build();

        (await pipeline.ExecuteAsync(static _ => new ValueTask<int>(1))).Should().Be(1);
        (await pipeline.ExecuteAsync(static _ => new ValueTask<int>(1))).Should().Be(7);
        seen.Should().Equal(7);
    }

    [Theory]
    [InlineData(0, "result")]
    [InlineData(49, "result")]
    [InlineData(50, "exception")]
    [InlineData(99, "exception")]
    public void OutcomeGenerator_PicksByWeight(int generatedWeight, string expected)
    {
        Func<OutcomeGeneratorArguments, Outcome<string>?> generator = new OutcomeGenerator<string>(_ => generatedWeight)
            .AddResult(() => "result", 50)
            .AddException<InvalidOperationException>(50);

        var outcome = generator(new OutcomeGeneratorArguments(ResilienceContextPool.Shared.Get()));

        outcome.HasValue.Should().BeTrue();
        (outcome!.Value.IsSuccess ? outcome.Value.Result : "exception").Should().Be(expected);
    }

    [Fact]
    public void OutcomeGenerator_FactoryOverloads_AndTheEmptyGenerator()
    {
        var context = ResilienceContextPool.Shared.Get("key");
        var args = new OutcomeGeneratorArguments(context);
        Func<OutcomeGeneratorArguments, Outcome<string>?> result = new OutcomeGenerator<string>().AddResult(c => c.OperationKey!);
        Func<OutcomeGeneratorArguments, Outcome<string>?> exception = new OutcomeGenerator<string>().AddException(() => new TimeoutException());
        Func<OutcomeGeneratorArguments, Outcome<string>?> contextException = new OutcomeGenerator<string>().AddException(c => new InvalidOperationException(c.OperationKey));
        Func<OutcomeGeneratorArguments, Outcome<string>?> empty = new OutcomeGenerator<string>();

        result(args)!.Value.Result.Should().Be("key");
        exception(args)!.Value.Exception.Should().BeOfType<TimeoutException>();
        contextException(args)!.Value.Exception!.Message.Should().Be("key");
        empty(args).HasValue.Should().BeFalse();
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task OutcomeGenerator_InjectsAnException()
    {
        var pipeline = new ResiliencePipelineBuilder<string>().AddChaosOutcome(new ChaosOutcomeStrategyOptions<string>
        {
            InjectionRate = 1,
            OutcomeGenerator = new OutcomeGenerator<string>().AddException<TimeoutException>(),
        }).Build();

        Func<Task> act = async () => await pipeline.ExecuteAsync(static _ => new ValueTask<string>("x"));

        await act.Should().ThrowAsync<TimeoutException>();
    }

    // ---- latency ----

    [Fact]
    public async Task Latency_DelaysTheCallback_OnThePipelinesTimeProvider_AndRaisesTheEventAfterTheDelay()
    {
        var time = new DelayRecordingTimeProvider();
        var counter = new Counter();
        var events = new List<TimeSpan>();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddChaosLatency(new ChaosLatencyStrategyOptions
        {
            InjectionRate = 1,
            Latency = TimeSpan.FromSeconds(5),
            OnLatencyInjected = args =>
            {
                events.Add(args.Latency);
                return default;
            },
        }).Build();

        var execution = pipeline.ExecuteAsync(counter.Invoke).AsTask();
        (await time.NextDelayAsync()).Should().Be(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(4));
        counter.Calls.Should().Be(0);
        events.Should().BeEmpty();

        time.Advance(TimeSpan.FromSeconds(1));
        (await execution).Should().Be(1);
        counter.Calls.Should().Be(1);
        events.Should().Equal(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Latency_Shorthand_AndTheGeneratorOverridesTheLatency()
    {
        var time = new DelayRecordingTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddChaosLatency(new ChaosLatencyStrategyOptions
        {
            InjectionRate = 1,
            LatencyGenerator = _ => TimeSpan.FromMilliseconds(250),
        }).Build();

        var execution = pipeline.ExecuteAsync(static _ => new ValueTask<int>(3)).AsTask();
        (await time.AdvanceNextDelayAsync()).Should().Be(TimeSpan.FromMilliseconds(250));
        (await execution).Should().Be(3);

        var shorthand = new ResiliencePipelineBuilder { TimeProvider = time }.AddChaosLatency(1, TimeSpan.FromSeconds(1)).Build();
        var voidExecution = shorthand.ExecuteAsync(static _ => ValueTask.CompletedTask).AsTask();
        (await time.AdvanceNextDelayAsync()).Should().Be(TimeSpan.FromSeconds(1));
        await voidExecution;
    }

    [Fact]
    public async Task Latency_ZeroFromTheGenerator_InjectsNothing()
    {
        var eventRaised = false;
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = new FakeTimeProvider() }.AddChaosLatency(new ChaosLatencyStrategyOptions
        {
            InjectionRate = 1,
            LatencyGenerator = _ => TimeSpan.Zero,
            OnLatencyInjected = _ =>
            {
                eventRaised = true;
                return default;
            },
        }).Build();

        var task = pipeline.ExecuteAsync(static _ => new ValueTask<int>(1));

        task.IsCompletedSuccessfully.Should().BeTrue();
        (await task).Should().Be(1);
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public async Task Latency_CancelledDuringTheDelay_EndsWithACancellation_WithoutCallingTheCallback()
    {
        var time = new DelayRecordingTimeProvider();
        using var cts = new CancellationTokenSource();
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }.AddChaosLatency(1, TimeSpan.FromSeconds(10)).Build();

        var execution = pipeline.TryExecuteAsync(counter.Invoke, cts.Token).AsTask();
        await time.NextDelayAsync();
        await cts.CancelAsync();

        (await execution).Exception.Should().BeAssignableTo<OperationCanceledException>();
        counter.Calls.Should().Be(0);
    }

    [Fact]
    public void Latency_SynchronousExecute_Blocks()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosLatency(1, TimeSpan.FromMilliseconds(20)).Build();

        pipeline.Execute(static () => 5).Should().Be(5);
    }

    // ---- behavior ----

    [Fact]
    public async Task Behavior_RunsBeforeTheCallback_ThenTheEvent()
    {
        var order = new List<string>();
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = 1,
            BehaviorGenerator = _ =>
            {
                order.Add("behavior");
                return default;
            },
            OnBehaviorInjected = _ =>
            {
                order.Add("event");
                return default;
            },
        }).Build();

        var result = await pipeline.ExecuteAsync(_ =>
        {
            order.Add("callback");
            return new ValueTask<int>(1);
        });

        result.Should().Be(1);
        order.Should().Equal("behavior", "event", "callback");
    }

    [Fact]
    public async Task Behavior_ThatThrows_BecomesTheOutcome_AndTheCallbackDoesNotRun()
    {
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder().AddChaosBehavior(1, static _ => throw new InvalidOperationException("behavior")).Build();

        Func<Task> act = async () => await pipeline.ExecuteAsync(counter.Invoke);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("behavior");
        counter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Behavior_Shorthand_ReceivesTheExecutionsToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosBehavior(1, token =>
        {
            seen = token;
            return default;
        }).Build();

        (await pipeline.ExecuteAsync(static _ => new ValueTask<int>(1), cts.Token)).Should().Be(1);
        seen.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Behavior_Disabled_DoesNotRun()
    {
        var ran = false;
        var pipeline = new ResiliencePipelineBuilder<int>().AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            Enabled = false,
            InjectionRate = 1,
            BehaviorGenerator = _ =>
            {
                ran = true;
                return default;
            },
        }).Build();

        (await pipeline.ExecuteAsync(static _ => new ValueTask<int>(1))).Should().Be(1);
        ran.Should().BeFalse();
    }

    // ---- combinations ----

    [Fact]
    public async Task ChaosInsideHedging_IsDecidedPerAttempt()
    {
        var randomizer = new SequenceRandomizer(0, 0.99);
        var options = Fault(0.5, 0);
        options.Randomizer = randomizer.Next;
        var counter = new Counter();
        var pipeline = new ResiliencePipelineBuilder<int>()
            .AddHedging(new Hedging.HedgingStrategyOptions<int> { Delay = TimeSpan.Zero })
            .AddChaosFault(options)
            .Build();

        (await pipeline.ExecuteAsync(counter.Invoke)).Should().Be(1);
        counter.Calls.Should().Be(1);
    }
}
