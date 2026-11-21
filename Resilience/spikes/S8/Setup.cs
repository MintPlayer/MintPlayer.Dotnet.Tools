using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;
using Polly.Telemetry;
using Polly.Timeout;
using S1;

namespace S8;

/// <summary>What is listening. Applied identically to our pipeline and to Polly's.</summary>
public enum Sink
{
    /// <summary>Telemetry configured, nobody listening: instruments disabled, NullLogger.</summary>
    None,
    /// <summary>A MeterListener with no-op callbacks on every instrument (Polly's own TelemetryBenchmark setup).</summary>
    MeterNoOp,
    /// <summary>The OpenTelemetry SDK MeterProvider (real aggregation: tag lookup, histogram buckets), exporter never fires.</summary>
    OTel,
    /// <summary>MeterNoOp + a LoggerFactory at Information whose provider discards (the pipeline's own logging cost).</summary>
    MeterNoOpLogInfo,
    /// <summary>MeterNoOp + an ActivityListener sampling AllData on our ActivitySource (Polly has no tracing).</summary>
    MeterNoOpTracing,
}

public static class Sinks
{
    public static readonly string[] Meters = [TelemetryNames.Default.MeterName, "Polly"];

    public static ILoggerFactory LoggerFactoryFor(Sink sink) => sink == Sink.MeterNoOpLogInfo
        ? LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new DiscardingLoggerProvider()))
        : NullLoggerFactory.Instance;

    /// <summary>Attach the listeners for <paramref name="sink"/>; dispose to detach (instruments go back to Enabled = false).</summary>
    public static IDisposable Attach(Sink sink)
    {
        var parts = new List<IDisposable>();
        if (sink is Sink.MeterNoOp or Sink.MeterNoOpLogInfo or Sink.MeterNoOpTracing) parts.Add(NoOpMeterListener());
        if (sink is Sink.OTel)
        {
            parts.Add(Sdk.CreateMeterProviderBuilder()
                .AddMeter(Meters)
                .AddReader(new PeriodicExportingMetricReader(new DiscardingExporter(), exportIntervalMilliseconds: 3_600_000))
                .Build());
        }
        if (sink is Sink.MeterNoOpTracing)
        {
            var listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == TelemetryNames.Default.MeterName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(listener);
            parts.Add(listener);
        }
        return new Composite(parts);
    }

    private static MeterListener NoOpMeterListener()
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (Meters.Contains(instrument.Meter.Name)) l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<int>(static (_, _, _, _) => { });
        listener.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
        listener.Start();
        return listener;
    }

    private sealed class Composite(List<IDisposable> parts) : IDisposable
    {
        public void Dispose() { foreach (var p in parts) p.Dispose(); }
    }

    private sealed class DiscardingExporter : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch) => ExportResult.Success;
    }
}

/// <summary>Enabled at every level, writes nothing: isolates what the pipeline pays, not what a sink pays.</summary>
public sealed class DiscardingLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => Logger.Instance;
    public void Dispose() { }

    private sealed class Logger : ILogger
    {
        public static readonly Logger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}

public static class Pipelines
{
    public const string PipelineName = "catalog";
    public const string InstanceName = "default";

    public static PipelineTelemetry OurTelemetry(ILoggerFactory loggerFactory, bool emitExecutingEvent = true, TelemetryNames? names = null) =>
        new(PipelineName, InstanceName, StrategyNames.Default, Config.MaxRetries, names, loggerFactory, emitExecutingEvent);

    /// <summary>S1's Polly pipeline, named like ours, optionally with Polly.Extensions telemetry.</summary>
    public static ResiliencePipeline<int> Polly(ILoggerFactory? telemetryLoggerFactory)
    {
        static ValueTask<bool> Handle(Polly.Outcome<int> o) => new(o.Exception is not null || o.Result == -1);

        var builder = new ResiliencePipelineBuilder<int> { Name = PipelineName, InstanceName = InstanceName }
            .AddFallback(new FallbackStrategyOptions<int>
            {
                ShouldHandle = static args => Handle(args.Outcome),
                FallbackAction = static _ => Outcome.FromResultAsValueTask(0),
            })
            .AddTimeout(Config.OuterTimeout)
            .AddRetry(new RetryStrategyOptions<int>
            {
                ShouldHandle = static args => Handle(args.Outcome),
                MaxRetryAttempts = Config.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = Config.RetryDelay,
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
            {
                ShouldHandle = static args => Handle(args.Outcome),
                FailureRatio = Config.FailureRatio,
                MinimumThroughput = Config.MinimumThroughput,
                SamplingDuration = Config.Sampling,
                BreakDuration = Config.Break,
            })
            .AddTimeout(new TimeoutStrategyOptions { Timeout = Config.InnerTimeout, Name = "AttemptTimeout" });

        if (telemetryLoggerFactory is not null)
            builder.ConfigureTelemetry(new TelemetryOptions { LoggerFactory = telemetryLoggerFactory });

        return builder.Build();
    }
}
