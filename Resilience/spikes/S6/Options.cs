namespace S6;

// ---- Runtime package: bindable per-strategy option POCOs (shared by every generated pipeline) ----

public sealed class TimeoutStrategyOptions
{
    public TimeSpan Timeout { get; set; }
}

public sealed class RetryStrategyOptions
{
    public int MaxRetries { get; set; }
    public TimeSpan Delay { get; set; }
}

public sealed class CircuitBreakerStrategyOptions
{
    public double FailureRatio { get; set; }
    public int MinimumThroughput { get; set; }
    public TimeSpan SamplingDuration { get; set; }
    public TimeSpan BreakDuration { get; set; }
}

// ---- Generated per pipeline: one options class whose initializers ARE the attribute values ----
//
//   [ResiliencePipeline(Reloadable = true)]                                   // section "Resilience:CatalogPipeline"
//   [Timeout(Seconds = 10, Name = "Total")]
//   [Retry(MaxRetries = 3)]
//   [CircuitBreaker(FailureRatio = 0.9, MinimumThroughput = 10, SamplingSeconds = 30, BreakSeconds = 15)]
//   [Timeout(Seconds = 2, Name = "Attempt")]
//   public sealed partial class CatalogPipeline;
//
// Property names: the attribute's Name, else the strategy kind; a second unnamed strategy of the same kind is a
// generator error (it would need a Name to have a stable config key). Because OptionsFactory creates a fresh
// instance per reload, a key REMOVED from configuration falls back to the attribute value, not to the last value.

public sealed class CatalogPipelineOptions
{
    public TimeoutStrategyOptions Total { get; set; } = new() { Timeout = TimeSpan.FromSeconds(10) };
    public RetryStrategyOptions Retry { get; set; } = new() { MaxRetries = 3, Delay = TimeSpan.Zero };
    public CircuitBreakerStrategyOptions CircuitBreaker { get; set; } = new()
    {
        FailureRatio = 0.9,
        MinimumThroughput = 10,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15),
    };
    public TimeoutStrategyOptions Attempt { get; set; } = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>Generated: returns null when valid, else the first problem. Runs on every reload.</summary>
    public string? Validate()
    {
        if (Total is null || Retry is null || CircuitBreaker is null || Attempt is null) return "a strategy section is null";
        if (Total.Timeout <= TimeSpan.Zero) return "Total:Timeout must be > 0";
        if (Attempt.Timeout <= TimeSpan.Zero) return "Attempt:Timeout must be > 0";
        if (Retry.MaxRetries < 0) return "Retry:MaxRetries must be >= 0";
        if (Retry.Delay < TimeSpan.Zero) return "Retry:Delay must be >= 0";
        if (CircuitBreaker.FailureRatio is <= 0 or > 1) return "CircuitBreaker:FailureRatio must be in (0, 1]";
        if (CircuitBreaker.MinimumThroughput < 2) return "CircuitBreaker:MinimumThroughput must be >= 2";
        if (CircuitBreaker.SamplingDuration <= TimeSpan.Zero) return "CircuitBreaker:SamplingDuration must be > 0";
        if (CircuitBreaker.BreakDuration <= TimeSpan.Zero) return "CircuitBreaker:BreakDuration must be > 0";
        return null;
    }
}

/// <summary>
/// Variant B's snapshot: an immutable class, published with one reference write. Fields, not properties, and the
/// breaker settings as a struct field so they can be passed by <c>in</c> without a copy.
/// </summary>
public sealed class CatalogSnapshot(CatalogPipelineOptions o, long version)
{
    public readonly TimeSpan OuterTimeout = o.Total.Timeout;
    public readonly TimeSpan InnerTimeout = o.Attempt.Timeout;
    public readonly int MaxRetries = o.Retry.MaxRetries;
    public readonly TimeSpan RetryDelay = o.Retry.Delay;
    public readonly BreakerSettings Breaker = new(
        o.CircuitBreaker.FailureRatio, o.CircuitBreaker.MinimumThroughput,
        o.CircuitBreaker.SamplingDuration, o.CircuitBreaker.BreakDuration);
    public readonly long Version = version;
}

/// <summary>Variant C's snapshot: the same values as a readonly struct, copied into the execution's frame.</summary>
public readonly struct CatalogValues(CatalogPipelineOptions o)
{
    public readonly TimeSpan OuterTimeout = o.Total.Timeout;
    public readonly TimeSpan InnerTimeout = o.Attempt.Timeout;
    public readonly int MaxRetries = o.Retry.MaxRetries;
    public readonly TimeSpan RetryDelay = o.Retry.Delay;
    public readonly BreakerSettings Breaker = new(
        o.CircuitBreaker.FailureRatio, o.CircuitBreaker.MinimumThroughput,
        o.CircuitBreaker.SamplingDuration, o.CircuitBreaker.BreakDuration);
}
