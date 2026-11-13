using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MintPlayer.Resilience;
using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Hedging;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Timeout;

namespace ResilienceSamples;

// The pipelines the parity and allocation tests execute. Their hooks are internal and static (or instance
// methods of the DI form), so each test can build the equivalent runtime pipeline with the SAME hooks and compare
// attempt counts, outcomes and events line for line.

/// <summary>Records the events the hooks raise, in order. Thread-safe.</summary>
public sealed class EventJournal
{
    private readonly List<string> _events = [];

    /// <summary>The journal of the static-form sample pipelines.</summary>
    public static EventJournal Shared { get; } = new();

    public void Add(string entry)
    {
        lock (_events)
        {
            _events.Add(entry);
        }
    }

    /// <summary>Returns the events so far and clears the journal.</summary>
    public string[] Take()
    {
        lock (_events)
        {
            var events = _events.ToArray();
            _events.Clear();
            return events;
        }
    }
}

/// <summary>Total timeout, linear retry and a per-attempt timeout, with a predicate, a retry event and a (void) timeout event.</summary>
[ResiliencePipeline<int>]
[Timeout(TimeoutMs = 10_000, Name = "Total")]
[Retry(MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Linear, DelayMs = 100)]
[Timeout(TimeoutMs = 1_000, Name = "Attempt")]
public sealed partial class RetryTimeoutPipeline
{
    [RetryWhen]
    internal static bool Handle(Outcome<int> outcome)
        => outcome.Rejection == RejectionKind.Timeout || outcome.Exception is InvalidOperationException || (outcome.IsSuccess && outcome.Result < 0);

    [OnRetry]
    internal static ValueTask Retried(OnRetryArguments<int> args)
    {
        EventJournal.Shared.Add($"retry attempt={args.AttemptNumber} delay={args.RetryDelay.TotalMilliseconds}ms outcome={Describe(args.Outcome)}");
        return default;
    }

    [OnTimeout("Attempt")]
    internal static void AttemptTimedOut(OnTimeoutArguments args) => EventJournal.Shared.Add($"timeout {args.Timeout.TotalMilliseconds}ms");

    internal static string Describe(Outcome<int> outcome) => outcome.IsRejected
        ? $"rejected:{outcome.Rejection}"
        : outcome.IsSuccess ? $"result:{outcome.Result}" : $"exception:{outcome.Exception!.GetType().Name}";
}

/// <summary>Retry around a circuit breaker, with every breaker event.</summary>
[ResiliencePipeline<int>]
[Retry(MaxRetryAttempts = 2, DelayMs = 0, ShouldHandle = nameof(RetryOn))]
[CircuitBreaker(FailureRatio = 0.5, MinimumThroughput = 2, SamplingDurationMs = 10_000, BreakDurationMs = 5_000)]
public sealed partial class BreakerPipeline
{
    internal static bool RetryOn(RetryPredicateArguments<int> args) => args.Outcome.Exception is InvalidOperationException;

    [BreakWhen]
    internal static bool BreakOn(CircuitBreakerPredicateArguments<int> args) => args.Outcome.Exception is InvalidOperationException;

    [OnOpened]
    internal static ValueTask Opened(OnCircuitOpenedArguments<int> args)
    {
        EventJournal.Shared.Add($"opened break={args.BreakDuration.TotalMilliseconds}ms");
        return default;
    }

    [OnClosed]
    internal static ValueTask Closed(OnCircuitClosedArguments<int> args)
    {
        EventJournal.Shared.Add("closed");
        return default;
    }

    [OnHalfOpened]
    internal static ValueTask HalfOpened(OnCircuitHalfOpenedArguments args)
    {
        EventJournal.Shared.Add("half-opened");
        return default;
    }
}

/// <summary>A fallback around a retry, with a parameterless fallback action returning the result directly.</summary>
[ResiliencePipeline<int>]
[Fallback(OnFallback = nameof(Fell))]
[Retry(MaxRetryAttempts = 1, DelayMs = 0)]
public sealed partial class FallbackPipeline
{
    [FallbackWhen]
    internal static bool FallbackOn(Outcome<int> outcome) => !outcome.IsSuccess || outcome.Result < 0;

    [FallbackWith]
    internal static int Substitute() => 0;

    internal static ValueTask Fell(OnFallbackArguments<int> args)
    {
        EventJournal.Shared.Add($"fallback from {RetryTimeoutPipeline.Describe(args.Outcome)}");
        return default;
    }
}

