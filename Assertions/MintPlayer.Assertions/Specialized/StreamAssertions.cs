using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on a <see cref="Stream"/>: readability, writability, seekability, position and length.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Nothing here reads the stream's contents, and that is a deliberate boundary rather than a
/// gap.</b> Reading consumes a forward-only stream and moves the position of a seekable one, so an
/// assertion that did it would change the thing it was asserting about — and the failure would show
/// up in whatever ran next, not here. To compare contents, read the stream yourself and assert on
/// the bytes or the text; that way the consumption is visible at the call site where it belongs.
/// </para>
/// <para>
/// Every property consulted below (<c>CanRead</c>, <c>Length</c>, <c>Position</c>) can throw
/// <see cref="ObjectDisposedException"/> or <see cref="NotSupportedException"/> on a perfectly
/// ordinary stream, so each is read through <see cref="Probe{T}"/> and a throw becomes an assertion
/// failure that names the exception. The alternative — letting it escape — reports "the test errored"
/// for something the assertion is precisely meant to be checking.
/// </para>
/// </remarks>
public class StreamAssertions
{
    public StreamAssertions(Stream? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "stream" : subjectExpression!;
    }

    /// <summary>The stream under test.</summary>
    public Stream? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the stream can be read from.</summary>
    public AndConstraint<StreamAssertions> BeReadable(string? because = null, params object?[] becauseArgs)
        => Check(s => s.CanRead, true, "readable", because, becauseArgs);

    /// <summary>Asserts the stream cannot be read from.</summary>
    public AndConstraint<StreamAssertions> NotBeReadable(string? because = null, params object?[] becauseArgs)
        => Check(s => s.CanRead, false, "readable", because, becauseArgs);

    /// <summary>Asserts the stream can be written to.</summary>
    public AndConstraint<StreamAssertions> BeWritable(string? because = null, params object?[] becauseArgs)
        => Check(s => s.CanWrite, true, "writable", because, becauseArgs);

    /// <summary>Asserts the stream cannot be written to.</summary>
    public AndConstraint<StreamAssertions> NotBeWritable(string? because = null, params object?[] becauseArgs)
        => Check(s => s.CanWrite, false, "writable", because, becauseArgs);

    /// <summary>Asserts the stream supports seeking.</summary>
    public AndConstraint<StreamAssertions> BeSeekable(string? because = null, params object?[] becauseArgs)
        => Check(s => s.CanSeek, true, "seekable", because, becauseArgs);

    /// <summary>Asserts the stream does not support seeking.</summary>
    public AndConstraint<StreamAssertions> NotBeSeekable(string? because = null, params object?[] becauseArgs)
        => Check(s => s.CanSeek, false, "seekable", because, becauseArgs);

    /// <summary>Asserts the stream's length is <paramref name="expected"/> bytes.</summary>
    public AndConstraint<StreamAssertions> HaveLength(long expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to have a length of {expected}", because, becauseArgs);
        if (!Probe(s => s.Length, "length", because, becauseArgs, out var length)) return new(this);

        Assert().ForCondition(length == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a length of {0}{reason}, but found {1}.", expected, length);
        return new(this);
    }

    /// <summary>Asserts the stream's current position is <paramref name="expected"/>.</summary>
    public AndConstraint<StreamAssertions> HavePosition(long expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to be at position {expected}", because, becauseArgs);
        if (!Probe(s => s.Position, "position", because, becauseArgs, out var position)) return new(this);

        Assert().ForCondition(position == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be at position {0}{reason}, but found {1}.", expected, position);
        return new(this);
    }

    /// <summary>Asserts the stream is positioned at its start.</summary>
    public AndConstraint<StreamAssertions> BeAtStart(string? because = null, params object?[] becauseArgs)
        => HavePosition(0, because, becauseArgs);

    /// <summary>Asserts the stream is positioned at its end.</summary>
    public AndConstraint<StreamAssertions> BeAtEnd(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to be at its end", because, becauseArgs);
        if (!Probe(s => s.Position, "position", because, becauseArgs, out var position)) return new(this);
        if (!Probe(s => s.Length, "length", because, becauseArgs, out var length)) return new(this);

        Assert().ForCondition(position == length).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be at its end ({0}){reason}, but found position {1}.", length, position);
        return new(this);
    }

    /// <summary>Asserts the stream holds no bytes.</summary>
    public AndConstraint<StreamAssertions> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to be empty", because, becauseArgs);
        if (!Probe(s => s.Length, "length", because, becauseArgs, out var length)) return new(this);

        Assert().ForCondition(length == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be empty{reason}, but it has a length of {0}.", length);
        return new(this);
    }

    /// <summary>Asserts the stream holds at least one byte.</summary>
    public AndConstraint<StreamAssertions> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("not to be empty", because, becauseArgs);
        if (!Probe(s => s.Length, "length", because, becauseArgs, out var length)) return new(this);

        Assert().ForCondition(length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be empty{reason}, but it has a length of 0.");
        return new(this);
    }

    private AndConstraint<StreamAssertions> Check(Func<Stream, bool> capability, bool expected, string name, string? because, object?[] becauseArgs)
    {
        if (Subject is null) return FailNull((expected ? "to be " : "not to be ") + name, because, becauseArgs);
        var prefix = expected ? "to be " : "not to be ";
        if (!Probe(capability, name, because, becauseArgs, out var actual)) return new(this);

        // The template is interpolated, so it is built at the call site whether or not it is needed.
        // Guarding the whole call keeps a passing capability check free; measured at 136 B/op before.
        if (actual != expected)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} {prefix}{name}{{reason}}, but it is{(expected ? " not" : string.Empty)}.");
        }
        return new(this);
    }

    /// <summary>
    /// Reads one property of the stream, turning a throw into an assertion failure. False means the
    /// failure was already reported and the caller should stop.
    /// </summary>
    private bool Probe<T>(Func<Stream, T> read, string what, string? because, object?[] becauseArgs, out T value)
    {
        try
        {
            value = read(Subject!);
            return true;
        }
        catch (Exception ex)
        {
            value = default!;
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to report its {what}{{reason}}, but reading it threw {{0}}: {{1}}.",
                    ex.GetType().Name, ex.Message);
            return false;
        }
    }

    private AndConstraint<StreamAssertions> FailNull(string expectation, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} " + expectation + "{reason}, but found <null>.");
        return new(this);
    }
}
