namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Counts the work the equivalency walker does, so a test can assert an exact operation count.
/// </summary>
/// <remarks>
/// <para>
/// This exists to close the one hole an allocation gate cannot: a change that makes the walker do
/// more work without allocating more. Operation counts are perfectly deterministic — no warm-up, no
/// GC, no allowance, no machine dependence — so a test can assert an <b>exact</b> number and catch
/// "someone made this O(n²)" even when the extra work is a stack-allocated matrix or a cache hit.
/// A count assertion also documents the algorithm's complexity in executable form.
/// </para>
/// <para>
/// ⚠️ <b>The cost, stated honestly.</b> The counters are behind a static <see cref="Enabled"/> flag
/// that is false in every production run, so the walker pays one static bool read and one
/// not-taken, perfectly-predicted branch per counted site. That is not literally zero, and the PRD's
/// rule says "must not allocate more, reflect more, or branch more". It was measured rather than
/// waved through: see the numbers recorded in the PRD's spike section. If the benchmark ever shows
/// this costing anything, delete it and accept the documented gap instead — a gate is not worth
/// paying for with the thing it protects.
/// </para>
/// <para>
/// [ThreadStatic], which is not decoration. These are process-wide statics and xUnit runs test
/// classes in parallel, so a second walk on another thread was counted into the first: an exact-count
/// assertion read 259 nodes where the graph has 133. Thread-local costs nothing extra on the walk —
/// a single comparison never crosses threads — and removes the interference entirely, where
/// disabling test parallelism would have hidden it behind a slower suite.
/// </para>
/// </remarks>
internal static class EquivalencyDiagnostics
{
    /// <summary>False in every production run. A test sets it through <see cref="Measure"/>.</summary>
    [ThreadStatic] public static bool Enabled;

    /// <summary>Times <c>CompareNode</c> was entered.</summary>
    [ThreadStatic] public static long Nodes;

    /// <summary>Times a member was looked up by name on the subject.</summary>
    [ThreadStatic] public static long MemberLookups;

    /// <summary>Times two collection items were probed for equivalence by the unordered matcher.</summary>
    [ThreadStatic] public static long MatchProbes;

    /// <summary>Runs <paramref name="action"/> with counting on, and returns what it did.</summary>
    /// <remarks>
    /// Restores the previous state in a <c>finally</c> so a throwing assertion — which is the normal
    /// outcome of half these tests — cannot leave counting enabled for the rest of the suite.
    /// </remarks>
    public static (long Nodes, long MemberLookups, long MatchProbes) Measure(Action action)
    {
        var wasEnabled = Enabled;
        Nodes = MemberLookups = MatchProbes = 0;
        Enabled = true;
        try
        {
            action();
        }
        finally
        {
            Enabled = wasEnabled;
        }
        return (Nodes, MemberLookups, MatchProbes);
    }
}
