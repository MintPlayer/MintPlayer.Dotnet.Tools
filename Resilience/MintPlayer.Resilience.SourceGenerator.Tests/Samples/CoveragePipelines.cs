using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using MintPlayer.Resilience;
using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Hedging;
using MintPlayer.Resilience.RateLimiting;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Simmy;
using MintPlayer.Resilience.Simmy.Behavior;
using MintPlayer.Resilience.Simmy.Outcomes;
using MintPlayer.Resilience.Timeout;

namespace ResilienceSamples;

// Compile checks: every strategy attribute, every hook shape and every member reference, in the static, DI,
// generic and reloadable forms. They are executed only lightly (smoke tests); their job is to prove that the
// generated code compiles for each combination.

/// <summary>Every strategy kind that can be flattened, with class-level budget, manual control and a shared limiter.</summary>
[ResiliencePipeline<string>]
[RetryBudget(RetryRatio = 0.5, MinRetriesPerSecond = 5, TimeToLiveMs = 5_000)]
[Fallback(FallbackAction = nameof(Recover))]
[Timeout(TimeoutMs = 30_000, Name = "Total", TimeoutGenerator = nameof(TotalTimeout))]
[ConcurrencyLimiter(PermitLimit = 100, QueueLimit = 10)]
[RateLimiter(RateLimiter = nameof(SharedLimiter), OnRejected = nameof(LimiterRejected))]
[FixedWindowLimiter(PermitLimit = 1_000, WindowMs = 1_000)]
[SlidingWindowLimiter(PermitLimit = 1_000, WindowMs = 1_000, SegmentsPerWindow = 4)]
[NativeConcurrencyLimiter(PermitLimit = 50, OnRejected = nameof(NativeRejected))]
[AdaptiveConcurrencyLimiter(Algorithm = AdaptiveConcurrencyAlgorithm.Aimd, InitialLimit = 10, MaxLimit = 100, LatencyThresholdMs = 250, ShouldHandle = nameof(Drops))]
[Retry(MaxRetryAttempts = 2, DelayMs = 10, MaxDelayMs = 50, BackoffType = DelayBackoffType.Exponential, DelayGenerator = nameof(RetryDelay), OnBudgetExhausted = nameof(BudgetExhausted))]
[CircuitBreaker(ManualControl = nameof(Control), StateProvider = nameof(State), SlowCallDurationThresholdMs = 500, SlowCallRatio = 0.5, BreakDurationGenerator = nameof(BreakFor))]
[ChaosFault(FaultType = typeof(TimeoutException), InjectionRate = 0, EnabledGenerator = nameof(ChaosOn))]
[ChaosOutcome(InjectionRate = 0, OutcomeGenerator = nameof(InjectedOutcome), OnOutcomeInjected = nameof(OutcomeInjected))]
[ChaosLatency(Enabled = false, LatencyMs = 5, InjectionRateGenerator = nameof(ChaosRate))]
[ChaosBehavior(InjectionRate = 0, BehaviorGenerator = nameof(Misbehave))]
[Timeout(TimeoutMs = 5_000, Name = "Attempt")]
public sealed partial class KitchenSinkPipeline
{
    internal static readonly RateLimiter SharedLimiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1_000, QueueLimit = 0 });

    internal static CircuitBreakerManualControl Control { get; } = new();

    internal static CircuitBreakerStateProvider State { get; } = new();

    internal static Outcome<string> Recover(Outcome<string> outcome) => Outcome.FromResult("recovered");

    internal static TimeSpan TotalTimeout(TimeoutGeneratorArguments args) => TimeSpan.FromSeconds(30);

    internal static ValueTask LimiterRejected(OnRateLimiterRejectedArguments args) => default;

    internal static void NativeRejected(OnLimiterRejectedArguments args)
    {
    }

    internal static bool Drops(AdaptiveConcurrencyLimiterPredicateArguments<string> args) => args.Outcome.Exception is TimeoutRejectedException;

    internal static TimeSpan? RetryDelay(RetryDelayGeneratorArguments<string> args) => null;

    internal static ValueTask BudgetExhausted(OnRetryBudgetExhaustedArguments<string> args) => default;

    internal static TimeSpan BreakFor(BreakDurationGeneratorArguments args) => TimeSpan.FromSeconds(args.FailureCount);

    internal static bool ChaosOn(EnabledGeneratorArguments args) => true;

    internal static double ChaosRate(InjectionRateGeneratorArguments args) => 0;

    internal static Outcome<string>? InjectedOutcome(OutcomeGeneratorArguments args) => Outcome.FromResult("injected");

    internal static ValueTask OutcomeInjected(OnOutcomeInjectedArguments<string> args) => default;

    internal static ValueTask Misbehave(BehaviorGeneratorArguments args) => default;
}

/// <summary>A reloadable, unpooled, generic DI-form pipeline with an instance generic hook and an instance budget.</summary>
[ResiliencePipeline(Reloadable = true, PooledAsync = false, Name = "Tenant")]
[Retry(MaxRetryAttempts = 2, DelayMs = 0, Budget = nameof(Budget))]
[CircuitBreaker(MinimumThroughput = 5)]
[FixedWindowLimiter(PermitLimit = 10)]
[Timeout(TimeoutMs = 1_000)]
[Timeout(TimeoutMs = 500)]
public sealed partial class TenantPipeline(EventJournal journal, int retries = 2)
{
    internal RetryBudget Budget { get; } = new(0.2, retries);

    [OnRetry]
    internal void Retried<TResult>(OnRetryArguments<TResult> args) => journal.Add($"tenant retry {args.AttemptNumber}");
}

/// <summary>A hedging pipeline in the DI form, reloadable: it forwards to a runtime pipeline rebuilt on reload.</summary>
[ResiliencePipeline<int>(Reloadable = true)]
[Retry(MaxRetryAttempts = 1, DelayMs = 0)]
[Hedging(MaxHedgedAttempts = 2, DelayMs = 0)]
public sealed partial class InstanceHedgingPipeline(EventJournal journal)
{
    [OnHedging]
    internal void Hedged(OnHedgingArguments<int> args) => journal.Add("hedged");

    [FallbackWith]
    internal static int Unused() => 0;
}

/// <summary>A nested pipeline: the containing types are re-declared as partial.</summary>
public static partial class Outer
{
    /// <summary>The nested pipeline.</summary>
    [ResiliencePipeline<int>]
    [Retry(MaxRetryAttempts = 1, DelayMs = 0)]
    [Fallback]
    public sealed partial class NestedPipeline
    {
        [FallbackWith]
        internal static ValueTask<int> Fallback(FallbackActionArguments<int> args) => new(-1);
    }
}
