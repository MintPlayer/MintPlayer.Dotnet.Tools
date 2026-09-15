namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Resolves the exception an assertion should judge, seeing through <see cref="AggregateException"/>.
/// </summary>
/// <remarks>
/// <para>
/// Without this, <c>Should().Throw&lt;ArgumentException&gt;()</c> <b>fails</b> against an action that
/// threw <c>AggregateException(inner: ArgumentException)</c> — the wrapper is not an
/// <c>ArgumentException</c>, so a direct type test says no. That is the wrong answer: the action did
/// throw an <c>ArgumentException</c>, and anything that goes through <c>Task.Wait()</c>,
/// <c>Parallel.ForEach</c>, or a faulted <c>Task.Result</c> wraps it.
/// </para>
/// <para>
/// A wrapper is only transparent when the caller is <i>not</i> asking about the wrapper:
/// <c>Throw&lt;AggregateException&gt;()</c> still matches the <c>AggregateException</c> itself, so
/// asserting on the wrapper stays possible.
/// </para>
/// <para>
/// <b>Only the first match is returned, deliberately.</b> FluentAssertions exposes
/// <c>IEnumerable&lt;TException&gt;</c> so every match can be asserted on; that shape costs a
/// collection allocation on the passing path of every exception assertion, which this library's
/// performance boundary does not permit for a case (several matching inner exceptions, each needing
/// separate assertions) that is vanishingly rare. <c>Flatten()</c> means a match nested several
/// wrappers deep is still found.
/// </para>
/// </remarks>
internal static class ExceptionExtractor
{
    /// <summary>
    /// The first exception assignable to <typeparamref name="TException"/>, looking inside an
    /// <see cref="AggregateException"/> when the caller is asking about something else.
    /// </summary>
    public static TException? Assignable<TException>(Exception? caught)
        where TException : Exception
    {
        if (caught is null) return null;
        if (caught is TException direct) return direct;

        // Asking about the wrapper itself is answered above; below, the wrapper is transparent.
        if (caught is not AggregateException aggregate) return null;

        foreach (var inner in aggregate.Flatten().InnerExceptions)
        {
            if (inner is TException match) return match;
        }

        return null;
    }

    /// <summary>
    /// The first exception of <b>exactly</b> <typeparamref name="TException"/> (not a derived type),
    /// looking inside an <see cref="AggregateException"/> when the caller is asking about something
    /// else.
    /// </summary>
    public static TException? Exactly<TException>(Exception? caught)
        where TException : Exception
    {
        if (caught is null) return null;
        if (caught.GetType() == typeof(TException)) return (TException)caught;

        if (caught is not AggregateException aggregate) return null;

        foreach (var inner in aggregate.Flatten().InnerExceptions)
        {
            if (inner.GetType() == typeof(TException)) return (TException)inner;
        }

        return null;
    }
}
