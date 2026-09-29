using MintPlayer.Resilience.Simmy;
using MintPlayer.Resilience.Simmy.Behavior;
using MintPlayer.Resilience.Simmy.Fault;
using MintPlayer.Resilience.Simmy.Latency;
using MintPlayer.Resilience.Simmy.Outcomes;

namespace MintPlayer.Resilience.Tests;

/// <summary>
/// A chaos strategy that is disabled, or enabled but not injecting on this call, allocates nothing (PRD §2.3: a
/// disabled chaos strategy is one branch). Runs in the allocation collection, alone.
/// </summary>
[Collection(nameof(AllocationTests))]
public class ChaosAllocationTests
{
    private const int Warmup = 200;
    private const int Iterations = 2_000;

    private static readonly Func<int, CancellationToken, ValueTask<int>> Callback = static (state, _) => new ValueTask<int>(state);

    private static long Measure(ResiliencePipeline<int> pipeline)
    {
        for (var i = 0; i < Warmup; i++)
        {
            Run(pipeline);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            Run(pipeline);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Run(ResiliencePipeline<int> pipeline)
    {
        var task = pipeline.ExecuteAsync(Callback, 1);
        if (!task.IsCompletedSuccessfully || task.Result != 1)
        {
            throw new InvalidOperationException("The call did not complete synchronously with the callback's result.");
        }
    }

    private static ResiliencePipeline<int> EveryChaosStrategy(Action<ChaosStrategyOptions> configure)
    {
        var fault = new ChaosFaultStrategyOptions { FaultGenerator = static _ => new InvalidOperationException() };
        var outcome = new ChaosOutcomeStrategyOptions<int> { OutcomeGenerator = static _ => Outcome.FromResult(2) };
        var latency = new ChaosLatencyStrategyOptions { Latency = TimeSpan.FromSeconds(1) };
        var behavior = new ChaosBehaviorStrategyOptions { BehaviorGenerator = static _ => throw new InvalidOperationException() };
        configure(fault);
        configure(outcome);
        configure(latency);
        configure(behavior);

        return new ResiliencePipelineBuilder<int>()
            .AddChaosFault(fault)
            .AddChaosOutcome(outcome)
            .AddChaosLatency(latency)
            .AddChaosBehavior(behavior)
            .Build();
    }

    [Fact]
    public void Disabled_AllocatesNothing()
    {
        var pipeline = EveryChaosStrategy(static options =>
        {
            options.Enabled = false;
            options.InjectionRate = 1;
        });

        Measure(pipeline).Should().Be(0);
    }

    [Fact]
    public void ZeroInjectionRate_AllocatesNothing()
    {
        var pipeline = EveryChaosStrategy(static options => options.InjectionRate = 0);

        Measure(pipeline).Should().Be(0);
    }

    [Fact]
    public void EnabledButNotInjecting_AllocatesNothing()
    {
        var pipeline = EveryChaosStrategy(static options =>
        {
            options.InjectionRate = 0.5;
            options.Randomizer = static () => 0.9;
        });

        Measure(pipeline).Should().Be(0);
    }

    [Fact]
    public void GeneratorsThatDoNotReadTheContext_AllocateNothing()
    {
        var pipeline = EveryChaosStrategy(static options =>
        {
            options.EnabledGenerator = static _ => true;
            options.InjectionRateGenerator = static _ => 0.1;
            options.Randomizer = static () => 0.5;
        });

        Measure(pipeline).Should().Be(0);
    }
}
