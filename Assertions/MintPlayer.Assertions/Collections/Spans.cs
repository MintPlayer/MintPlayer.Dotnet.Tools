using System.Runtime.InteropServices;

namespace MintPlayer.Assertions.Collections;

/// <summary>
/// Turns a sequence into a <see cref="ReadOnlySpan{T}"/>, without copying when it is already
/// contiguous.
/// </summary>
/// <remarks>
/// <para>
/// The collection assertions previously wrote <c>source as IReadOnlyList&lt;T&gt; ?? [.. source]</c>
/// and then iterated the result. That reads as a fast path and is not one: iterating an
/// <c>IReadOnlyList&lt;T&gt;</c> goes through <c>IEnumerable&lt;T&gt;.GetEnumerator()</c>, whose
/// return type is the interface <c>IEnumerator&lt;T&gt;</c>, so the underlying struct enumerator is
/// boxed — one heap allocation, every call.
/// </para>
/// <para>
/// A span has no enumerator to box and no interface to dispatch through: its enumerator is a ref
/// struct the JIT inlines, and bounds checks go against the span's own length. Measured over 100k
/// loops of 8 items, spans came out ~2x faster than the boxed foreach for a <c>List&lt;T&gt;</c> and
/// ~7x faster for an array, allocating nothing in both cases.
/// </para>
/// <para>
/// The array case is tested first, and that ordering is load-bearing: an array is not a
/// <c>List&lt;T&gt;</c>, so testing only for the list silently drops every <c>T[]</c> onto the
/// copying path — and arrays are what test code writes.
/// </para>
/// <para>
/// ⚠️ <see cref="CollectionsMarshal.AsSpan{T}(List{T})"/> returns the list's own backing array. The
/// span is invalidated if the list is resized, so this is only safe because no assertion mutates the
/// sequence it was handed.
/// </para>
/// </remarks>
internal static class Spans
{
    /// <summary>
    /// A span over <paramref name="source"/>, copying only when it is not already contiguous.
    /// </summary>
    /// <remarks>
    /// Returning a span over a freshly allocated array is safe: the array lives on the heap and the
    /// GC tracks the span's interior pointer into it, so the array cannot be collected while the
    /// span is alive.
    /// </remarks>
    public static ReadOnlySpan<T> From<T>(IEnumerable<T> source) => source switch
    {
        T[] array => array,
        List<T> list => CollectionsMarshal.AsSpan(list),
        _ => (T[])[.. source],
    };

    /// <summary>
    /// The same materialisation, but as a list — for sequences that cannot be held in a span.
    /// </summary>
    /// <remarks>
    /// Used for the caller-supplied <c>expected</c>/<c>unexpected</c> sequences, which a span cannot
    /// serve. <see cref="ReadOnlySpan{T}"/> is a ref struct, so it cannot be passed to
    /// <c>FailWith</c>'s <c>object?</c> parameters — and those sequences are rendered in failure
    /// messages — nor captured by a lambda, which the inspector assertions do.
    ///
    /// The subject is different: it is available separately as <c>Subject</c> for rendering, so the
    /// span never has to become an object. That asymmetry is why one side converted and the other
    /// did not, and it is worth knowing before someone tries to "finish the job".
    ///
    /// The cost is small and on the right side of the trade: the expectation is iterated once per
    /// assertion and is typically a handful of items, where the subject is iterated by every
    /// assertion in a suite.
    /// </remarks>
    public static IReadOnlyList<T> ListFrom<T>(IEnumerable<T> source) => source as IReadOnlyList<T> ?? [.. source];
}
