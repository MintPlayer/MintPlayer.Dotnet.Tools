# S7 snippet (a): plain retry, exponential backoff, OnRetry logging

The target code is illustrative; the library does not exist yet. Names follow the recommendations in
[RESULTS.md](RESULTS.md) §4 (mirror Polly names where it costs nothing).

## Polly v8 (source)

Assembled from `C:\Repos\polly\docs\strategies\retry.md` L32-39 (`optionsComplex`) and
`C:\Repos\polly\README.md` L200-210 (`optionsOnRetry`), with the logger that real code puts in `OnRetry`.

```csharp
public sealed class DownloadService(HttpClient client, ILogger<DownloadService> logger)
{
    private readonly ResiliencePipeline _pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>(),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            MaxRetryAttempts = 4,
            Delay = TimeSpan.FromSeconds(3),
            OnRetry = args =>
            {
                logger.LogWarning(args.Outcome.Exception,
                    "Retry {Attempt} in {Delay}", args.AttemptNumber, args.RetryDelay);
                return default;
            },
        })
        .Build();

    public ValueTask<string> GetAsync(string url, CancellationToken ct) =>
        _pipeline.ExecuteAsync(async token => await client.GetStringAsync(url, token), ct);
}
```

## Target: declarative (`[ResiliencePipeline]`, PRD §2.1)

```csharp
[ResiliencePipeline]
[Retry(MaxRetryAttempts = 4, BackoffType = DelayBackoffType.Exponential, UseJitter = true, DelayMs = 3_000,
       ShouldHandle = nameof(Transient), OnRetry = nameof(LogRetry))]
public sealed partial class DownloadPipeline(ILogger<DownloadPipeline> logger)   // non-static: see RESULTS §5.3
{
    // Kept verbatim from Polly; the generator reads the initializer and folds it to
    // `ex is HttpRequestException or TimeoutRejectedException`. The field itself is never evaluated.
    private static readonly PredicateBuilder Transient = new PredicateBuilder()
        .Handle<HttpRequestException>()
        .Handle<TimeoutRejectedException>();

    private ValueTask LogRetry<T>(OnRetryArguments<T> args)
    {
        logger.LogWarning(args.Outcome.Exception, "Retry {Attempt} in {Delay}", args.AttemptNumber, args.RetryDelay);
        return default;
    }
}

// DI: services.AddResiliencePipeline<DownloadPipeline>();
public sealed class DownloadService(HttpClient client, DownloadPipeline pipeline)
{
    public ValueTask<string> GetAsync(string url, CancellationToken ct) =>
        pipeline.ExecuteAsync(async token => await client.GetStringAsync(url, token), ct); // closure → intercepted, 0 B
}
```

## Target: runtime builder (PRD §2.2)

```csharp
private readonly ResiliencePipeline _pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>(),               // implicit → Func<RetryPredicateArguments<object>, bool>
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        MaxRetryAttempts = 4,
        Delay = TimeSpan.FromSeconds(3),
        OnRetry = args =>
        {
            logger.LogWarning(args.Outcome.Exception, "Retry {Attempt} in {Delay}", args.AttemptNumber, args.RetryDelay);
            return default;
        },
    })
    .Build();
```

Identical source to Polly, apart from the `using`. MPR0002 fires on the capturing `ExecuteAsync` lambda.

## Differences

| # | Polly | MintPlayer.Resilience | Kind |
|---|---|---|---|
| a1 | `Delay = TimeSpan.FromSeconds(3)` | attribute: `DelayMs = 3_000` (attributes cannot hold `TimeSpan`); builder: unchanged | name (declarative only) |
| a2 | `ShouldHandle` is `Func<RetryPredicateArguments<T>, ValueTask<bool>>` | `Func<RetryPredicateArguments<T>, bool>`; `PredicateBuilder` converts to both | signature |
| a3 | Non-generic pipeline: `ExecuteAsync<TResult>` generic per call | declarative: generator emits a generic `ExecuteAsync<TResult>` for an untyped pipeline, and a closed method for `[ResiliencePipeline<T>]` | shape |
| a4 | `OnRetry` is a lambda capturing `logger` | declarative: an instance method referenced by `nameof`, the logger comes from the constructor | shape |
| a5 | `OnRetryArguments<T>`: `Context, Outcome, AttemptNumber (0-based), RetryDelay, Duration` | same struct, same members, same 0-based numbering | none |
| a6 | `ResiliencePipeline.ExecuteAsync` returns an unpooled `ValueTask` | pooled `ValueTask` (S2): awaiting twice now throws, MPR0006 | semantic |
