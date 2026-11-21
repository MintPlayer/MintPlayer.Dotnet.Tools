using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using KV = System.Collections.Generic.KeyValuePair<string, object?>;

namespace S8;

/// <summary>One Meter + ActivitySource + three instruments per naming scheme, shared by every pipeline.</summary>
public sealed class ResilienceInstruments
{
    private static readonly ConcurrentDictionary<TelemetryNames, ResilienceInstruments> s_cache = new();

    // OTel HTTP semconv buckets (seconds). Without advice, OTel's default buckets (0..10000) assume ms.
    private static readonly double[] s_secondsBuckets =
        [0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private ResilienceInstruments(TelemetryNames names)
    {
        Names = names;
        Meter = new Meter(new MeterOptions(names.MeterName) { Version = "1.0.0" });
        ActivitySource = new ActivitySource(names.MeterName, "1.0.0");
        var advice = names.DurationUnit == "s" ? new InstrumentAdvice<double> { HistogramBucketBoundaries = s_secondsBuckets } : null;
        Events = Meter.CreateCounter<int>(names.EventsCounter, unit: names == TelemetryNames.PollyCompatible ? null : "{event}",
            description: "Tracks the number of resilience events that occurred in resilience strategies.");
        AttemptDuration = Meter.CreateHistogram(names.AttemptDuration, names.DurationUnit,
            "Tracks the duration of execution attempts.", tags: null, advice: advice);
        PipelineDuration = Meter.CreateHistogram(names.PipelineDuration, names.DurationUnit,
            "The execution duration of resilience pipelines.", tags: null, advice: advice);
    }

    public static ResilienceInstruments For(TelemetryNames names) => s_cache.GetOrAdd(names, static n => new(n));

    public TelemetryNames Names { get; }
    public Meter Meter { get; }
    public ActivitySource ActivitySource { get; }
    public Counter<int> Events { get; }
    public Histogram<double> AttemptDuration { get; }
    public Histogram<double> PipelineDuration { get; }
}

/// <summary>
/// Per pipeline instance: every tag set the happy path and the common failure paths need is pre-built here,
/// once, so a measurement is "is the instrument Enabled? then Record(value, prebuiltArray)". No TagList
/// construction, no boxing, no per-call strategy-name lookups.
///
/// Generated pipelines hold one of these in a readonly field and call it unconditionally (telemetry that was
/// switched off at compile time is simply not emitted). Runtime pipelines hold a nullable reference and pay
/// one null check per call site.
/// </summary>
public sealed class PipelineTelemetry
{
    private const int MaxPrebuiltAttempts = 16;
    private static readonly object[] s_boxedInts = [.. Enumerable.Range(0, 100).Select(static i => (object)i)];
    private static readonly object s_true = true, s_false = false;

    private readonly Counter<int> _events;
    private readonly Histogram<double> _attemptDuration;
    private readonly Histogram<double> _pipelineDuration;
    private readonly ActivitySource _activitySource;
    private readonly ILogger _logger;
    private readonly double _ticksToUnit;
    private readonly double _ticksToMs = 1_000.0 / Stopwatch.Frequency;
    private readonly string _exceptionTypeTag;
    private readonly bool _emitExecutingEvent;

    private readonly KV[] _executingTags;
    private readonly KV[] _executedTags;
    // [attempt][0 = not handled, 1 = handled + more attempts left, 2 = handled + last attempt]
    private readonly KV[][][] _attemptTags;
    private readonly KV[] _retryTags, _timeoutTags, _fallbackTags, _openedTags, _closedTags, _halfOpenedTags, _rejectedTags;

