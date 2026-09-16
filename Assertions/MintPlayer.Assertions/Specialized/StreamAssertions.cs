using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on a <see cref="Stream"/>: its capabilities, its length and its position.
/// </summary>
/// <remarks>
/// The cheapest thing in this milestone and the least controversial: every check reads a property
/// the stream already exposes, so there is no reflection here and nothing that could affect any
/// other assertion. Reading <see cref="Stream.Length"/> or <see cref="Stream.Position"/> can throw
/// on a non-seekable stream, which is why the assertions that need them check
/// <see cref="Stream.CanSeek"/> first and say so rather than letting a
/// <see cref="NotSupportedException"/> escape from what is supposed to be an assertion.
/// </remarks>
public class StreamAssertions : StreamAssertions<Stream, StreamAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public StreamAssertions(Stream? subject, string? subjectExpression) : base(subject, subjectExpression) { }
}

/// <summary>
/// The shared stream assertions, generic over the concrete stream and assertions types so a derived
/// family (see <see cref="BufferedStreamAssertions"/>) keeps its own type through a chain.
/// </summary>
public abstract class StreamAssertions<TSubject, TSelf> : ReferenceTypeAssertions<TSubject, TSelf>
    where TSubject : Stream
    where TSelf : StreamAssertions<TSubject, TSelf>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    protected StreamAssertions(TSubject? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the stream can be read from.</summary>
    public AndConstraint<TSelf> BeReadable(string? because = null, params object?[] becauseArgs)
        => Capability(s => s.CanRead, true, "readable", because, becauseArgs);

    /// <summary>Asserts the stream cannot be read from.</summary>
    public AndConstraint<TSelf> NotBeReadable(string? because = null, params object?[] becauseArgs)
        => Capability(s => s.CanRead, false, "readable", because, becauseArgs);

    /// <summary>Asserts the stream can be written to.</summary>
    public AndConstraint<TSelf> BeWritable(string? because = null, params object?[] becauseArgs)
        => Capability(s => s.CanWrite, true, "writable", because, becauseArgs);

    /// <summary>Asserts the stream cannot be written to.</summary>
    public AndConstraint<TSelf> NotBeWritable(string? because = null, params object?[] becauseArgs)
        => Capability(s => s.CanWrite, false, "writable", because, becauseArgs);

    /// <summary>Asserts the stream supports seeking.</summary>
    public AndConstraint<TSelf> BeSeekable(string? because = null, params object?[] becauseArgs)
        => Capability(s => s.CanSeek, true, "seekable", because, becauseArgs);

    /// <summary>Asserts the stream does not support seeking.</summary>
    public AndConstraint<TSelf> NotBeSeekable(string? because = null, params object?[] becauseArgs)
        => Capability(s => s.CanSeek, false, "seekable", because, becauseArgs);

    /// <summary>Asserts the stream can be read but not written.</summary>
    public AndConstraint<TSelf> BeReadOnly(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to be read-only", because, becauseArgs);

        Assert().ForCondition(Subject.CanRead && !Subject.CanWrite).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be read-only{reason}, but it is {0}.", Describe(Subject));
        return new((TSelf)this);
    }

    /// <summary>Asserts the stream can be written but not read.</summary>
    public AndConstraint<TSelf> BeWriteOnly(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to be write-only", because, becauseArgs);

        Assert().ForCondition(Subject.CanWrite && !Subject.CanRead).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be write-only{reason}, but it is {0}.", Describe(Subject));
        return new((TSelf)this);
    }

    /// <summary>Asserts the stream's length is <paramref name="expected"/> bytes.</summary>
    public AndConstraint<TSelf> HaveLength(long expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to have a length of {expected} byte(s)", because, becauseArgs);
        if (!RequireSeekable("length", because, becauseArgs)) return new((TSelf)this);

        Assert().ForCondition(Subject.Length == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a length of {0} byte(s){reason}, but found {1}.", expected, Subject.Length);
        return new((TSelf)this);
    }

    /// <summary>Asserts the stream's length is not <paramref name="unexpected"/> bytes.</summary>
    public AndConstraint<TSelf> NotHaveLength(long unexpected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to have a length of {unexpected} byte(s)", because, becauseArgs);
        if (!RequireSeekable("length", because, becauseArgs)) return new((TSelf)this);

        Assert().ForCondition(Subject.Length != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a length of {0} byte(s){reason}.", unexpected);
        return new((TSelf)this);
    }

    /// <summary>Asserts the stream's position is at <paramref name="expected"/>.</summary>
    public AndConstraint<TSelf> HavePosition(long expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to be at position {expected}", because, becauseArgs);
        if (!RequireSeekable("position", because, becauseArgs)) return new((TSelf)this);

        Assert().ForCondition(Subject.Position == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be at position {0}{reason}, but found {1}.", expected, Subject.Position);
        return new((TSelf)this);
    }

    /// <summary>Asserts the stream's position is not <paramref name="unexpected"/>.</summary>
    public AndConstraint<TSelf> NotHavePosition(long unexpected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to be at position {unexpected}", because, becauseArgs);
        if (!RequireSeekable("position", because, becauseArgs)) return new((TSelf)this);

        Assert().ForCondition(Subject.Position != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be at position {0}{reason}.", unexpected);
        return new((TSelf)this);
    }

    /// <summary>Asserts the stream is positioned at its start.</summary>
    public AndConstraint<TSelf> BeAtStart(string? because = null, params object?[] becauseArgs)
        => HavePosition(0, because, becauseArgs);

    /// <summary>Asserts the stream is positioned at its end.</summary>
    public AndConstraint<TSelf> BeAtEnd(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to be at its end", because, becauseArgs);
        if (!RequireSeekable("position", because, becauseArgs)) return new((TSelf)this);

        Assert().ForCondition(Subject.Position == Subject.Length).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be at its end{reason}, but it is at {0} of {1}.", Subject.Position, Subject.Length);
        return new((TSelf)this);
    }

    private AndConstraint<TSelf> Capability(Func<TSubject, bool> read, bool expected, string name, string? because, object?[] becauseArgs)
    {
        if (Subject is null) return FailNull(expected ? $"to be {name}" : $"not to be {name}", because, becauseArgs);

        Assert().ForCondition(read(Subject) == expected).BecauseOf(because, becauseArgs)
            .FailWith(expected
                ? "Expected {subject} to be " + name + "{reason}, but it is not."
                : "Did not expect {subject} to be " + name + "{reason}.");
        return new((TSelf)this);
    }

    /// <summary>
    /// Reports "this stream cannot answer that" instead of letting the stream throw.
    /// </summary>
    /// <remarks>
    /// <see cref="Stream.Length"/> and <see cref="Stream.Position"/> throw
    /// <see cref="NotSupportedException"/> on a non-seekable stream — a network stream, a pipe, a
    /// compression stream. An assertion that lets that escape reports a crash where the honest
    /// answer is a failure with a reason.
    /// </remarks>
    private bool RequireSeekable(string what, string? because, object?[] becauseArgs)
    {
        if (Subject!.CanSeek) return true;

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a " + what + "{reason}, but the stream is not seekable, so it has none.");
        return false;
    }

    private static string Describe(Stream stream) => (stream.CanRead, stream.CanWrite) switch
    {
        (true, true) => "both readable and writable",
        (true, false) => "read-only",
        (false, true) => "write-only",
        _ => "neither readable nor writable",
    };

    private AndConstraint<TSelf> FailNull(string expectation, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} " + expectation + "{reason}, but it was <null>.");
        return new((TSelf)this);
    }
}

/// <summary>
/// Assertions on a <see cref="BufferedStream"/>: everything a stream has, plus its buffer size.
/// </summary>
public class BufferedStreamAssertions : StreamAssertions<BufferedStream, BufferedStreamAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public BufferedStreamAssertions(BufferedStream? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the buffer is <paramref name="expected"/> bytes.</summary>
    public AndConstraint<BufferedStreamAssertions> HaveBufferSize(int expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a buffer size of {0}{reason}, but it was <null>.", expected);
        if (Subject is null) return new(this);

        Assert().ForCondition(Subject.BufferSize == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a buffer size of {0}{reason}, but found {1}.", expected, Subject.BufferSize);
        return new(this);
    }

    /// <summary>Asserts the buffer is not <paramref name="unexpected"/> bytes.</summary>
    public AndConstraint<BufferedStreamAssertions> NotHaveBufferSize(int unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a buffer size of {0}{reason}, but it was <null>.", unexpected);
        if (Subject is null) return new(this);

        Assert().ForCondition(Subject.BufferSize != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a buffer size of {0}{reason}.", unexpected);
        return new(this);
    }
}
