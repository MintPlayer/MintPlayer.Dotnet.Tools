# S7 snippet (b): the HTTP standard handler and `AddResilienceHandler` with custom options

The target code is illustrative. Microsoft's code is typed on Polly, so the migration here is a package
swap (`Microsoft.Extensions.Http.Resilience` → `MintPlayer.Resilience.Http`) plus the same option names.

## Source 1: standard handler, customised

From learn.microsoft.com, "Build resilient HTTP apps" (`docs/core/resilience/http-resilience.md`,
commit 156931b), the `DisableForUnsafeHttpMethods` section, with the most commonly tuned knobs added.

```csharp
services.AddHttpClient<GitHubClient>(c => c.BaseAddress = new("https://api.github.com"))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 5;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);   // must stay ≥ 2 × attempt timeout
    });
```

## Source 2: custom handler

Verbatim from the same page, section "Add custom resilience handlers".

```csharp
httpClientBuilder.AddResilienceHandler(
    "CustomPipeline",
    static builder =>
{
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        BackoffType = DelayBackoffType.Exponential,
        MaxRetryAttempts = 5,
        UseJitter = true
    });

    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(10),
        FailureRatio = 0.2,
        MinimumThroughput = 3,
        ShouldHandle = static args =>
        {
            return ValueTask.FromResult(args is
            {
                Outcome.Result.StatusCode:
                    HttpStatusCode.RequestTimeout or
                        HttpStatusCode.TooManyRequests
            });
        }
    });

    builder.AddTimeout(TimeSpan.FromSeconds(5));
});
```

## Target 1: standard handler

Runtime (same source, different `using`):

```csharp
services.AddHttpClient<GitHubClient>(c => c.BaseAddress = new("https://api.github.com"))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 5;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
    });
```

Declarative (the preset spelled out, so it is generated and flat; `[Http*]` attributes carry the
Microsoft defaults from RESULTS §1, so only the overrides are written):

```csharp
[ResiliencePipeline<HttpResponseMessage>]
[HttpRateLimiter]                                        // concurrency 1000, queue 0
[HttpTimeout(TimeoutMs = 30_000)]                        // total
[HttpRetry(MaxRetryAttempts = 5, DelayMs = 1_000, DisableForUnsafeHttpMethods = true)]
[HttpCircuitBreaker(SamplingDurationMs = 10_000)]
[HttpTimeout(TimeoutMs = 5_000)]                         // attempt
public static partial class GitHubPipeline;

services.AddHttpClient<GitHubClient>(c => c.BaseAddress = new("https://api.github.com"))
    .AddResilienceHandler<GitHubPipeline>();             // also sets HttpClient.Timeout = Infinite, like Microsoft
```

MPR0004 enforces Microsoft's two cross-option validations at compile time: the attempt timeout must be
≤ the total timeout, and the sampling duration must be ≥ 2 × the attempt timeout.

## Target 2: custom handler

Runtime:

```csharp
httpClientBuilder.AddResilienceHandler("CustomPipeline", static builder =>
{
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        BackoffType = DelayBackoffType.Exponential,
        MaxRetryAttempts = 5,
        UseJitter = true
    });
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(10),
        FailureRatio = 0.2,
        MinimumThroughput = 3,
        ShouldHandle = static args => args is                 // bool, not ValueTask<bool>
        {
            Outcome.Result.StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        }
    });
    builder.AddTimeout(TimeSpan.FromSeconds(5));
});
```

Declarative (what MPR0001's code fix produces; every value is constant and the predicate is a static
lambda over `args` only):

```csharp
[ResiliencePipeline<HttpResponseMessage>]
[HttpRetry(BackoffType = DelayBackoffType.Exponential, MaxRetryAttempts = 5, UseJitter = true)]
[HttpCircuitBreaker(SamplingDurationMs = 10_000, FailureRatio = 0.2, MinimumThroughput = 3,
                    ShouldHandle = nameof(CircuitBreakerShouldHandle))]
[Timeout(TimeoutMs = 5_000)]
public static partial class CustomPipeline
{
    private static bool CircuitBreakerShouldHandle(CircuitBreakerPredicateArguments<HttpResponseMessage> args) => args is
    {
        Outcome.Result.StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
    };
}

httpClientBuilder.AddResilienceHandler<CustomPipeline>();
```

## Differences

| # | Microsoft / Polly | MintPlayer.Resilience | Kind |
|---|---|---|---|
| b1 | Package `Microsoft.Extensions.Http.Resilience`, namespace `Microsoft.Extensions.Http.Resilience` | `MintPlayer.Resilience.Http`; same type names (`HttpStandardResilienceOptions`, `HttpRetryStrategyOptions`, …) | namespace |
| b2 | `AddResilienceHandler(string name, Action<ResiliencePipelineBuilder<HttpResponseMessage>>)` | same overload, plus `AddResilienceHandler<TPipeline>()` | addition |
| b3 | `ShouldHandle` returns `ValueTask.FromResult(expr)` | returns `expr` (the code fix unwraps `ValueTask.FromResult`, `new ValueTask<bool>(…)`, `new(…)`) | signature |
| b4 | `TimeSpan` option values | attribute `…Ms` ints; builder unchanged | name (declarative only) |
| b5 | Validation at `ValidateOnStart` (runtime) | declarative: MPR0004 at compile time; runtime: same validation at build | timing |
| b6 | The standard handler re-sends the **same** `HttpRequestMessage` on retry (no clone) | same, to keep parity. The PRD's "clones and buffers" claim is wrong (RESULTS §2) | semantic |
| b7 | `DisableForUnsafeHttpMethods()` is `[Experimental]` | a plain, stable member; attribute flag `DisableForUnsafeHttpMethods = true` | name |
