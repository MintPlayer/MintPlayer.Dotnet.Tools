namespace S8;

/// <summary>
/// Every name a dashboard or alert rule would bind to. Bound once when a pipeline's telemetry is built,
/// so switching naming schemes costs nothing per execution.
/// </summary>
public sealed record TelemetryNames(
    string MeterName,
    string EventsCounter,
    string AttemptDuration,
    string PipelineDuration,
    string DurationUnit,        // "s" (OTel semantic-convention guidance) or "ms" (Polly)
    string ExceptionTypeTag,    // "error.type" (OTel stable attribute) or "exception.type" (Polly)
    string LoggerCategory)
{
    /// <summary>Our default: Polly's semantic names without the vendor segment, OTel units and error.type.</summary>
    public static readonly TelemetryNames Default = new(
        MeterName: "MintPlayer.Resilience",
        EventsCounter: "resilience.strategy.events",
        AttemptDuration: "resilience.strategy.attempt.duration",
        PipelineDuration: "resilience.pipeline.duration",
        DurationUnit: "s",
        ExceptionTypeTag: "error.type",
        LoggerCategory: "MintPlayer.Resilience");

    /// <summary>Drop-in for dashboards and alerts built on Polly v8 (and Microsoft.Extensions.Resilience).</summary>
    public static readonly TelemetryNames PollyCompatible = new(
        MeterName: "Polly",
        EventsCounter: "resilience.polly.strategy.events",
        AttemptDuration: "resilience.polly.strategy.attempt.duration",
        PipelineDuration: "resilience.polly.pipeline.duration",
        DurationUnit: "ms",
        ExceptionTypeTag: "exception.type",
        LoggerCategory: "Polly");

    public double TicksToUnit => (DurationUnit == "ms" ? 1_000.0 : 1.0) / System.Diagnostics.Stopwatch.Frequency;
}

/// <summary>Tag keys: identical to Polly's (Polly.Extensions/Telemetry/ResilienceTelemetryTags.cs).</summary>
public static class Tags
{
    public const string EventName = "event.name";
    public const string EventSeverity = "event.severity";
    public const string PipelineName = "pipeline.name";
    public const string PipelineInstance = "pipeline.instance";
    public const string StrategyName = "strategy.name";
    public const string OperationKey = "operation.key";
    public const string AttemptNumber = "attempt.number";
    public const string AttemptHandled = "attempt.handled";
}

/// <summary>event.name values: identical to Polly's, plus OnCircuitRejected (Polly has no rejection event).</summary>
public static class Events
{
    public const string PipelineExecuting = "PipelineExecuting";
    public const string PipelineExecuted = "PipelineExecuted";
    public const string ExecutionAttempt = "ExecutionAttempt";
    public const string OnRetry = "OnRetry";
    public const string OnTimeout = "OnTimeout";
    public const string OnFallback = "OnFallback";
    public const string OnCircuitOpened = "OnCircuitOpened";
    public const string OnCircuitClosed = "OnCircuitClosed";
    public const string OnCircuitHalfOpened = "OnCircuitHalfOpened";
    public const string OnCircuitRejected = "OnCircuitRejected";
}

/// <summary>event.severity values: Polly's ResilienceEventSeverity names.</summary>
public static class Severity
{
    public const string Debug = "Debug";
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";
}
