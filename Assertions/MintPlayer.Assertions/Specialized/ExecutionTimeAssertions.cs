using System.Diagnostics;
using System.Runtime.ExceptionServices;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on the execution time of an action, obtained from
/// <see cref="ActionAssertions.ExecutionTime"/>.
/// </summary>
/// <remarks>
/// <para>
/// The action runs <b>once</b>, on the first assertion, and every later assertion on the same
/// instance reads that one measurement. Re-running it per assertion (the previous behaviour) meant
/// <c>.BeGreaterThan(x).And.BeLessThan(y)</c> measured two different executions and could report a
/// self-contradictory pair of results for a single action.
/// </para>
/// <para>
/// An assertion that names an upper bound measures with a <b>deadline</b>: the action runs on a
/// background thread and the assertion stops waiting once the bound has demonstrably been exceeded.
/// Running it inline — the previous behaviour — meant <c>BeLessThan(1.Seconds())</c> against an
/// action that never returns never returned either, wedging the test run instead of failing it.
/// There is nothing to assert about an action that has already blown its budget, so waiting longer
/// buys nothing.
/// </para>
/// <para>
/// A lower bound has no deadline to apply and runs inline, which is the cheaper path: no thread, no
/// synchronisation. That asymmetry is deliberate, not an oversight — <c>BeGreaterThan</c> on a
/// hanging action is a hang either way, and the common case (a fast action) should not pay for a
/// thread hop.
/// </para>
/// <para>
/// ⚠️ A timed-out action keeps running on its background thread. Nothing can safely abort arbitrary
/// user code mid-flight, so the assertion reports and moves on; the thread finishes on its own.
/// </para>
/// </remarks>
public class ExecutionTimeAssertions
{
    /// <summary>
    /// Grace added to the deadline before giving up, so a genuinely borderline action is reported
    /// as "took slightly too long" rather than "never finished".
    /// </summary>
    private static readonly TimeSpan DeadlineGrace = TimeSpan.FromMilliseconds(50);

    private readonly Action? action;
    private Measurement? measurement;

    public ExecutionTimeAssertions(Action? action, string? subjectExpression)
    {
        this.action = action;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "action" : subjectExpression!;
    }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For("execution time of " + SubjectExpression);

    public AndConstraint<ExecutionTimeAssertions> BeLessThan(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        var m = Measure(expected, because, becauseArgs);
        if (!m.Ran) return new(this);

        Assert().ForCondition(m.Completed && m.Elapsed < expected).BecauseOf(because, becauseArgs)
            .FailWith(m.Completed
                ? "Expected {subject} to be less than {0}{reason}, but it took {1}."
                : "Expected {subject} to be less than {0}{reason}, but it had not completed after {1}.",
                expected, m.Elapsed);
        return new(this);
    }

    public AndConstraint<ExecutionTimeAssertions> BeLessThanOrEqualTo(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        var m = Measure(expected, because, becauseArgs);
        if (!m.Ran) return new(this);

        Assert().ForCondition(m.Completed && m.Elapsed <= expected).BecauseOf(because, becauseArgs)
            .FailWith(m.Completed
                ? "Expected {subject} to be less than or equal to {0}{reason}, but it took {1}."
                : "Expected {subject} to be less than or equal to {0}{reason}, but it had not completed after {1}.",
                expected, m.Elapsed);
        return new(this);
    }

    public AndConstraint<ExecutionTimeAssertions> BeGreaterThan(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        var m = Measure(upperBound: null, because, becauseArgs);
        if (!m.Ran) return new(this);

        Assert().ForCondition(m.Elapsed > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than {0}{reason}, but it took {1}.", expected, m.Elapsed);
        return new(this);
    }

    public AndConstraint<ExecutionTimeAssertions> BeGreaterThanOrEqualTo(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        var m = Measure(upperBound: null, because, becauseArgs);
        if (!m.Ran) return new(this);

        Assert().ForCondition(m.Elapsed >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than or equal to {0}{reason}, but it took {1}.", expected, m.Elapsed);
        return new(this);
    }

    /// <summary>Asserts the execution time is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public AndConstraint<ExecutionTimeAssertions> BeCloseTo(TimeSpan expected, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);

        var m = Measure(expected + precision, because, becauseArgs);
        if (!m.Ran) return new(this);

        Assert().ForCondition(m.Completed && (m.Elapsed - expected).Duration() <= precision).BecauseOf(because, becauseArgs)
            .FailWith(m.Completed
                ? "Expected {subject} to be within {0} of {1}{reason}, but it took {2}."
                : "Expected {subject} to be within {0} of {1}{reason}, but it had not completed after {2}.",
                precision, expected, m.Elapsed);
        return new(this);
    }

    /// <summary>
    /// Runs the action once and caches the result. <paramref name="upperBound"/>, when given, caps
    /// how long the measurement waits.
    /// </summary>
    private Measurement Measure(TimeSpan? upperBound, string? because, object?[] becauseArgs)
    {
        if (measurement is { } cached) return cached;

        Assert().ForCondition(action is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be measurable{reason}, but the action was <null>.");
        if (action is null) return measurement = Measurement.NotRun;

        return measurement = upperBound is { } deadline
            ? RunWithDeadline(action, deadline)
            : RunInline(action);
    }

    private static Measurement RunInline(Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        action.Invoke();
        stopwatch.Stop();
        return new Measurement(stopwatch.Elapsed, Completed: true, Ran: true);
    }

    private static Measurement RunWithDeadline(Action action, TimeSpan deadline)
    {
        ExceptionDispatchInfo? failure = null;
        var stopwatch = Stopwatch.StartNew();

        var run = Task.Run(() =>
        {
            try { action.Invoke(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });

        var completed = run.Wait(deadline + DeadlineGrace);
        stopwatch.Stop();

        // The action's own exception is the caller's problem and takes precedence over any timing
        // verdict: rethrown with its original stack, exactly as the inline path would surface it.
        failure?.Throw();

        return new Measurement(stopwatch.Elapsed, completed, Ran: true);
    }

    private sealed record Measurement(TimeSpan Elapsed, bool Completed, bool Ran)
    {
        /// <summary>The action was null; the failure is already reported and comparisons are skipped.</summary>
        public static readonly Measurement NotRun = new(TimeSpan.Zero, Completed: false, Ran: false);
    }
}
