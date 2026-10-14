using MintPlayer.Resilience.SourceGenerator.Models;

namespace MintPlayer.Resilience.SourceGenerator.Generators;

/// <summary>The type of an attribute value, and how it maps onto the runtime option.</summary>
internal enum SettingType
{
    Int,
    Double,
    Bool,

    /// <summary>An enum; <see cref="ValueSpec.EnumType"/> names it.</summary>
    Enum,

    /// <summary>Integer milliseconds on the attribute, a <c>TimeSpan</c> option.</summary>
    Ms,

    /// <summary>Integer milliseconds on the attribute where -1 means "not set", a <c>TimeSpan?</c> option.</summary>
    NullableMs,
}

/// <summary>What a hook slot returns in the runtime option.</summary>
internal enum SlotReturn
{
    Bool,
    Event,
    TimeSpan,
    NullableTimeSpan,
    Double,
    Exception,
    Outcome,
    FallbackAction,
    Lease,
    ActionGenerator,
    Behavior,
}

/// <summary>One value of a strategy attribute (<c>MaxRetryAttempts</c>, <c>DelayMs</c>, …).</summary>
/// <param name="Attribute">The attribute property.</param>
/// <param name="Option">The runtime option property (and the reloadable options property).</param>
/// <param name="Type">The value type.</param>
/// <param name="Default">The attribute's default, as a raw number (or 0/1 for a bool, the underlying value for an enum).</param>
/// <param name="EnumType">The fully qualified enum type, for <see cref="SettingType.Enum"/>.</param>
internal sealed record ValueSpec(string Attribute, string Option, SettingType Type, double Default, string? EnumType = null);

/// <summary>One hook slot of a strategy.</summary>
/// <param name="Slot">The attribute property and runtime option name.</param>
/// <param name="Arguments">The fully qualified arguments type; <c>{T}</c> stands for the result type.</param>
/// <param name="Return">What the option returns.</param>
/// <param name="AllowOutcome">Whether the hook may take just the <c>Outcome&lt;T&gt;</c>.</param>
internal sealed record HookSpec(string Slot, string Arguments, SlotReturn Return, bool AllowOutcome = false)
{
    public bool ResultTyped => Arguments.Contains("{T}");
}

/// <summary>Everything the generator knows about one strategy kind.</summary>
internal sealed record KindSpec(
    StrategyKind Kind,
    string Attribute,
    string DefaultKey,
    ValueSpec[] Values,
    HookSpec[] Hooks,
    string[] Members,
    bool Inline = false,
    bool TypedOnly = false,
    bool Forks = false);

/// <summary>The strategy table: attribute metadata names, values with their defaults (Polly's), hook slots and signatures.</summary>
internal static class StrategySchema
{
    public const string Namespace = "MintPlayer.Resilience";
    public const string PipelineAttribute = "MintPlayer.Resilience.ResiliencePipelineAttribute";
    public const string GenericPipelineAttribute = "MintPlayer.Resilience.ResiliencePipelineAttribute`1";
    public const string BudgetAttribute = "MintPlayer.Resilience.RetryBudgetAttribute";
    public const string OutcomeType = "MintPlayer.Resilience.Outcome`1";

    private const string R = "global::MintPlayer.Resilience.";

