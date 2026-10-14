using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Resilience.SourceGenerator.Models;

/// <summary>The strategy kinds a <c>[ResiliencePipeline]</c> class can declare, one per strategy attribute.</summary>
public enum StrategyKind
{
    Timeout,
    Retry,
    CircuitBreaker,
    Fallback,
    RateLimiter,
    ConcurrencyLimiter,
    FixedWindowLimiter,
    SlidingWindowLimiter,
    NativeConcurrencyLimiter,
    AdaptiveConcurrencyLimiter,
    Hedging,
    ChaosFault,
    ChaosOutcome,
    ChaosLatency,
    ChaosBehavior,
}

/// <summary>What a hook method takes.</summary>
public enum HookParameter
{
    /// <summary>The strategy's arguments struct (<c>OnRetryArguments&lt;T&gt;</c>, …).</summary>
    Arguments,

    /// <summary>Only the outcome (<c>Outcome&lt;T&gt;</c>), for predicates and the fallback action.</summary>
    Outcome,

    /// <summary>Nothing.</summary>
    None,
}

/// <summary>What a hook method returns, as far as the generator adapts it.</summary>
public enum HookReturn
{
    /// <summary>The type the option expects (bool, TimeSpan, ValueTask, …), used as is.</summary>
    Direct,

    /// <summary><c>void</c>, for an event that is expected to return <c>ValueTask</c>.</summary>
    Void,

    /// <summary>A fallback action returning <c>ValueTask&lt;Outcome&lt;T&gt;&gt;</c>.</summary>
    ValueTaskOutcome,

    /// <summary>A fallback action returning <c>Outcome&lt;T&gt;</c>.</summary>
    Outcome,

    /// <summary>A fallback action returning <c>ValueTask&lt;T&gt;</c>.</summary>
    ValueTaskResult,

    /// <summary>A fallback action returning <c>T</c>.</summary>
    Result,
}

/// <summary>A containing type of a nested pipeline class, re-declared as <c>partial</c>.</summary>
[GenerateEquality]
public sealed partial class ContainingTypeModel
{
    /// <summary>"class", "struct", "record", "record struct" or "interface".</summary>
    public string Keyword { get; set; } = "class";

    public string Name { get; set; } = string.Empty;
}

/// <summary>One attribute property that was set: its name and its value as a C# literal.</summary>
[GenerateEquality]
public sealed partial class SettingModel
{
    public string Key { get; set; } = string.Empty;

    /// <summary>The value as a C# expression: <c>3</c>, <c>0.5d</c>, <c>true</c>, <c>(global::X.Y)2</c>, <c>typeof(global::X)</c>.</summary>
    public string Literal { get; set; } = string.Empty;

    /// <summary>The raw value for integer settings (milliseconds and counts), so the producer can test for the -1 sentinel.</summary>
    public long? Integer { get; set; }
}

/// <summary>A hook bound to a strategy slot (<c>ShouldHandle</c>, <c>OnRetry</c>, …).</summary>
[GenerateEquality]
public sealed partial class HookModel
{
    /// <summary>The option the hook fills, e.g. <c>ShouldHandle</c>.</summary>
    public string Slot { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public bool IsStatic { get; set; }

    /// <summary>The method has one type parameter, which stands for the result type.</summary>
    public bool IsGeneric { get; set; }

    public HookParameter Parameter { get; set; }

    public HookReturn Return { get; set; }
}

/// <summary>A field or property of the pipeline class referenced by a strategy (a shared budget, manual control, a limiter).</summary>
[GenerateEquality]
public sealed partial class MemberRefModel
{
    public string Slot { get; set; } = string.Empty;

    public string Member { get; set; } = string.Empty;

    public bool IsStatic { get; set; }
}

/// <summary>One strategy attribute of the pipeline, in declaration order.</summary>
[GenerateEquality]
public sealed partial class StrategyModel
{
    public StrategyKind Kind { get; set; }

    /// <summary>The position of the strategy, 0 = outermost; also its slot in the execution frame.</summary>
    public int Index { get; set; }

    /// <summary>The attribute's <c>Name</c>, when set.</summary>
    public string? Name { get; set; }

    /// <summary>The property name of the strategy in a reloadable pipeline's options class (the name, else the kind; made unique).</summary>
    public string Key { get; set; } = string.Empty;

    public EquatableArray<SettingModel> Settings { get; set; } = EquatableArray<SettingModel>.Empty;

    public EquatableArray<HookModel> Hooks { get; set; } = EquatableArray<HookModel>.Empty;

    public EquatableArray<MemberRefModel> Members { get; set; } = EquatableArray<MemberRefModel>.Empty;
}

/// <summary>A constructor parameter of a DI-form pipeline, resolved from the service provider by the generated <c>Create</c>.</summary>
[GenerateEquality]
public sealed partial class ConstructorParameterModel
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    /// <summary>The C# expression of the parameter's default value, or <see langword="null"/> when it has none.</summary>
    public string? Default { get; set; }
}

/// <summary>A class-level <c>[RetryBudget]</c>.</summary>
[GenerateEquality]
public sealed partial class BudgetModel
{
    public string RetryRatio { get; set; } = "0.2d";

    public string MinRetriesPerSecond { get; set; } = "10";

    public string TimeToLiveMs { get; set; } = "10000";
}

/// <summary>A <c>[ResiliencePipeline]</c> class reduced to everything the producer needs.</summary>
/// <remarks>Value equality is generated by <c>[GenerateEquality]</c>; the model holds no symbol, syntax node or location.</remarks>
[GenerateEquality]
public sealed partial class PipelineModel
{
    /// <summary>The namespace, empty for the global namespace.</summary>
    public string Namespace { get; set; } = string.Empty;

    public EquatableArray<ContainingTypeModel> ContainingTypes { get; set; } = EquatableArray<ContainingTypeModel>.Empty;

    public string ClassName { get; set; } = string.Empty;

    /// <summary>The fully qualified (<c>global::</c>) name of the class.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>The declared accessibility keyword of the class, reused for the generated options class.</summary>
    public string Accessibility { get; set; } = "internal";

    /// <summary>The DI form: instance members, per-instance state.</summary>
    public bool IsInstance { get; set; }

    /// <summary>The fully qualified result type; <see langword="null"/> for a generic pipeline.</summary>
    public string? ResultType { get; set; }

    public bool PooledAsync { get; set; } = true;

    public bool Reloadable { get; set; }

    /// <summary>The attribute's <c>Name</c>, else the class name.</summary>
    public string PipelineName { get; set; } = string.Empty;

    public EquatableArray<StrategyModel> Strategies { get; set; } = EquatableArray<StrategyModel>.Empty;

    public BudgetModel? Budget { get; set; }

    public EquatableArray<ConstructorParameterModel> ConstructorParameters { get; set; } = EquatableArray<ConstructorParameterModel>.Empty;

    /// <summary>
    /// Why the class cannot be generated (not partial, generic, a generic-only strategy in a generic pipeline, …),
    /// or <see langword="null"/>. Such a class gets no generated code; the analyzers (MPR0004–MPR0007) report it.
    /// </summary>
    public string? SkipReason { get; set; }

    public override string ToString() => FullName;
}
