namespace MintPlayer.Resilience;

/// <summary>
/// Declares a pipeline that the source generator compiles into the class itself (PRD §2.1). Put it on a
/// <c>sealed partial class</c> together with strategy attributes (<see cref="TimeoutAttribute"/>,
/// <see cref="RetryAttribute"/>, <see cref="CircuitBreakerAttribute"/>, …), which run in declaration order,
/// the first one outermost. The generator emits <c>ExecuteAsync</c>, <c>TryExecuteAsync</c> and
/// <c>Execute</c> on the class.
/// </summary>
/// <remarks>
/// <para>
/// <b>Result type.</b> With <see cref="ResiliencePipelineAttribute{TResult}"/>, or when a hook names a
/// concrete result type (<c>static bool Transient(Outcome&lt;HttpResponseMessage&gt; o)</c>), the pipeline is
/// typed: its members execute callbacks returning that type, like a <see cref="ResiliencePipeline{T}"/>.
/// Otherwise it is generic, like a non-generic <see cref="ResiliencePipeline"/>: <c>ExecuteAsync&lt;TResult&gt;</c>
/// plus void overloads, and result-typed hooks must be generic methods (<c>static bool Handle&lt;TResult&gt;(Outcome&lt;TResult&gt; o)</c>).
/// </para>
/// <para>
/// <b>Static and DI forms.</b> When every hook is static and the class has no constructor with parameters,
/// the generated members are static (<c>CatalogPipeline.ExecuteAsync(…)</c>) and the strategy state (a
/// breaker's health) is process-wide. When a hook is an instance method or a constructor takes parameters,
/// the members are instance members, the state lives on the instance, and the class is meant to be a DI
/// singleton whose hooks use injected services.
/// </para>
/// <para>
/// <b>Hooks</b> are bound either by name, through the strategy attribute's string properties
/// (<c>[Retry(ShouldHandle = nameof(Transient))]</c>), or by a hook attribute on the method
/// (<c>[RetryWhen]</c>), which binds to the only strategy of its kind, or to the one whose <c>Name</c> it
/// gives when there are several. A name given on the strategy wins over a hook attribute.
/// </para>
/// <para>
/// The generated class implements <see cref="IGeneratedResiliencePipeline{TSelf}"/> (and
/// <see cref="IReloadableResiliencePipeline{TSelf, TOptions}"/> when <see cref="Reloadable"/> is set), the
/// seam through which dependency injection registers it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ResiliencePipelineAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether the generated asynchronous members return a pooled <see cref="ValueTask"/>
    /// (plan S2; default <see langword="true"/>). A pooled task must be awaited exactly once; set this to
    /// <see langword="false"/> for callers that await a result twice or read <c>.Result</c> early.
    /// </summary>
    public bool PooledAsync { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the strategy values can be reloaded from configuration (plan S6). The generator
    /// then emits a <c>&lt;ClassName&gt;Options</c> class whose defaults are the attribute values, and each
    /// execution reads one immutable settings snapshot at entry. Default <see langword="false"/>: the values
    /// are compile-time constants.
    /// </summary>
    public bool Reloadable { get; set; }

    /// <summary>Gets or sets the name of the pipeline, used by telemetry and as the configuration key. Default: the class name.</summary>
    public string? Name { get; set; }
}

/// <summary>Declares a generated pipeline whose executions return <typeparamref name="TResult"/>; see <see cref="ResiliencePipelineAttribute"/>.</summary>
/// <typeparam name="TResult">The result type of the callbacks.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ResiliencePipelineAttribute<TResult> : ResiliencePipelineAttribute
{
}

/// <summary>
/// Gives the retry strategies of a generated pipeline a budget owned by the pipeline (beyond Polly, see
/// <see cref="Retry.RetryBudget"/>). Not a strategy: its position among the attributes does not matter.
/// A <see cref="RetryAttribute.Budget"/> that names a shared budget takes precedence.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RetryBudgetAttribute : Attribute
{
    /// <summary>Gets or sets the retries allowed per recent request. Default 0.2.</summary>
    public double RetryRatio { get; set; } = 0.2;

    /// <summary>Gets or sets the retries allowed per second regardless of traffic. Default 10.</summary>
    public int MinRetriesPerSecond { get; set; } = 10;

    /// <summary>Gets or sets how long a deposited request counts, in milliseconds. Default 10 000.</summary>
    public int TimeToLiveMs { get; set; } = 10_000;
}
