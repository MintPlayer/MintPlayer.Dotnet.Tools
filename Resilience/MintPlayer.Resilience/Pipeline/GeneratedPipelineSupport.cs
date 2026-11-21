using System.ComponentModel;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Timeout;

namespace MintPlayer.Resilience.Pipeline;

/// <summary>
/// The runtime pieces that code generated for a <c>[ResiliencePipeline]</c> class calls: the retry backoff
/// arithmetic, delays, the default predicate, discarded-result disposal and the argument structs bound to
/// an <see cref="ExecutionFrame"/>. They are the exact code the interpreter's strategies run, so a generated
/// pipeline and the equivalent runtime pipeline behave identically.
/// </summary>
/// <remarks>Public only for generated pipelines; not intended for direct use.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedPipelineSupport
{
    private static readonly Action<object?> CancelSource = static state => ((CancellationTokenSource)state!).Cancel();

    /// <summary>The predicate strategies use when none is set: every exception except a cancellation, and every rejection.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="outcome">The outcome.</param>
    /// <returns>Whether the outcome is handled.</returns>
    public static bool HandleByDefault<T>(in Outcome<T> outcome) => DefaultPredicates.HandleOutcome(outcome);

    /// <summary>Gets the exception of a non-rejected outcome (the callback's own), without materializing a rejection exception.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The exception, or <see langword="null"/> for a result or a rejection.</returns>
    public static Exception? RawException<T>(in Outcome<T> outcome) => outcome.RawException;

    /// <summary>Creates the <see cref="RejectionKind.Timeout"/> outcome of a timeout that fired.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="timeout">The timeout that elapsed.</param>
    /// <param name="cause">The cancellation the timeout provoked.</param>
    /// <returns>The rejection.</returns>
    public static Outcome<T> TimeoutRejection<T>(TimeSpan timeout, OperationCanceledException cause)
        => Outcome.Rejected<T>(RejectionKind.Timeout, timeout.Ticks, cause);

    /// <summary>Links a timeout's source to the token it replaces, so a caller cancellation cancels the attempt.</summary>
    /// <param name="previous">The token the timeout replaces.</param>
    /// <param name="source">The timeout's source.</param>
    /// <returns>The registration, to dispose when the timeout's scope ends.</returns>
    public static CancellationTokenRegistration Link(CancellationToken previous, CancellationTokenSource source)
        => previous.CanBeCanceled ? previous.UnsafeRegister(CancelSource, source) : default;

    /// <summary>The delay before the next retry (Polly's backoff and jitter formulas).</summary>
    /// <param name="type">The backoff type.</param>
    /// <param name="jitter">Whether jitter is applied.</param>
    /// <param name="attempt">The 0-based attempt that just failed.</param>
    /// <param name="baseDelay">The base delay.</param>
    /// <param name="maxDelay">The cap on the computed delay.</param>
    /// <param name="state">Decorrelated-jitter state, starting at 0 for each execution.</param>
    /// <param name="randomizer">Returns a value in [0, 1).</param>
    /// <returns>The delay.</returns>
    public static TimeSpan GetRetryDelay(DelayBackoffType type, bool jitter, int attempt, TimeSpan baseDelay, TimeSpan? maxDelay, ref double state, Func<double> randomizer)
        => RetryHelper.GetRetryDelay(type, jitter, attempt, baseDelay, maxDelay, ref state, randomizer);

    /// <summary>Whether a delay returned by a delay generator is used (zero or positive).</summary>
    /// <param name="delay">The generated delay.</param>
    /// <returns>Whether it replaces the computed delay.</returns>
    public static bool IsValidDelay(TimeSpan delay) => RetryHelper.IsValidDelay(delay);

    /// <summary>Waits on <paramref name="timeProvider"/> with the frame's current token; blocks for a synchronous execution.</summary>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="delay">The delay.</param>
    /// <param name="frame">The execution.</param>
    /// <returns>A task that completes after the delay.</returns>
    public static ValueTask DelayAsync(TimeProvider timeProvider, TimeSpan delay, ExecutionFrame frame) => DelayHelper.DelayAsync(timeProvider, delay, frame);

    /// <summary>Disposes the result of an outcome that is being discarded (a retried response), as retry does. Value types are skipped.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="outcome">The discarded outcome.</param>
    /// <param name="isSynchronous">Whether this is a synchronous execution.</param>
    /// <returns>A task that completes when the result is disposed; failures are swallowed.</returns>
    public static ValueTask DisposeDiscardedAsync<T>(in Outcome<T> outcome, bool isSynchronous)
    {
        if (!typeof(T).IsValueType && outcome.TryGetResult(out var result))
        {
            return DisposeHelper.TryDisposeSafeAsync(result, isSynchronous);
        }

        return default;
    }

    /// <summary>Creates the arguments of a retry predicate.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="frame">The execution.</param>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <param name="attemptNumber">The 0-based attempt.</param>
    /// <returns>The arguments.</returns>
    public static RetryPredicateArguments<T> RetryPredicate<T>(ExecutionFrame frame, in Outcome<T> outcome, int attemptNumber) => new(frame, outcome, attemptNumber);

    /// <summary>Creates the arguments of a retry delay generator.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="frame">The execution.</param>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <param name="attemptNumber">The 0-based attempt.</param>
    /// <returns>The arguments.</returns>
    public static RetryDelayGeneratorArguments<T> RetryDelayGenerator<T>(ExecutionFrame frame, in Outcome<T> outcome, int attemptNumber) => new(frame, outcome, attemptNumber);

    /// <summary>Creates the arguments of <c>OnRetry</c>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="frame">The execution.</param>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <param name="attemptNumber">The 0-based attempt.</param>
    /// <param name="retryDelay">The delay before the next attempt.</param>
    /// <param name="duration">The duration of the attempt.</param>
    /// <returns>The arguments.</returns>
    public static OnRetryArguments<T> OnRetry<T>(ExecutionFrame frame, in Outcome<T> outcome, int attemptNumber, TimeSpan retryDelay, TimeSpan duration)
        => new(frame, outcome, attemptNumber, retryDelay, duration);

    /// <summary>Creates the arguments of <c>OnBudgetExhausted</c>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="frame">The execution.</param>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <param name="attemptNumber">The 0-based attempt.</param>
    /// <returns>The arguments.</returns>
    public static OnRetryBudgetExhaustedArguments<T> OnRetryBudgetExhausted<T>(ExecutionFrame frame, in Outcome<T> outcome, int attemptNumber) => new(frame, outcome, attemptNumber);

    /// <summary>Creates the arguments of a timeout generator.</summary>
    /// <param name="frame">The execution.</param>
    /// <returns>The arguments.</returns>
    public static TimeoutGeneratorArguments TimeoutGenerator(ExecutionFrame frame) => new(frame);

    /// <summary>Creates the arguments of <c>OnTimeout</c>.</summary>
    /// <param name="frame">The execution.</param>
    /// <param name="timeout">The timeout that elapsed.</param>
    /// <returns>The arguments.</returns>
    public static OnTimeoutArguments OnTimeout(ExecutionFrame frame, TimeSpan timeout) => new(frame, timeout);

    /// <summary>Creates the arguments of a fallback predicate.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="frame">The execution.</param>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The arguments.</returns>
    public static FallbackPredicateArguments<T> FallbackPredicate<T>(ExecutionFrame frame, in Outcome<T> outcome) => new(frame, outcome);

    /// <summary>Creates the arguments of a fallback action.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="frame">The execution.</param>
    /// <param name="outcome">The outcome being replaced.</param>
    /// <returns>The arguments.</returns>
    public static FallbackActionArguments<T> FallbackAction<T>(ExecutionFrame frame, in Outcome<T> outcome) => new(frame, outcome);

    /// <summary>Creates the arguments of <c>OnFallback</c>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="frame">The execution.</param>
    /// <param name="outcome">The outcome being replaced.</param>
    /// <returns>The arguments.</returns>
    public static OnFallbackArguments<T> OnFallback<T>(ExecutionFrame frame, in Outcome<T> outcome) => new(frame, outcome);

    /// <summary>Completes a synchronous execution (<c>Execute</c>): its delays block, so the task has normally completed already.</summary>
    /// <typeparam name="TOut">What the execution returns.</typeparam>
    /// <param name="task">The execution.</param>
    /// <returns>Its result.</returns>
    public static TOut Wait<TOut>(ValueTask<TOut> task)
        => task.IsCompleted ? task.GetAwaiter().GetResult() : task.AsTask().GetAwaiter().GetResult();

    /// <summary>Adapts a void execution to a non-generic <see cref="ValueTask"/>, allocation-free when it completes synchronously.</summary>
    /// <param name="task">The execution.</param>
    /// <param name="pooled">Whether the pipeline returns pooled tasks (plan S2).</param>
    /// <returns>The task.</returns>
    public static ValueTask ToVoidTask(ValueTask<VoidResult> task, bool pooled)
    {
        if (task.IsCompletedSuccessfully)
        {
            _ = task.Result;
            return default;
        }

        return pooled ? AwaitVoidAsync(task) : new ValueTask(task.AsTask());
    }

    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private static async ValueTask AwaitVoidAsync(ValueTask<VoidResult> task) => await task.ConfigureAwait(false);

    /// <summary>
    /// Releases what the breakers of <paramref name="pipeline"/> attached to user objects (state providers, manual
    /// controls), so the pipeline that replaces it (a generated pipeline's UseTimeProvider or reload) can attach them.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="pipeline">The pipeline being replaced.</param>
    public static void ReleaseAttachments<T>(ResiliencePipeline<T> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        foreach (var strategy in pipeline.Core.Strategies)
        {
            Release(strategy);
        }
    }

    internal static void Release<T>(PipelineStrategy<T> strategy)
    {
        if (strategy is CircuitBreaker.CircuitBreakerStrategy<T> breaker)
        {
            breaker.Attachment?.Dispose();
        }
    }

    /// <summary>
    /// Validates strategy options with the runtime builder's own rules (Polly's ranges), throwing a
    /// <see cref="System.ComponentModel.DataAnnotations.ValidationException"/> for an invalid value. Generated
    /// pipelines validate their inlined strategies (retry, timeout, fallback) this way when they create or
    /// reload their settings.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    public static void Validate(ResilienceStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
    }
}
