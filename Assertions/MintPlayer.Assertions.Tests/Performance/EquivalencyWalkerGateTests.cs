using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions.Tests.Performance;

/// <summary>
/// The gate on the equivalency walker — the thing the README's 9.29 µs / 14.83 KB measures.
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
    /// The walk must stay in the league the README claims. This is the loose alarm of the pair — it
    /// exists to catch a CHANGE OF SHAPE, not a regression of a few hundred bytes, which is what
    /// <see cref="TheDefaultWalkStaysUnderItsMeasuredByteCost"/> is for. The two are deliberately
    /// separate: a tight bound says "something was added", a loose one says "this is no longer the
    /// same algorithm", and a single threshold cannot say both.
    /// <para>
    /// 32 KB sits at 2.3× the measured 13,992 B and 12× below a reflection walk (~407 KB), so a
    /// silent fallback to reflection or a quadratic matcher trips it while ordinary drift does not.
    /// It was 100 KB, which let a 20% regression through without comment — see PRD §9.15.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWalkStaysFarUnderAReflectionWalkersCost()
    {
        var bytes = AllocationProbe.BytesPerOp(() => ((object)Actual).Should().BeEquivalentTo(Expected));

        Assert.True(bytes < 32 * 1024,
            $"The equivalency walk allocated {bytes:N0} B/op for the 5-type/4-level/20-item graph. "
            + "The measured figure is ~14 KB and FluentAssertions is ~407 KB, so this has stopped "
            + "being a fast path. See PRD §9.1 — the usual cause is a change that multiplied the "
            + "number of subtree comparisons.");
    }

    /// <summary>
    /// The regression that shipped once, expressed as a rule.
    /// </summary>
    /// <remarks>
    /// Matching an already-ordered collection unordered must cost very nearly what comparing it
    /// pairwise costs. Strict ordering is O(n) by construction, so a matcher that goes quadratic
    /// diverges from it visibly and deterministically. When the eager candidate matrix shipped, this
    /// ratio was 74×; it measures <b>1.04×</b> today, and the bound is 1.15× rather than the 3× it
    /// started at — 3× would have accepted a matcher three times more expensive than the algorithm
    /// it is supposed to match, which is not a gate.
    /// </remarks>
    [Fact]
    public void UnorderedMatchingOfAnAlreadyOrderedCollectionStaysNearStrictOrdering()
    {
        var strict = AllocationProbe.BytesPerOp(
            () => ((object)Actual).Should().BeEquivalentTo(Expected, o => o.WithStrictOrdering()));
        var unordered = AllocationProbe.BytesPerOp(
            () => ((object)Actual).Should().BeEquivalentTo(Expected));

        Assert.True(unordered <= strict * 1.15,
            $"Unordered matching allocated {unordered:N0} B/op against {strict:N0} B/op for strict "
            + $"ordering ({(double)unordered / strict:F2}×, measured 1.04× when this bound was set). "
            + "The matcher is doing work proportional "
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

    #region Member traits must cost nothing when nobody asks for them

    /// <summary>
    /// S1's pass condition, as a test. Adding <see cref="MemberTraits"/> to every accessor is only
    /// acceptable if a comparison that does not use them is unchanged — not "close", unchanged.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is what stops the traits from being folded into one table with a mask test in
    /// <c>CompareMembers</c>. That refactor looks tidier, passes every correctness test, and puts a
    /// per-member branch plus a longer member array on the hottest loop in the library — where
    /// <c>FindByName</c> is O(members²) per node. The default table must keep containing exactly the
    /// members it contained before traits existed.
    /// </remarks>
    /// <summary>
    /// The default walk's byte cost, pinned tightly rather than by the 100 KB smoke alarm above.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This is the test that caught the traits work's real cost, and the counters could not.</b>
    /// Adding <c>MemberTraits</c> cost <b>2,768 B/op</b> — a closure allocated at method entry in
    /// <c>EquivalencyRegistry.TryGetAccessors</c>, for a branch that returns on its first line and
    /// never reaches the lambda (see the comment there). The node and member-lookup counts were
    /// IDENTICAL before and after, because no extra work was done: the walk simply allocated a
    /// display class it never used, twice per structural node.
    /// <para>
    /// Hence the tight bound. A smoke alarm at 100 KB would have shrugged at a 20% regression, and
    /// an operation-count gate is structurally blind to allocation that accompanies no work. If this
    /// fails, measure the delta before touching the number — 14,100 is under 1% of headroom over the
    /// current 13,992, which is less than one allocation per node on this graph and therefore cannot
    /// hide a per-node cost at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDefaultWalkStaysUnderItsMeasuredByteCost()
    {
        var bytes = AllocationProbe.BytesPerOp(() => ((object)Actual).Should().BeEquivalentTo(Expected));

        Assert.True(bytes <= 14_100,
            $"The default equivalency walk allocated {bytes:N0} B/op; it measured 13,992 when this "
            + "bound was set. Something was added to the passing path. A capturing lambda anywhere "
            + "in a hot method is the cause that leaves the operation counts unchanged — the display "
            + "class is allocated at method entry, even on a path that returns before reaching it.");
    }

    [Fact]
    public void TraitsDoNotChangeTheDefaultWalk()
    {
        var counts = EquivalencyDiagnostics.Measure(
            () => ((object)Actual).Should().BeEquivalentTo(Expected));

        Assert.Equal((133L, 112L), (counts.Nodes, counts.MemberLookups));
    }

    /// <summary>
    /// Asking for a trait nothing in the graph carries must not change the walk either: the request
    /// is answered by table selection, once per type, not by work per node.
    /// </summary>
    [Fact]
    public void AskingForInternalMembersDoesNotChangeAGraphThatHasNone()
    {
        var counts = EquivalencyDiagnostics.Measure(
            () => ((object)Actual).Should().BeEquivalentTo(Expected, o => o.IncludingInternalMembers()));

        Assert.Equal((133L, 112L), (counts.Nodes, counts.MemberLookups));
    }

    #endregion
}
