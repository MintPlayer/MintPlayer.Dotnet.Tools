using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Collections;

/// <summary>
/// Assertions on a <see cref="Span{T}"/>. Because ref structs cannot be generic type arguments,
/// every method returns the assertions struct itself (also exposed as <see cref="And"/>) instead
/// of an AndConstraint. The span is only materialized (copied to an array) when a failure message
/// must be rendered.
/// </summary>
public readonly ref struct SpanAssertions<T>
{
    private readonly string? subjectExpression;

    public SpanAssertions(Span<T> subject, string? subjectExpression)
    {
        Subject = subject;
        this.subjectExpression = subjectExpression;
    }

    /// <summary>The span under test.</summary>
    public Span<T> Subject { get; }

    /// <summary>Continues asserting on the same span.</summary>
    public SpanAssertions<T> And => this;

    private ReadOnlySpanAssertions<T> Inner => new(Subject, subjectExpression);

    /// <summary>Asserts the span contains exactly the same items, in order, as <paramref name="expected"/>.</summary>
    public SpanAssertions<T> Be(ReadOnlySpan<T> expected, string? because = null, params object?[] becauseArgs)
    {
        Inner.Be(expected, because, becauseArgs);
        return this;
    }

    /// <summary>Asserts the span contains exactly the same items, in order, as <paramref name="expected"/>.</summary>
    public SpanAssertions<T> Equal(ReadOnlySpan<T> expected, string? because = null, params object?[] becauseArgs)
    {
        Inner.Equal(expected, because, becauseArgs);
        return this;
    }

    /// <summary>Asserts the span has the given length.</summary>
    public SpanAssertions<T> HaveLength(int expected, string? because = null, params object?[] becauseArgs)
    {
        Inner.HaveLength(expected, because, becauseArgs);
        return this;
    }

    /// <summary>Asserts the span is empty.</summary>
    public SpanAssertions<T> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        Inner.BeEmpty(because, becauseArgs);
        return this;
    }

    /// <summary>Asserts the span is not empty.</summary>
    public SpanAssertions<T> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        Inner.NotBeEmpty(because, becauseArgs);
        return this;
    }

    /// <summary>Asserts the span contains the given item.</summary>
    public SpanAssertions<T> Contain(T expected, string? because = null, params object?[] becauseArgs)
    {
        Inner.Contain(expected, because, becauseArgs);
        return this;
    }

    /// <summary>Asserts the span starts with the given sequence of items.</summary>
    public SpanAssertions<T> StartWith(ReadOnlySpan<T> expected, string? because = null, params object?[] becauseArgs)
    {
        Inner.StartWith(expected, because, becauseArgs);
        return this;
    }

    /// <summary>Asserts the span ends with the given sequence of items.</summary>
    public SpanAssertions<T> EndWith(ReadOnlySpan<T> expected, string? because = null, params object?[] becauseArgs)
    {
        Inner.EndWith(expected, because, becauseArgs);
        return this;
    }
}

/// <summary>
/// Assertions on a <see cref="ReadOnlySpan{T}"/>. Because ref structs cannot be generic type
/// arguments, every method returns the assertions struct itself (also exposed as <see cref="And"/>)
/// instead of an AndConstraint. The span is only materialized (copied to an array) when a failure
/// message must be rendered.
/// </summary>
public readonly ref struct ReadOnlySpanAssertions<T>
{
    private readonly string? subjectExpression;

    public ReadOnlySpanAssertions(ReadOnlySpan<T> subject, string? subjectExpression)
    {
        Subject = subject;
        this.subjectExpression = subjectExpression;
    }

    /// <summary>The span under test.</summary>
    public ReadOnlySpan<T> Subject { get; }

    /// <summary>Continues asserting on the same span.</summary>
    public ReadOnlySpanAssertions<T> And => this;

    private Assertion Assert() => Assertion.For(subjectExpression);

    /// <summary>Asserts the span contains exactly the same items, in order, as <paramref name="expected"/>.</summary>
    public ReadOnlySpanAssertions<T> Be(ReadOnlySpan<T> expected, string? because = null, params object?[] becauseArgs)
    {
        if (!SequenceEqual(Subject, expected))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected.ToArray(), Subject.ToArray());
        }
        return this;
    }

    /// <summary>Asserts the span contains exactly the same items, in order, as <paramref name="expected"/>.</summary>
    public ReadOnlySpanAssertions<T> Equal(ReadOnlySpan<T> expected, string? because = null, params object?[] becauseArgs)
    {
        if (!SequenceEqual(Subject, expected))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to equal {0}{reason}, but found {1}.", expected.ToArray(), Subject.ToArray());
        }
        return this;
    }

    /// <summary>Asserts the span has the given length.</summary>
    public ReadOnlySpanAssertions<T> HaveLength(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject.Length != expected)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to have length {0}{reason}, but found {1}: {2}.", expected, Subject.Length, Subject.ToArray());
        }
        return this;
    }

    /// <summary>Asserts the span is empty.</summary>
    public ReadOnlySpanAssertions<T> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        if (!Subject.IsEmpty)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to be empty{reason}, but found {0}.", Subject.ToArray());
        }
        return this;
    }

    /// <summary>Asserts the span is not empty.</summary>
    public ReadOnlySpanAssertions<T> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        if (Subject.IsEmpty)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} not to be empty{reason}.");
        }
        return this;
    }

    /// <summary>Asserts the span contains the given item.</summary>
    public ReadOnlySpanAssertions<T> Contain(T expected, string? because = null, params object?[] becauseArgs)
    {
        var comparer = EqualityComparer<T>.Default;
        var found = false;
        foreach (var item in Subject)
        {
            if (comparer.Equals(item, expected)) { found = true; break; }
        }

        if (!found)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to contain {0}{reason}, but found {1}.", expected, Subject.ToArray());
        }
        return this;
    }

    /// <summary>Asserts the span starts with the given sequence of items.</summary>
    public ReadOnlySpanAssertions<T> StartWith(ReadOnlySpan<T> expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject.Length < expected.Length || !SequenceEqual(Subject[..expected.Length], expected))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to start with {0}{reason}, but found {1}.", expected.ToArray(), Subject.ToArray());
        }
        return this;
    }

    /// <summary>Asserts the span ends with the given sequence of items.</summary>
    public ReadOnlySpanAssertions<T> EndWith(ReadOnlySpan<T> expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject.Length < expected.Length || !SequenceEqual(Subject[^expected.Length..], expected))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to end with {0}{reason}, but found {1}.", expected.ToArray(), Subject.ToArray());
        }
        return this;
    }

    private static bool SequenceEqual(ReadOnlySpan<T> actual, ReadOnlySpan<T> expected)
    {
        if (actual.Length != expected.Length) return false;

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < actual.Length; i++)
        {
            if (!comparer.Equals(actual[i], expected[i])) return false;
        }
        return true;
    }
}