    public static readonly KindSpec[] Kinds =
    [
        new(StrategyKind.Timeout, "MintPlayer.Resilience.TimeoutAttribute", "Timeout",
            [new("TimeoutMs", "Timeout", SettingType.Ms, 30_000)],
            [
                new("TimeoutGenerator", R + "Timeout.TimeoutGeneratorArguments", SlotReturn.TimeSpan),
                new("OnTimeout", R + "Timeout.OnTimeoutArguments", SlotReturn.Event),
            ],
            [], Inline: true),

        new(StrategyKind.Retry, "MintPlayer.Resilience.RetryAttribute", "Retry",
            [
                new("MaxRetryAttempts", "MaxRetryAttempts", SettingType.Int, 3),
                new("BackoffType", "BackoffType", SettingType.Enum, 0, R + "DelayBackoffType"),
                new("DelayMs", "Delay", SettingType.Ms, 2_000),
                new("MaxDelayMs", "MaxDelay", SettingType.NullableMs, -1),
                new("UseJitter", "UseJitter", SettingType.Bool, 0),
            ],
            [
                new("ShouldHandle", R + "Retry.RetryPredicateArguments<{T}>", SlotReturn.Bool, AllowOutcome: true),
                new("DelayGenerator", R + "Retry.RetryDelayGeneratorArguments<{T}>", SlotReturn.NullableTimeSpan, AllowOutcome: true),
                new("OnRetry", R + "Retry.OnRetryArguments<{T}>", SlotReturn.Event),
                new("OnBudgetExhausted", R + "Retry.OnRetryBudgetExhaustedArguments<{T}>", SlotReturn.Event),
            ],
            ["Budget"], Inline: true),

        new(StrategyKind.CircuitBreaker, "MintPlayer.Resilience.CircuitBreakerAttribute", "CircuitBreaker",
            [
                new("FailureRatio", "FailureRatio", SettingType.Double, 0.1),
                new("MinimumThroughput", "MinimumThroughput", SettingType.Int, 100),
                new("SamplingDurationMs", "SamplingDuration", SettingType.Ms, 30_000),
                new("BreakDurationMs", "BreakDuration", SettingType.Ms, 5_000),
                new("SlowCallDurationThresholdMs", "SlowCallDurationThreshold", SettingType.NullableMs, -1),
                new("SlowCallRatio", "SlowCallRatio", SettingType.Double, 1.0),
            ],
            [
                new("ShouldHandle", R + "CircuitBreaker.CircuitBreakerPredicateArguments<{T}>", SlotReturn.Bool, AllowOutcome: true),
                new("BreakDurationGenerator", R + "CircuitBreaker.BreakDurationGeneratorArguments", SlotReturn.TimeSpan),
                new("OnOpened", R + "CircuitBreaker.OnCircuitOpenedArguments<{T}>", SlotReturn.Event),
                new("OnClosed", R + "CircuitBreaker.OnCircuitClosedArguments<{T}>", SlotReturn.Event),
                new("OnHalfOpened", R + "CircuitBreaker.OnCircuitHalfOpenedArguments", SlotReturn.Event),
            ],
            ["ManualControl", "StateProvider"]),

        new(StrategyKind.Fallback, "MintPlayer.Resilience.FallbackAttribute", "Fallback",
            [],
            [
                new("ShouldHandle", R + "Fallback.FallbackPredicateArguments<{T}>", SlotReturn.Bool, AllowOutcome: true),
                new("FallbackAction", R + "Fallback.FallbackActionArguments<{T}>", SlotReturn.FallbackAction, AllowOutcome: true),
                new("OnFallback", R + "Fallback.OnFallbackArguments<{T}>", SlotReturn.Event),
            ],
            [], Inline: true),

        new(StrategyKind.RateLimiter, "MintPlayer.Resilience.RateLimiterAttribute", "RateLimiter",
            [],
            [
                new("RateLimiter", R + "RateLimiting.RateLimiterArguments", SlotReturn.Lease),
                new("OnRejected", R + "RateLimiting.OnRateLimiterRejectedArguments", SlotReturn.Event),
            ],
            ["RateLimiter"]),

        new(StrategyKind.ConcurrencyLimiter, "MintPlayer.Resilience.ConcurrencyLimiterAttribute", "ConcurrencyLimiter",
            [
                new("PermitLimit", "PermitLimit", SettingType.Int, 1000),
                new("QueueLimit", "QueueLimit", SettingType.Int, 0),
            ],
            [new("OnRejected", R + "RateLimiting.OnRateLimiterRejectedArguments", SlotReturn.Event)],
            []),

        new(StrategyKind.FixedWindowLimiter, "MintPlayer.Resilience.FixedWindowLimiterAttribute", "FixedWindowLimiter",
            [
                new("PermitLimit", "PermitLimit", SettingType.Int, 1000),
                new("WindowMs", "Window", SettingType.Ms, 1_000),
            ],
            [new("OnRejected", R + "RateLimiting.OnLimiterRejectedArguments", SlotReturn.Event)],
            []),

        new(StrategyKind.SlidingWindowLimiter, "MintPlayer.Resilience.SlidingWindowLimiterAttribute", "SlidingWindowLimiter",
            [
                new("PermitLimit", "PermitLimit", SettingType.Int, 1000),
                new("WindowMs", "Window", SettingType.Ms, 1_000),
                new("SegmentsPerWindow", "SegmentsPerWindow", SettingType.Int, 10),
            ],
            [new("OnRejected", R + "RateLimiting.OnLimiterRejectedArguments", SlotReturn.Event)],
            []),

        new(StrategyKind.NativeConcurrencyLimiter, "MintPlayer.Resilience.NativeConcurrencyLimiterAttribute", "NativeConcurrencyLimiter",
            [new("PermitLimit", "PermitLimit", SettingType.Int, 1000)],
            [new("OnRejected", R + "RateLimiting.OnLimiterRejectedArguments", SlotReturn.Event)],
            []),

        new(StrategyKind.AdaptiveConcurrencyLimiter, "MintPlayer.Resilience.AdaptiveConcurrencyLimiterAttribute", "AdaptiveConcurrencyLimiter",
            [
                new("Algorithm", "Algorithm", SettingType.Enum, 1, R + "RateLimiting.AdaptiveConcurrencyAlgorithm"),
                new("InitialLimit", "InitialLimit", SettingType.Int, 20),
                new("MinLimit", "MinLimit", SettingType.Int, 1),
                new("MaxLimit", "MaxLimit", SettingType.Int, 1000),
                new("BackoffRatio", "BackoffRatio", SettingType.Double, 0.9),
                new("Smoothing", "Smoothing", SettingType.Double, 0.2),
                new("Tolerance", "Tolerance", SettingType.Double, 1.5),
                new("LatencyWindow", "LatencyWindow", SettingType.Int, 600),
                new("LatencyThresholdMs", "LatencyThreshold", SettingType.NullableMs, -1),
            ],
            [
                new("ShouldHandle", R + "RateLimiting.AdaptiveConcurrencyLimiterPredicateArguments<{T}>", SlotReturn.Bool, AllowOutcome: true),
                new("OnRejected", R + "RateLimiting.OnLimiterRejectedArguments", SlotReturn.Event),
            ],
            []),

        new(StrategyKind.Hedging, "MintPlayer.Resilience.HedgingAttribute", "Hedging",
            [
                new("MaxHedgedAttempts", "MaxHedgedAttempts", SettingType.Int, 1),
                new("DelayMs", "Delay", SettingType.Ms, 2_000),
            ],
            [
                new("ShouldHandle", R + "Hedging.HedgingPredicateArguments<{T}>", SlotReturn.Bool, AllowOutcome: true),
                new("ActionGenerator", R + "Hedging.HedgingActionGeneratorArguments<{T}>", SlotReturn.ActionGenerator),
                new("DelayGenerator", R + "Hedging.HedgingDelayGeneratorArguments", SlotReturn.TimeSpan),
                new("OnHedging", R + "Hedging.OnHedgingArguments<{T}>", SlotReturn.Event),
            ],
            [], TypedOnly: true, Forks: true),

        new(StrategyKind.ChaosFault, "MintPlayer.Resilience.ChaosFaultAttribute", "ChaosFault",
            ChaosValues(),
            [
                .. ChaosHooks(),
                new("FaultGenerator", R + "Simmy.Fault.FaultGeneratorArguments", SlotReturn.Exception),
                new("OnFaultInjected", R + "Simmy.Fault.OnFaultInjectedArguments", SlotReturn.Event),
            ],
            []),

        new(StrategyKind.ChaosOutcome, "MintPlayer.Resilience.ChaosOutcomeAttribute", "ChaosOutcome",
            ChaosValues(),
            [
                .. ChaosHooks(),
                new("OutcomeGenerator", R + "Simmy.Outcomes.OutcomeGeneratorArguments", SlotReturn.Outcome),
                new("OnOutcomeInjected", R + "Simmy.Outcomes.OnOutcomeInjectedArguments<{T}>", SlotReturn.Event),
            ],
            [], TypedOnly: true),

        new(StrategyKind.ChaosLatency, "MintPlayer.Resilience.ChaosLatencyAttribute", "ChaosLatency",
            [.. ChaosValues(), new("LatencyMs", "Latency", SettingType.Ms, 30_000)],
            [
                .. ChaosHooks(),
                new("LatencyGenerator", R + "Simmy.Latency.LatencyGeneratorArguments", SlotReturn.TimeSpan),
                new("OnLatencyInjected", R + "Simmy.Latency.OnLatencyInjectedArguments", SlotReturn.Event),
            ],
            []),

        new(StrategyKind.ChaosBehavior, "MintPlayer.Resilience.ChaosBehaviorAttribute", "ChaosBehavior",
            ChaosValues(),
            [
                .. ChaosHooks(),
                new("BehaviorGenerator", R + "Simmy.Behavior.BehaviorGeneratorArguments", SlotReturn.Behavior),
                new("OnBehaviorInjected", R + "Simmy.Behavior.OnBehaviorInjectedArguments", SlotReturn.Event),
            ],
            []),
    ];

