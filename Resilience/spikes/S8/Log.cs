using Microsoft.Extensions.Logging;

namespace S8;

/// <summary>
/// Source-generated log events. Ids 1-3 keep Polly's ids (Polly: 1 "StrategyExecuting", 2 "StrategyExecuted",
/// 3 "ExecutionAttempt"); Polly funnels every other event through id 0 "ResilienceEvent", we give each its own.
/// No payload (result) on the happy-path events: Polly's {Result} is an <c>object?</c>, which boxes a value-type
/// result on every logged execution.
/// </summary>
public static partial class Log
{
    private const string Source = "Source: '{PipelineName}/{PipelineInstance}'";
    private const string StrategySource = "Source: '{PipelineName}/{PipelineInstance}/{StrategyName}'";

    [LoggerMessage(EventId = 1, EventName = "PipelineExecuting", Level = LogLevel.Debug,
        Message = "Resilience pipeline executing. " + Source)]
    public static partial void PipelineExecuting(ILogger logger, string pipelineName, string pipelineInstance);

    [LoggerMessage(EventId = 2, EventName = "PipelineExecuted",
        Message = "Resilience pipeline executed. " + Source + ", Handled: '{Handled}', Execution Time: {ExecutionTimeMs}ms")]
    public static partial void PipelineExecuted(ILogger logger, LogLevel level, string pipelineName, string pipelineInstance, bool handled, double executionTimeMs, Exception? exception);

    [LoggerMessage(EventId = 3, EventName = "ExecutionAttempt",
        Message = "Execution attempt. " + StrategySource + ", Handled: '{Handled}', Attempt: '{Attempt}', Execution Time: {ExecutionTimeMs}ms")]
    public static partial void ExecutionAttempt(ILogger logger, LogLevel level, string pipelineName, string pipelineInstance, string strategyName, bool handled, int attempt, double executionTimeMs, Exception? exception);

    [LoggerMessage(EventId = 10, EventName = "OnRetry", Level = LogLevel.Warning,
        Message = "Retrying. " + StrategySource + ", Attempt: '{Attempt}', Delay: {DelayMs}ms")]
    public static partial void Retry(ILogger logger, string pipelineName, string pipelineInstance, string strategyName, int attempt, double delayMs, Exception? exception);

    [LoggerMessage(EventId = 11, EventName = "OnTimeout", Level = LogLevel.Error,
        Message = "Execution timed out. " + StrategySource + ", Timeout: {TimeoutMs}ms")]
    public static partial void Timeout(ILogger logger, string pipelineName, string pipelineInstance, string strategyName, double timeoutMs);

    [LoggerMessage(EventId = 12, EventName = "OnFallback", Level = LogLevel.Warning,
        Message = "Fallback used. " + StrategySource)]
    public static partial void Fallback(ILogger logger, string pipelineName, string pipelineInstance, string strategyName, Exception? exception);

    [LoggerMessage(EventId = 20, EventName = "OnCircuitOpened", Level = LogLevel.Error,
        Message = "Circuit opened. " + StrategySource + ", Break: {BreakMs}ms")]
    public static partial void CircuitOpened(ILogger logger, string pipelineName, string pipelineInstance, string strategyName, double breakMs);

    [LoggerMessage(EventId = 21, EventName = "OnCircuitClosed", Level = LogLevel.Information,
        Message = "Circuit closed. " + StrategySource)]
    public static partial void CircuitClosed(ILogger logger, string pipelineName, string pipelineInstance, string strategyName);

    [LoggerMessage(EventId = 22, EventName = "OnCircuitHalfOpened", Level = LogLevel.Warning,
        Message = "Circuit half-opened. " + StrategySource)]
    public static partial void CircuitHalfOpened(ILogger logger, string pipelineName, string pipelineInstance, string strategyName);

    // Debug, not Warning: an open circuit rejects every call; the counter carries the rate, the log would flood.
    [LoggerMessage(EventId = 23, EventName = "OnCircuitRejected", Level = LogLevel.Debug,
        Message = "Execution rejected by an open circuit. " + StrategySource)]
    public static partial void CircuitRejected(ILogger logger, string pipelineName, string pipelineInstance, string strategyName);
}