    public PipelineTelemetry(
        string pipelineName,
        string pipelineInstance,
        StrategyNames strategies,
        int maxRetries,
        TelemetryNames? names = null,
        ILoggerFactory? loggerFactory = null,
        bool emitExecutingEvent = true)
    {
        names ??= TelemetryNames.Default;
        var instruments = ResilienceInstruments.For(names);
        _events = instruments.Events;
        _attemptDuration = instruments.AttemptDuration;
        _pipelineDuration = instruments.PipelineDuration;
        _activitySource = instruments.ActivitySource;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(names.LoggerCategory);
        _ticksToUnit = names.TicksToUnit;
        _exceptionTypeTag = names.ExceptionTypeTag;
        _emitExecutingEvent = emitExecutingEvent;
        PipelineName = pipelineName;
        PipelineInstance = pipelineInstance;
        Strategies = strategies;

        KV[] Pipeline(string ev, string severity) =>
            [new(Tags.EventName, ev), new(Tags.EventSeverity, severity), new(Tags.PipelineName, pipelineName), new(Tags.PipelineInstance, pipelineInstance)];
        KV[] Strategy(string ev, string severity, string strategy) =>
            [.. Pipeline(ev, severity), new(Tags.StrategyName, strategy)];

        _executingTags = Pipeline(Events.PipelineExecuting, Severity.Debug);
        _executedTags = Pipeline(Events.PipelineExecuted, Severity.Information);

        var prebuilt = Math.Min(maxRetries, MaxPrebuiltAttempts - 1) + 1;
        _attemptTags = new KV[prebuilt][][];
        for (var a = 0; a < prebuilt; a++)
        {
            KV[] Attempt(string severity, bool handled) =>
                [.. Strategy(Events.ExecutionAttempt, severity, strategies.Retry), new(Tags.AttemptNumber, Box(a)), new(Tags.AttemptHandled, handled ? s_true : s_false)];
            _attemptTags[a] = [Attempt(Severity.Information, false), Attempt(Severity.Warning, true), Attempt(Severity.Error, true)];
        }

        _retryTags = Strategy(Events.OnRetry, Severity.Warning, strategies.Retry);
        _timeoutTags = Strategy(Events.OnTimeout, Severity.Error, strategies.AttemptTimeout);
        _fallbackTags = Strategy(Events.OnFallback, Severity.Warning, strategies.Fallback);
        _openedTags = Strategy(Events.OnCircuitOpened, Severity.Error, strategies.CircuitBreaker);
        _closedTags = Strategy(Events.OnCircuitClosed, Severity.Information, strategies.CircuitBreaker);
        _halfOpenedTags = Strategy(Events.OnCircuitHalfOpened, Severity.Warning, strategies.CircuitBreaker);
        _rejectedTags = Strategy(Events.OnCircuitRejected, Severity.Debug, strategies.CircuitBreaker);
    }

    public string PipelineName { get; }
    public string PipelineInstance { get; }
    public StrategyNames Strategies { get; }

    private static object Box(int i) => (uint)i < (uint)s_boxedInts.Length ? s_boxedInts[i] : i;

    // ---------------------------------------------------------------- happy path (3 events, as Polly)

    /// <summary>Returns the start timestamp. Optional span per execution, only when a tracer listens.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long PipelineExecuting(out Activity? activity)
    {
        activity = _activitySource.HasListeners() ? StartActivity() : null;
        if (_emitExecutingEvent && _events.Enabled) _events.Add(1, _executingTags);
        Log.PipelineExecuting(_logger, PipelineName, PipelineInstance); // generated: IsEnabled(Debug) first
        return Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Activity? StartActivity()
    {
        var activity = _activitySource.StartActivity("resilience.pipeline", ActivityKind.Internal);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(Tags.PipelineName, PipelineName);
            activity.SetTag(Tags.PipelineInstance, PipelineInstance);
        }
        return activity;
    }

    public void PipelineExecuted(long startTimestamp, bool handled, Exception? exception, Activity? activity)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        if (_pipelineDuration.Enabled)
        {
            if (exception is null) _pipelineDuration.Record(elapsed * _ticksToUnit, _executedTags);
            else RecordWithException(_pipelineDuration, elapsed * _ticksToUnit, _executedTags, exception);
        }
        if (_logger.IsEnabled(LogLevel.Information))
            Log.PipelineExecuted(_logger, LogLevel.Information, PipelineName, PipelineInstance, handled, elapsed * _ticksToMs, exception);
        if (activity is not null) StopActivity(activity, handled, exception);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StopActivity(Activity activity, bool handled, Exception? exception)
    {
        if (handled || exception is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error);
            if (exception is not null) activity.SetTag(_exceptionTypeTag, exception.GetType().FullName);
        }
        activity.Dispose();
    }

