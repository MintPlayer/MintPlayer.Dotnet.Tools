using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions.Tests.Performance;

/// <summary>
/// The gate on the equivalency walker — the thing the README's 13.13 µs / 20.34 KB measures.
/// </summary>
/// <remarks>
/// Two independent instruments, because neither alone is sufficient:
/// <list type="bullet">
/// <item><b>Allocated bytes</b> catch anything that adds an object to the walk. Deterministic, so
/// they work on a loaded CI runner where wall-clock does not.</item>
/// <item><b>Operation counts</b> catch work that allocates nothing — a stack-allocated matrix, a
/// cache lookup that became linear, an extra pass over members. This is the hole an allocation-only
/// gate structurally cannot see, and it is exactly the hole a "make it correct" change falls into.</item>
/// </list>
/// The counts are asserted <b>exactly</b>. That is deliberate: an exact number is a executable
/// statement of the algorithm's complexity, and a range would hide the 2× that matters.
/// </remarks>
public class EquivalencyWalkerGateTests
{
    private static readonly BenchmarkGraph.Order Actual = BenchmarkGraph.Create();
    private static readonly BenchmarkGraph.Order Expected = BenchmarkGraph.Create();

    #region Allocation

    /// <summary>
    /// The walk must stay in the league the README claims. 100 KB is a smoke alarm, not a
    /// thermostat: the measured figure is ~20 KB and a reflection walker is ~407 KB, so anything
    /// past this has changed the shape of the walk rather than tuned it.
    /// </summary>
    [Fact]
    public void TheWalkStaysFarUnderAReflectionWalkersCost()
    {
        var bytes = AllocationProbe.BytesPerOp(() => ((object)Actual).Should().BeEquivalentTo(Expected));

        Assert.True(bytes < 100 * 1024,
            $"The equivalency walk allocated {bytes:N0} B/op for the 5-type/4-level/20-item graph. "
            + "The README baseline is ~20 KB and FluentAssertions is ~407 KB, so this has stopped "
            + "being a fast path. See PRD §9.1 — the usual cause is a change that multiplied the "
            + "number of subtree comparisons.");
    }

    /// <summary>
    /// The regression that shipped once, expressed as a rule.
    /// </summary>
    /// <remarks>
    /// Matching an already-ordered collection unordered must stay in the same league as comparing it
    /// pairwise. Strict ordering is O(n) by construction, so a matcher that goes quadratic diverges
    /// from it visibly and deterministically. When the eager candidate matrix shipped, this ratio
    /// was 74×.
    /// </remarks>
    [Fact]
    public void UnorderedMatchingOfAnAlreadyOrderedCollectionStaysNearStrictOrdering()
    {
        var strict = AllocationProbe.BytesPerOp(
            () => ((object)Actual).Should().BeEquivalentTo(Expected, o => o.WithStrictOrdering()));
        var unordered = AllocationProbe.BytesPerOp(
            () => ((object)Actual).Should().BeEquivalentTo(Expected));

        Assert.True(unordered <= strict * 3,
            $"Unordered matching allocated {unordered:N0} B/op against {strict:N0} B/op for strict "
            + $"ordering ({(double)unordered / strict:F1}×). The matcher is doing work proportional "
            + "to n² on a collection whose first candidate already fits. See PRD §9.1.");
    }

    #endregion

    #region Operation counts — the time-only regressions bytes cannot see

    /// <summary>
    /// An already-aligned 20-item collection must cost a LINEAR number of match probes.
    /// </summary>
    /// <remarks>
    /// This is the assertion that would have caught the eager candidate matrix immediately: it
    /// probed 400 times (20×20) where the aligned case needs 20. The exact number is asserted
    /// because the whole point is to notice the difference between n and n².
    /// </remarks>
    [Fact]
    public void AnAlignedCollectionCostsOneProbePerItem()
    {
        var counts = EquivalencyDiagnostics.Measure(
            () => ((object)Actual).Should().BeEquivalentTo(Expected));

        Assert.Equal(20, counts.MatchProbes);
    }

    /// <summary>
    /// The node count for a fixed graph is a fixed number. Anything that changes it changed the
    /// shape of the walk, and the test says so before anyone has to measure time.
    /// </summary>
    [Fact]
    public void TheNodeCountForTheBenchmarkGraphIsExact()
    {
        var counts = EquivalencyDiagnostics.Measure(
            () => ((object)Actual).Should().BeEquivalentTo(Expected));

        // Pinned, not derived: the value IS the assertion. Deriving it here would restate the
        // walker and change in lockstep with the bug it is meant to catch.
        //
        // ⚠️ If this fails, DO NOT just update the number. It means the walk changed shape, and the
        // question is which of these happened:
        //   - a feature legitimately visits more nodes           -> update, and say so in the commit
        //   - an algorithm went from linear to quadratic         -> that is the bug this exists for
        //   - the counters leaked across threads                 -> they are [ThreadStatic]; if that
        //     was removed, this reads roughly double (259 was observed where the graph has 133)
        //
        // Updating the number without answering that turns the only gate that can see a time-only
        // regression into a rubber stamp.
        Assert.Equal((133L, 112L), (counts.Nodes, counts.MemberLookups));
    }

    /// <summary>
    /// Strict ordering must cost strictly fewer probes than unordered matching — zero, in fact,
    /// since it compares pairwise and never probes for a match at all.
    /// </summary>
    [Fact]
    public void StrictOrderingDoesNotProbe()
    {
        var counts = EquivalencyDiagnostics.Measure(
            () => ((object)Actual).Should().BeEquivalentTo(Expected, o => o.WithStrictOrdering()));

        Assert.Equal(0, counts.MatchProbes);
    }

    /// <summary>
    /// The counters must not leak across tests — a throwing assertion is the normal outcome of half
    /// this suite, and counting left enabled would make every later measurement wrong.
    /// </summary>
    [Fact]
    public void CountingIsDisabledAgainAfterAThrowingAssertion()
    {
        Assert.ThrowsAny<Exception>(() => EquivalencyDiagnostics.Measure(
            () => ((object)Actual).Should().BeEquivalentTo(new { Id = -1 })));

        Assert.False(EquivalencyDiagnostics.Enabled);
    }

    #endregion
}
