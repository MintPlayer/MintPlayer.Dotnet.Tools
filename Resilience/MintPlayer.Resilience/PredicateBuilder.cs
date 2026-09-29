using System.ComponentModel;
using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.RateLimiting;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Timeout;

namespace MintPlayer.Resilience;

/// <summary>
/// Builds a <c>ShouldHandle</c> predicate from exception types and result tests; it converts implicitly
/// to the predicate property of every strategy's options.
/// </summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>
/// <see cref="Handle{TException}()"/> also matches a rejection whose exception type is a <c>TException</c>:
/// <c>Handle&lt;TimeoutRejectedException&gt;()</c> matches an inner timeout without creating the exception.
/// </remarks>
public class PredicateBuilder<TResult>
{
    private readonly List<Predicate<Outcome<TResult>>> _predicates = [];

    /// <summary>Handles an exception of type <typeparamref name="TException"/> (or a rejection of that type).</summary>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <returns>This builder.</returns>
    public PredicateBuilder<TResult> Handle<TException>()
        where TException : Exception
        => Add(static outcome => outcome.RawException is TException || RejectionMatches<TException>.Kind(outcome.Rejection));

    /// <summary>Handles an exception of type <typeparamref name="TException"/> that satisfies <paramref name="predicate"/>.</summary>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <param name="predicate">The test the exception must pass.</param>
    /// <returns>This builder.</returns>
    public PredicateBuilder<TResult> Handle<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(predicate);

        // The rejection exception is only created when its type can match.
        return Add(outcome => outcome.RawException is TException exception
            ? predicate(exception)
            : RejectionMatches<TException>.Kind(outcome.Rejection) && predicate((TException)outcome.Exception!));
    }

    /// <summary>Handles an exception of type <typeparamref name="TException"/> anywhere in the inner-exception chain (and in an <see cref="AggregateException"/>).</summary>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <returns>This builder.</returns>
    public PredicateBuilder<TResult> HandleInner<TException>()
        where TException : Exception
        => HandleInner<TException>(static _ => true);

    /// <summary>Handles an exception of type <typeparamref name="TException"/> anywhere in the inner-exception chain that satisfies <paramref name="predicate"/>.</summary>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <param name="predicate">The test the exception must pass.</param>
    /// <returns>This builder.</returns>
    public PredicateBuilder<TResult> HandleInner<TException>(Func<TException, bool> predicate)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Add(outcome => HandleInner(outcome.IsRejected ? outcome.Exception : outcome.RawException, predicate));

        static bool HandleInner(Exception? exception, Func<TException, bool> predicate)
        {
            if (exception is AggregateException aggregate)
            {
                foreach (var innerException in aggregate.Flatten().InnerExceptions)
                {
                    if (HandleNested(predicate, innerException))
                    {
                        return true;
                    }
                }
            }

            return HandleNested(predicate, exception);
        }

        static bool HandleNested(Func<TException, bool> predicate, Exception? current)
        {
            while (current is not null)
            {
                if (current is TException match)
                {
                    return predicate(match);
                }

                current = current.InnerException;
            }

            return false;
        }
    }

    /// <summary>Handles a result that satisfies <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The test the result must pass.</param>
    /// <returns>This builder.</returns>
    public PredicateBuilder<TResult> HandleResult(Func<TResult, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Add(outcome => outcome.TryGetResult(out var result) && predicate(result!));
    }

    /// <summary>Handles a result equal to <paramref name="result"/>.</summary>
    /// <param name="result">The result to handle.</param>
    /// <param name="comparer">The comparer; <see cref="EqualityComparer{T}.Default"/> when null.</param>
    /// <returns>This builder.</returns>
    public PredicateBuilder<TResult> HandleResult(TResult result, IEqualityComparer<TResult>? comparer = null)
    {
        comparer ??= EqualityComparer<TResult>.Default;
        return HandleResult(r => comparer.Equals(r, result));
    }

    /// <summary>Builds the predicate: true when any of the configured tests matches.</summary>
    /// <returns>The predicate.</returns>
    /// <exception cref="InvalidOperationException">No test was configured.</exception>
    public Predicate<Outcome<TResult>> Build() => _predicates.Count switch
    {
        0 => throw new InvalidOperationException("No predicates were configured. There must be at least one predicate added."),
        1 => _predicates[0],
        _ => CreatePredicate([.. _predicates]),
    };

    /// <summary>Converts the builder to a retry predicate.</summary>
    /// <param name="builder">The builder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator Func<RetryPredicateArguments<TResult>, bool>(PredicateBuilder<TResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var predicate = builder.Build();
        return args => predicate(args.Outcome);
    }

    /// <summary>Converts the builder to a fallback predicate.</summary>
    /// <param name="builder">The builder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator Func<FallbackPredicateArguments<TResult>, bool>(PredicateBuilder<TResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var predicate = builder.Build();
        return args => predicate(args.Outcome);
    }

    /// <summary>Converts the builder to a circuit-breaker predicate.</summary>
    /// <param name="builder">The builder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator Func<CircuitBreakerPredicateArguments<TResult>, bool>(PredicateBuilder<TResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var predicate = builder.Build();
        return args => predicate(args.Outcome);
    }

    private static Predicate<Outcome<TResult>> CreatePredicate(Predicate<Outcome<TResult>>[] predicates) =>
        outcome =>
        {
            foreach (var predicate in predicates)
            {
                if (predicate(outcome))
                {
                    return true;
                }
            }

            return false;
        };

    private PredicateBuilder<TResult> Add(Predicate<Outcome<TResult>> predicate)
    {
        _predicates.Add(predicate);
        return this;
    }

    /// <summary>For each rejection kind, whether its exception type is a <typeparamref name="TException"/>; computed once.</summary>
    private static class RejectionMatches<TException>
        where TException : Exception
    {
        private static readonly bool CircuitOpen = typeof(TException).IsAssignableFrom(typeof(BrokenCircuitException));
        private static readonly bool CircuitIsolated = typeof(TException).IsAssignableFrom(typeof(IsolatedCircuitException));
        private static readonly bool RateLimited = typeof(TException).IsAssignableFrom(typeof(RateLimiterRejectedException));
        private static readonly bool Timeout = typeof(TException).IsAssignableFrom(typeof(TimeoutRejectedException));

        public static bool Kind(RejectionKind kind) => kind switch
        {
            RejectionKind.CircuitOpen => CircuitOpen,
            RejectionKind.CircuitIsolated => CircuitIsolated,
            RejectionKind.RateLimited => RateLimited,
            RejectionKind.Timeout => Timeout,
            _ => false,
        };
    }
}

/// <summary>A <see cref="PredicateBuilder{TResult}"/> for the non-generic builder's options.</summary>
public sealed class PredicateBuilder : PredicateBuilder<object>
{
}