/// <summary>A generic pipeline (any result type, and void), with generic hooks and a breaker shared by every result type.</summary>
[ResiliencePipeline]
[Retry(MaxRetryAttempts = 2, DelayMs = 0)]
[CircuitBreaker(FailureRatio = 1.0, MinimumThroughput = 2, BreakDurationMs = 5_000)]
[Timeout(TimeoutMs = 1_000)]
public sealed partial class GenericPipeline
{
    [OnRetry]
    internal static void Retried<TResult>(OnRetryArguments<TResult> args) => EventJournal.Shared.Add($"retry attempt={args.AttemptNumber}");

    [BreakWhen]
    internal static bool Breaks<TResult>(Outcome<TResult> outcome) => outcome.Exception is InvalidOperationException;
}

/// <summary>A reloadable pipeline (S6): values from the generated options class, one snapshot per execution.</summary>
[ResiliencePipeline<int>(Reloadable = true)]
[Retry(Name = "Retry", MaxRetryAttempts = 3, DelayMs = 0)]
[CircuitBreaker(Name = "Breaker", FailureRatio = 0.5, MinimumThroughput = 2, BreakDurationMs = 5_000)]
[Timeout(Name = "Attempt", TimeoutMs = 2_000)]
public sealed partial class ReloadablePipeline
{
}

/// <summary>Hedging: not flattened; the generated members forward to a runtime pipeline built once.</summary>
[ResiliencePipeline<int>]
[Timeout(TimeoutMs = 10_000)]
[Hedging(MaxHedgedAttempts = 1, DelayMs = 100)]
public sealed partial class HedgingPipeline
{
    [HedgeWhen]
    internal static bool HedgeOn(Outcome<int> outcome) => !outcome.IsSuccess || outcome.Result < 0;

    [OnHedging]
    internal static ValueTask Hedged(OnHedgingArguments<int> args)
    {
        EventJournal.Shared.Add($"hedging attempt={args.AttemptNumber}");
        return default;
    }
}

/// <summary>The DI form: instance hooks that use an injected service; the breaker state lives on the instance.</summary>
[ResiliencePipeline<int>]
[Retry(MaxRetryAttempts = 2, DelayMs = 0)]
[CircuitBreaker(FailureRatio = 0.5, MinimumThroughput = 2, SamplingDurationMs = 10_000, BreakDurationMs = 5_000)]
public sealed partial class InstanceBreakerPipeline(EventJournal journal)
{
    public EventJournal Journal => journal;

    [RetryWhen]
    internal bool Handle(Outcome<int> outcome) => outcome.Exception is InvalidOperationException;

    [BreakWhen]
    internal bool BreakOn(Outcome<int> outcome) => outcome.Exception is InvalidOperationException;

    [OnRetry]
    internal ValueTask Retried(OnRetryArguments<int> args)
    {
        journal.Add($"retry attempt={args.AttemptNumber}");
        return default;
    }

    [OnOpened]
    internal void Opened(OnCircuitOpenedArguments<int> args) => journal.Add("opened");
}

/// <summary>The S1 benchmark shape, for the allocation tests: fallback, total timeout, jittered retry, breaker, attempt timeout.</summary>
[ResiliencePipeline<int>]
[Fallback]
[Timeout(TimeoutMs = 10_000)]
[Retry(MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, UseJitter = true, DelayMs = 100)]
[CircuitBreaker]
[Timeout(TimeoutMs = 2_000)]
public sealed partial class AllocationPipeline
{
    [RetryWhen, BreakWhen]
    internal static bool Handle(Outcome<int> outcome) => outcome.Exception is InvalidOperationException || (outcome.IsSuccess && outcome.Result == -1);

    [FallbackWhen]
    internal static bool FallbackOn(Outcome<int> outcome) => Handle(outcome);

    [FallbackWith]
    internal static ValueTask<Outcome<int>> Zero(FallbackActionArguments<int> args) => Outcome.FromResultAsValueTask(0);
}

/// <summary>Retry and fallback without a breaker, for the allocation tests of the retry and fallback paths (a breaker would open on them).</summary>
[ResiliencePipeline<int>]
[Fallback]
[Retry(MaxRetryAttempts = 3, DelayMs = 0)]
[Timeout(TimeoutMs = 2_000)]
public sealed partial class RetryFallbackPipeline
{
    [RetryWhen, FallbackWhen]
    internal static bool Handle(Outcome<int> outcome) => outcome.IsSuccess && outcome.Result == -1;

    [FallbackWith]
    internal static int Zero() => 0;
}

/// <summary>The same shape without pooling (S2 opt-out): a suspended execution returns a Task-backed ValueTask.</summary>
[ResiliencePipeline<int>(PooledAsync = false)]
[Retry(MaxRetryAttempts = 1, DelayMs = 0)]
public sealed partial class UnpooledPipeline
{
}

/// <summary>No strategies at all: only the callback runs.</summary>
[ResiliencePipeline<int>]
public sealed partial class EmptyPipeline
{
}
