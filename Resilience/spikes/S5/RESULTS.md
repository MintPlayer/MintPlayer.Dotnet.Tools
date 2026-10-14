# Spike S5: rejection without throwing

Issue #189. This is throwaway code (`Resilience/spikes/S5`) and is deleted before the PR is finalized.
The run was .NET 11.0.0-rc.1, 8 cores, Release, Polly.Core / Polly.RateLimiting 8.8.0.

- `dotnet run -c Release` runs the allocation tables and the safety experiments E1–E8.
- `dotnet run -c Release -- --bench` runs the BDN class `RejectionBenchmarks` (timing plus
  `[MemoryDiagnoser]`). **It has not been run yet.** Other spikes were running in parallel, so every
  ns figure below is indicative only.

The pipeline is in `Pipeline.cs`: a breaker check, then a BCL limiter `AttemptAcquire`, then a pooled
timeout CTS. Its methods use `PoolingAsyncValueTaskMethodBuilder`. The three scenarios are:
- a circuit tripped for 1 h;
- a `ConcurrencyLimiter` (PermitLimit 1, QueueLimit 0) whose one permit is held;
- a `FixedWindowRateLimiter` (PermitLimit 1, 1 h window) whose permit is used up.

Polly gets the same limiter configuration. Its circuit is opened by real failures.

## 1. Bytes per rejected call

`GC.GetTotalAllocatedBytes(precise: true)` over 200,000 calls, after 5,000 warm-up calls.

| Scenario | Variant | B/op | ns/op (indicative) |
|---|---|---:|---:|
| Circuit open | **Ours `TryExecuteAsync` → `Outcome`** | **0** | 212–232 |
| Circuit open | Ours `ExecuteAsync`, fresh exception | 740–757 | ~11,000 |
| Circuit open | Ours `ExecuteAsync`, cached exception thrown | 552 | ~8,000–9,000 |
| Circuit open | Polly `ExecuteOutcomeAsync` (pooled context) | 200 | ~1,400 |
| Circuit open | Polly `ExecuteAsync` (throws `BrokenCircuitException`) | 1,266 | ~10,000–14,000 |
| Concurrency limited | **Ours `TryExecuteAsync`** | **0** | 88–189 |
| Concurrency limited | Ours `ExecuteAsync`, fresh | 688 | ~7,000–10,000 |
| Concurrency limited | Ours `ExecuteAsync`, cached thrown | 552 | ~9,000–11,000 |
| Concurrency limited | Polly `ExecuteOutcomeAsync` | 17,300 | ~53,000–67,000 |
| Concurrency limited | Polly `ExecuteAsync` (throws `RateLimiterRejectedException`) | 24,370 | ~98,000–116,000 |
| Fixed window limited | **Ours `TryExecuteAsync`** | **64** (all BCL, see below) | 156–263 |
| Fixed window limited | Ours `ExecuteAsync`, fresh | 752 | ~8,000 |
| Fixed window limited | Ours `ExecuteAsync`, cached thrown | 616 | ~8,000 |
| Fixed window limited | Polly `ExecuteOutcomeAsync` | 17,400 | ~53,000–62,000 |
| Fixed window limited | Polly `ExecuteAsync` (throws) | 24,690 | ~117,000–140,000 |

Where the bytes come from:

| Measurement | B/op |
|---|---:|
| Bare sync `throw` of a cached instance | 176 (the runtime allocates a new stack-trace array on every throw) |
| Bare sync `throw new CircuitOpenException()` | 312 |
| Bare sync `throw new StacklessRejection()` (StackTrace overridden) | 296: this saves nothing |
| `new CircuitOpenException()`, not thrown | 136 (about 45 ns) |
| `ExceptionDispatchInfo.SetCurrentStackTrace(new Exception())` | **10,029** (about 26 µs) |
| `FixedWindowRateLimiter.AttemptAcquire` failed lease | 40 (a new `FixedWindowLease` each time) |
| … plus `TryGetMetadata(MetadataName.RetryAfter)` | +24 (boxes the `TimeSpan`) |
| `ConcurrencyLimiter` failed lease (`AttemptAcquire` or `AcquireAsync`) | 0 (a cached lease) |