    /// <summary>Hook attributes (by metadata name): the kind and slot they bind to.</summary>
    public static readonly (string Attribute, StrategyKind Kind, string Slot)[] HookAttributes =
    [
        ("MintPlayer.Resilience.RetryWhenAttribute", StrategyKind.Retry, "ShouldHandle"),
        ("MintPlayer.Resilience.OnRetryAttribute", StrategyKind.Retry, "OnRetry"),
        ("MintPlayer.Resilience.DelayGeneratorAttribute", StrategyKind.Retry, "DelayGenerator"),
        ("MintPlayer.Resilience.TimeoutGeneratorAttribute", StrategyKind.Timeout, "TimeoutGenerator"),
        ("MintPlayer.Resilience.OnTimeoutAttribute", StrategyKind.Timeout, "OnTimeout"),
        ("MintPlayer.Resilience.BreakWhenAttribute", StrategyKind.CircuitBreaker, "ShouldHandle"),
        ("MintPlayer.Resilience.OnOpenedAttribute", StrategyKind.CircuitBreaker, "OnOpened"),
        ("MintPlayer.Resilience.OnClosedAttribute", StrategyKind.CircuitBreaker, "OnClosed"),
        ("MintPlayer.Resilience.OnHalfOpenedAttribute", StrategyKind.CircuitBreaker, "OnHalfOpened"),
        ("MintPlayer.Resilience.BreakDurationGeneratorAttribute", StrategyKind.CircuitBreaker, "BreakDurationGenerator"),
        ("MintPlayer.Resilience.FallbackWhenAttribute", StrategyKind.Fallback, "ShouldHandle"),
        ("MintPlayer.Resilience.FallbackWithAttribute", StrategyKind.Fallback, "FallbackAction"),
        ("MintPlayer.Resilience.OnFallbackAttribute", StrategyKind.Fallback, "OnFallback"),
        ("MintPlayer.Resilience.HedgeWhenAttribute", StrategyKind.Hedging, "ShouldHandle"),
        ("MintPlayer.Resilience.OnHedgingAttribute", StrategyKind.Hedging, "OnHedging"),
    ];

    public static KindSpec Of(StrategyKind kind)
    {
        foreach (var spec in Kinds)
        {
            if (spec.Kind == kind)
            {
                return spec;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
    }

    public static KindSpec? ByAttribute(string metadataName)
    {
        foreach (var spec in Kinds)
        {
            if (spec.Attribute == metadataName)
            {
                return spec;
            }
        }

        return null;
    }

    private static ValueSpec[] ChaosValues() =>
    [
        new("InjectionRate", "InjectionRate", SettingType.Double, 0.001),
        new("Enabled", "Enabled", SettingType.Bool, 1),
    ];

    private static HookSpec[] ChaosHooks() =>
    [
        new("EnabledGenerator", R + "Simmy.EnabledGeneratorArguments", SlotReturn.Bool),
        new("InjectionRateGenerator", R + "Simmy.InjectionRateGeneratorArguments", SlotReturn.Double),
    ];
}