    public void ExecutionAttempt(int attempt, bool handled, bool isLast, long attemptStartTimestamp, Exception? exception)
    {
        var elapsed = Stopwatch.GetTimestamp() - attemptStartTimestamp;
        var kind = !handled ? 0 : isLast ? 2 : 1;
        if (_attemptDuration.Enabled)
        {
            if (exception is null && attempt < _attemptTags.Length) _attemptDuration.Record(elapsed * _ticksToUnit, _attemptTags[attempt][kind]);
            else RecordAttemptSlow(elapsed * _ticksToUnit, attempt, kind, exception);
        }
        var level = kind switch { 0 => LogLevel.Information, 1 => LogLevel.Warning, _ => LogLevel.Error };
        if (_logger.IsEnabled(level))
            Log.ExecutionAttempt(_logger, level, PipelineName, PipelineInstance, Strategies.Retry, handled, attempt, elapsed * _ticksToMs, exception);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordAttemptSlow(double value, int attempt, int kind, Exception? exception)
    {
        // TagList keeps up to 8 tags inline: 7 + error.type still does not allocate.
        var tags = new TagList
        {
            { Tags.EventName, Events.ExecutionAttempt },
            { Tags.EventSeverity, kind switch { 0 => Severity.Information, 1 => Severity.Warning, _ => Severity.Error } },
            { Tags.PipelineName, PipelineName },
            { Tags.PipelineInstance, PipelineInstance },
            { Tags.StrategyName, Strategies.Retry },
            { Tags.AttemptNumber, Box(attempt) },
            { Tags.AttemptHandled, kind == 0 ? s_false : s_true },
        };
        if (exception is not null) tags.Add(_exceptionTypeTag, exception.GetType().FullName);
        _attemptDuration.Record(value, in tags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordWithException(Histogram<double> histogram, double value, KV[] prebuilt, Exception exception)
    {
        var tags = new TagList(prebuilt) { { _exceptionTypeTag, exception.GetType().FullName } };
        histogram.Record(value, in tags);
    }

    // ---------------------------------------------------------------- strategy events (failure paths)

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnRetry(int attempt, TimeSpan delay, Exception? exception)
    {
        Count(_retryTags, exception);
        Log.Retry(_logger, PipelineName, PipelineInstance, Strategies.Retry, attempt, delay.TotalMilliseconds, exception);
        AddActivityEvent(Events.OnRetry);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnTimeout(TimeSpan timeout)
    {
        Count(_timeoutTags, null);
        Log.Timeout(_logger, PipelineName, PipelineInstance, Strategies.AttemptTimeout, timeout.TotalMilliseconds);
        AddActivityEvent(Events.OnTimeout);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnFallback(Exception? exception)
    {
        Count(_fallbackTags, exception);
        Log.Fallback(_logger, PipelineName, PipelineInstance, Strategies.Fallback, exception);
        AddActivityEvent(Events.OnFallback);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnCircuitOpened(TimeSpan breakDuration)
    {
        Count(_openedTags, null);
        Log.CircuitOpened(_logger, PipelineName, PipelineInstance, Strategies.CircuitBreaker, breakDuration.TotalMilliseconds);
        AddActivityEvent(Events.OnCircuitOpened);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnCircuitClosed()
    {
        Count(_closedTags, null);
        Log.CircuitClosed(_logger, PipelineName, PipelineInstance, Strategies.CircuitBreaker);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnCircuitHalfOpened()
    {
        Count(_halfOpenedTags, null);
        Log.CircuitHalfOpened(_logger, PipelineName, PipelineInstance, Strategies.CircuitBreaker);
    }

    // Not NoInlining: while a circuit is open this IS the hot path.
    public void OnCircuitRejected()
    {
        if (_events.Enabled) _events.Add(1, _rejectedTags);
        Log.CircuitRejected(_logger, PipelineName, PipelineInstance, Strategies.CircuitBreaker);
    }

    private void Count(KV[] prebuilt, Exception? exception)
    {
        if (!_events.Enabled) return;
        if (exception is null) { _events.Add(1, prebuilt); return; }
        var tags = new TagList(prebuilt) { { _exceptionTypeTag, exception.GetType().FullName } };
        _events.Add(1, in tags);
    }

    private static void AddActivityEvent(string name)
    {
        if (Activity.Current is { IsAllDataRequested: true } current) current.AddEvent(new ActivityEvent(name));
    }
}

/// <summary>strategy.name per strategy instance. Generated pipelines get these from attribute Name = "…" or defaults.</summary>
public sealed record StrategyNames(string Fallback, string Timeout, string Retry, string CircuitBreaker, string AttemptTimeout)
{
    // Polly's defaults, except the second timeout gets a distinct name (Polly would call both "Timeout").
    public static readonly StrategyNames Default = new("Fallback", "Timeout", "Retry", "CircuitBreaker", "AttemptTimeout");
}