- **The 17–25 KB in Polly's rate-limiter rejection comes from Polly, not the BCL.** In
  `RateLimiterResilienceStrategy.cs:77`, Polly calls `exception.TrySetStackTrace()`, which runs
  `ExceptionDispatchInfo.SetCurrentStackTrace`. That is a full stack walk with file and line
  resolution, run on every rejection. The circuit breaker does not call it, which is why its figure
  (1.27 KB) matches the PRD's.
- **The 64 B on our fixed-window path is all BCL.** Getting to 0 B needs our own window counter in
  the generated code; S4 is building a lock-free core anyway. Without that, the floor is 40 B if
  `RetryAfter` is left unread.
- **Throwing costs about 4–11 µs whether or not the instance is cached.** A cached throw saves only the
  136–200 B exception object, out of 550–760 B per async rejection. The rest is the stack-trace array,
  the EDI capture in the async builder and the rethrow's trace. The time is set by the unwind itself.

## 2. Is it safe to throw a cached exception? No. Evidence (`Safety.cs`)

- **E6: a throw mutates the instance.** Throwing an existing instance rewrites `_stackTrace`, and the
  first throw also writes `_ipForWatsonBuckets`. Reading `TargetSite` or `Source` caches
  `_exceptionMethod` and `_source`. `EDI.Capture(ex).Throw()` rewrites `_stackTrace` as well.
- **E1: stale TargetSite.** After the instance is rethrown from `ThrowFromBeta`, `StackTrace` shows
  Beta but `TargetSite` still reports `ThrowFromAlpha`, the value cached on first read.
- **E2: logs show another request's stack.** Request 1 catches the cached exception. Request 2, on
  another thread, is then rejected. Request 1's `ILogger.LogError(ex, …)` output shows only
  `RequestTwo`'s frames. Every MEL formatter prints `exception.ToString()`, so all of them are affected.
  With a fresh exception per throw, each request logs its own stack.
- **E3: concurrent throws** (8 threads × 50k, the cached instance, read right after the catch):
  - about 11% of catches show only the other thread's stack;
  - about 22% show both stacks mixed;
  - about 7–12% show an empty stack.

  The same pattern holds on the async `ExecuteAsync` EDI path. There were no read faults and no
  crash: the runtime keeps memory safe, but the data is wrong. With fresh exceptions, there were 0
  errors of any kind.
- **E4: `Data` leaks and corrupts.**
  - A value one caller adds (`Data["RequestId"]="req-1"`) is visible to the next, unrelated caller.
  - `ListDictionaryInternal` is not thread-safe: 8 threads doing add/remove raised 1–451 exceptions,
    and 100–16,351 entries were left over where 0 were expected. That is an unbounded leak of user
    data on a static instance.
- **E5: repeated EDI rethrow grows without limit.** Each `ExceptionDispatchInfo.Capture(sameEx).Throw()`
  appends to the trace: 103 → 1.5 K → 15.5 K → 156 K chars after 1, 10, 100 and 1,000 throws. That
  is a memory leak and O(n) work per throw. Plain `throw ex` and the async-builder path stayed flat
  (223 and 225 chars).
- **E7: a "stackless" type only hides the problem.** Overriding `StackTrace` to return null does hide
  the trace from `ToString()`. But the runtime still captures it (`new StackTrace(ex).FrameCount = 2`),
  still writes the fields, and still charges the full throw cost (296 B, about 4 µs). It also removes
  the stack from diagnostics for the one call that does throw.
- **E8: the never-thrown sentinel is clean until a user throws it.** After 10k rejections through
  `TryExecuteAsync`, the sentinel's `StackTrace` is still null and `Data.Count` is still 0.
  - After **one** user `throw outcome.Exception;`, every later `Outcome.Exception` carries that
    user's stack.
  - `GetResultOrThrow()` throws a fresh instance, not the sentinel, so it is safe.

