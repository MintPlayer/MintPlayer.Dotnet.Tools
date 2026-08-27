using System.Diagnostics;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on the execution time of an action, obtained from
/// <see cref="ActionAssertions.ExecutionTime"/>. The action is stored lazily; each assertion
/// method invokes it exactly once, measured with a <see cref="Stopwatch"/>, and includes the
/// measured time in its failure message. An exception thrown by the action propagates as-is.
/// </summary>
public class ExecutionTimeAssertions
{
    private readonly Action? action;

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
        var elapsed = Measure(because, becauseArgs, out var measured);
        Assert().ForCondition(!measured || elapsed < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be less than {0}{reason}, but it took {1}.", expected, elapsed);
        return new(this);
    }

    public AndConstraint<ExecutionTimeAssertions> BeLessThanOrEqualTo(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        var elapsed = Measure(because, becauseArgs, out var measured);
        Assert().ForCondition(!measured || elapsed <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be less than or equal to {0}{reason}, but it took {1}.", expected, elapsed);
        return new(this);
    }

    public AndConstraint<ExecutionTimeAssertions> BeGreaterThan(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        var elapsed = Measure(because, becauseArgs, out var measured);
        Assert().ForCondition(!measured || elapsed > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than {0}{reason}, but it took {1}.", expected, elapsed);
        return new(this);
    }

    public AndConstraint<ExecutionTimeAssertions> BeGreaterThanOrEqualTo(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        var elapsed = Measure(because, becauseArgs, out var measured);
        Assert().ForCondition(!measured || elapsed >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than or equal to {0}{reason}, but it took {1}.", expected, elapsed);
        return new(this);
    }

    /// <summary>Asserts the execution time is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public AndConstraint<ExecutionTimeAssertions> BeCloseTo(TimeSpan expected, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        var elapsed = Measure(because, becauseArgs, out var measured);
        Assert().ForCondition(!measured || (elapsed - expected).Duration() <= precision).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be within {0} of {1}{reason}, but it took {2}.", precision, expected, elapsed);
        return new(this);
    }

    /// <summary>
    /// Runs the action once under a <see cref="Stopwatch"/>. <paramref name="measured"/> is false
    /// when the action was null (already reported), so the caller's comparison is skipped.
    /// </summary>
    private TimeSpan Measure(string? because, object?[] becauseArgs, out bool measured)
    {
        Assert().ForCondition(action is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be measurable{reason}, but the action was <null>.");
        if (action is null)
        {
            measured = false;
            return TimeSpan.Zero;
        }

        var stopwatch = Stopwatch.StartNew();
        action.Invoke();
        stopwatch.Stop();
        measured = true;
        return stopwatch.Elapsed;
    }
}