## 3. Recommendation

1. **`TryExecuteAsync` never throws on our own rejections.** It returns
   `Outcome<T>.Rejected(kind, retryAfter)`. The rejection is stored as an **enum plus a `long`
   RetryAfter**, not as an exception reference: 0 B, and nothing is shared or mutable.
2. **`ExecuteAsync` throws a fresh exception per rejection**, with `InnerException` set to the
   breaking exception, as Polly does. Do not cache and do not use stackless types. The allocation is
   about 0.2 KB of a 0.7 KB, 4–11 µs throw, and it buys correct logs, `Data` and `TargetSite`. It is
   still 1.7× lighter than Polly on an open circuit and about 33× lighter on rate-limited calls.
   Anyone who cares about rejection cost should use `TryExecuteAsync`, which is where the
   ≤ 250 ns / 0 B target lives.
3. **`Outcome<T>.Exception` for a rejection:**
   - The measured variant returns a never-thrown cached sentinel (0 B, safe per E8).
   - The recommended shape is **fresh on access** (136 B, about 45 ns, only when someone reads it).
     This removes the E8 "user rethrows the sentinel" hazard and the shared-`Data` hazard by design,
     not by documentation.
   - The zero-cost path is the enum.
   - Add an analyzer and code fix that rewrites `o.Exception is CircuitOpenException` to
     `o.Rejection == RejectionKind.CircuitOpen`. This is Polly migration sugar.
4. **Do not use `ExceptionDispatchInfo.Capture` on rejections** (E5). Keep the PRD §2.4 point that
   we skip EDI, but drop "cached, stackless exception instance": there is no instance at all on the
   Try path.

Proposed public shape:

```csharp
public enum RejectionKind : byte { None, CircuitOpen, CircuitIsolated, RateLimited, Timeout /*, Bulkhead*/ }

public readonly struct Outcome<T>          // sizeof(T) + 24 B
{
    public bool IsSuccess { get; }         // no exception, not rejected
    public bool IsRejected { get; }        // Rejection != None
    public RejectionKind Rejection { get; }
    public TimeSpan? RetryAfter { get; }   // from breaker / limiter lease
    public T Result { get; }
    public Exception? Exception { get; }   // user callback exception; for a rejection: fresh on access
    public T GetResultOrThrow();           // fresh ResilienceRejectedException subtype, or EDI-rethrow of user exception
}
// Throwing types: abstract ResilienceRejectedException { Kind, RetryAfter }
//   ← CircuitOpenException (InnerException = breaking exception), RateLimitedException, ResilienceTimeoutException
```

User code:
- `if (o.IsRejected) return Results.StatusCode(503);`
- `switch (o.Rejection) { case RejectionKind.RateLimited: … }`
- `o is { Rejection: RejectionKind.CircuitOpen, RetryAfter: { } ra }`

## 4. Pass / fail against the plan (S5 row)

| Criterion | Result |
|---|---|
| ≤ 250 ns / 0 B on `TryExecuteAsync`, open circuit | **0 B: PASS.** Time 212–232 ns by Stopwatch on a loaded machine; the BDN run is pending. S4's lock-free breaker takes out the `lock` + `GetUtcNow`. |
| Same, rate limited | Concurrency limiter: **0 B, PASS**. Fixed window: **64 B from the BCL, FAIL on bytes.** 0 B needs our own window counter (40 B if `RetryAfter` is not read). |
| Settle the cached-exception safety question | **Settled: unsafe.** It gives foreign or mixed stacks under concurrency, stale `TargetSite`, leaking and corrupted `Data`, and unbounded growth under repeated EDI rethrow. Allocate per throw (+0.2 KB, no time cost) and accept it. |
| Timeout rejection | Returned as `RejectionKind.Timeout` with no exception of our own. Not in the bytes table: the OCE comes from the user callback, and a fired CTS cannot be `TryReset`, so both libraries pay for a new CTS per timeout whatever the exception design. |
