namespace MintPlayer.Assertions.Tests.Performance;

/// <summary>
/// The rule this library is built on, as a test: <b>a passing assertion allocates nothing beyond the
/// <c>Should()</c> wrapper.</b>
/// </summary>
/// <remarks>
/// <para>
/// Measured <b>relative</b> to a bare <c>Should()</c> on the same subject, never as an absolute byte
/// count. That is deliberate: the wrapper is one object per chain and is not what these tests are
/// about, and a pinned absolute number drifts with unrelated changes until someone "fixes" it by
/// raising the constant. Relative states the actual rule.
/// </para>
/// <para>
/// The equivalency benchmark only ever covered <c>BeEquivalentTo</c>. Every string, numeric, date and
/// collection assertion — which is nearly all of the library and nearly all of what a suite runs —
/// was unmeasured and therefore unprotected. This is that coverage.
/// </para>
/// <para>
/// See <see cref="AllocationProbe"/> for why bytes rather than time, and for the warm-up and
/// two-sample mechanics that keep the number honest.
/// </para>
/// </remarks>
public class PassingPathAllocationTests
{
    /// <summary>
    /// Bytes an assertion is allowed to add over the bare <c>Should()</c> it is chained onto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 16 bytes is not "a bit of slack" — it is below one small object header, so nothing can hide
    /// under it. Every assertion covered here measures at or below the baseline.
    /// </para>
    /// <para>
    /// It took three fixes to get here, and the numbers are recorded because each was found by this
    /// test rather than by reading code. On a passing <c>42.Should().Be(42)</c>:
    /// <list type="bullet">
    /// <item><b>112 B/op</b> — <c>FailWith</c>'s <c>params object?[]</c>, built at the call site
    /// before the call and discarded when the condition held, with every value-type argument boxed
    /// into it. Fixed by arity-specific generic overloads, which every existing call site rebound
    /// to automatically.</item>
    /// <item><b>24 B/op</b> — <c>AndConstraint&lt;T&gt;</c> was a class, one allocation from each of
    /// ~360 assertion methods. Now a readonly struct.</item>
    /// <item><b>0</b>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ Raising this number is almost never the right response to a failure. It means an
    /// allocation came back; find it. See the message on <see cref="AssertNoExtraAllocation"/> for
    /// the three causes that actually occur.
    /// </para>
    /// </remarks>
    private const long AllowanceBytesPerOp = 16;

    /* KNOWN GAP, deliberately recorded rather than silently left.
     *
     * This covers one assertion per family, not every assertion. The families are what matter for
     * the shared machinery — Should(), FailWith, AndConstraint, Items — but a per-assertion cost
     * inside a single method is only caught if that method is listed here.
     *
     * A concrete example, found by adding IteratingTheSubjectAllocatesNothing: NotContainNulls
     * allocated `new List<int>()` unconditionally to hold indexes for a failure message, then threw
     * it away empty on every passing call. Ordinary-looking code; 32 B/op; invisible to review.
     *
     * The same shape — allocate a List<T> up front, fill it only on mismatch, assert Count == 0 —
     * appears in roughly ten more places across GenericCollectionAssertions and
     * GenericDictionaryAssertions (OnlyContain, OnlyHaveUniqueItems, BeSubsetOf, IntersectWith,
     * ContainKeys, NotContainKeys, ...). Each converts the same way: declare `List<T>? xs = null`,
     * use `(xs ??= []).Add(item)`, and test `xs is null`.
     *
     * They are not fixed here because ten near-identical edits made in one pass is the shape that
     * has twice introduced a compiler-invisible bug in this file (PRD 9.5). The right order is: add
     * the gate test for an assertion, watch it fail, fix that one, move on. */

    private static void AssertNoExtraAllocation(string what, Action bare, Action full)
    {
        var baseline = AllocationProbe.BytesPerOp(bare);
        var measured = AllocationProbe.BytesPerOp(full);
        var added = measured - baseline;

        Assert.True(added <= AllowanceBytesPerOp,
            $"{what} added {added:N0} B/op over a bare Should() ({measured:N0} vs {baseline:N0}). "
            + "A passing assertion must not allocate. Usual causes: a boxed enumerator from "
            + "iterating an interface-typed collection (MPA0005), a value type boxed into FailWith's "
            + "params array, or a copy of an already-materialised subject.");
    }

    #region Scalars

    [Fact]
    public void NumericAssertionAllocatesNothing()
    {
        var value = 42;
        AssertNoExtraAllocation("Be(int)", () => value.Should(), () => value.Should().Be(42));
    }

    [Fact]
    public void StringAssertionAllocatesNothing()
    {
        var value = "hello world";
        AssertNoExtraAllocation("Be(string)", () => value.Should(), () => value.Should().Be("hello world"));
    }

    [Fact]
    public void BooleanAssertionAllocatesNothing()
    {
        var value = true;
        AssertNoExtraAllocation("BeTrue()", () => value.Should(), () => value.Should().BeTrue());
    }

    [Fact]
    public void DateTimeAssertionAllocatesNothing()
    {
        var value = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        AssertNoExtraAllocation("Be(DateTime)", () => value.Should(), () => value.Should().Be(value));
    }

    #endregion

    #region Collections — the subject must not be copied

    /// <summary>
    /// An array subject must not be copied. <c>Items</c> used to do <c>[.. Subject]</c>
    /// unconditionally, so every collection assertion allocated a fresh array and a wrapper for a
    /// subject that was already contiguous.
    /// </summary>
    [Fact]
    public void CountingAnArrayAllocatesNothing()
    {
        int[] subject = [1, 2, 3, 4, 5, 6, 7, 8];
        AssertNoExtraAllocation("HaveCount() over T[]", () => subject.Should(), () => subject.Should().HaveCount(8));
    }

    /// <summary>The same for a <c>List&lt;T&gt;</c>, which reaches a span via CollectionsMarshal.</summary>
    [Fact]
    public void CountingAListAllocatesNothing()
    {
        List<int> subject = [1, 2, 3, 4, 5, 6, 7, 8];
        AssertNoExtraAllocation("HaveCount() over List<T>", () => subject.Should(), () => subject.Should().HaveCount(8));
    }

    /// <summary>
    /// Iterating the subject is where the boxed enumerator lived — 16 loops in the collection
    /// assertions alone, each one allocation per call and invisible in the source.
    /// </summary>
    [Fact]
    public void IteratingTheSubjectAllocatesNothing()
    {
        int[] subject = [1, 2, 3, 4, 5, 6, 7, 8];
        AssertNoExtraAllocation("NotContainNulls() over T[]",
            () => subject.Should(), () => subject.Should().NotContainNulls());
    }

    [Fact]
    public void EmptinessChecksAllocateNothing()
    {
        int[] subject = [1, 2, 3];
        AssertNoExtraAllocation("NotBeEmpty() over T[]", () => subject.Should(), () => subject.Should().NotBeEmpty());
    }

    /// <summary>
    /// The predicate assertions whose failure-detail list is collected lazily.
    /// </summary>
    [Fact]
    public void PredicateAssertionsAllocateNothingWhenNothingMatches()
    {
        int[] subject = [1, 2, 3, 4, 5, 6, 7, 8];
        AssertNoExtraAllocation("NotContain(predicate)",
            () => subject.Should(), () => subject.Should().NotContain(x => x > 100));
        AssertNoExtraAllocation("OnlyContain(predicate)",
            () => subject.Should(), () => subject.Should().OnlyContain(x => x > 0));
    }

    /// <summary>
    /// <c>ContainSingle</c> passes on exactly ONE match, so it cannot use the lazy-list trick its
    /// siblings do — a lazy list would still be allocated on every successful call. It counts
    /// instead, and collects only in the failing branch.
    /// </summary>
    /// <remarks>
    /// ⚠️ This test exists because the obvious "simplification" — collecting matches into a list and
    /// testing <c>Count == 1</c> — passes every behavioural test in the suite while allocating on
    /// the passing path. Only a byte count can tell the two apart.
    /// </remarks>
    [Fact]
    public void ContainSingleAllocatesNothingWhenItSucceeds()
    {
        int[] subject = [1, 2, 3, 4, 5, 6, 7, 8];
        AssertNoExtraAllocation("ContainSingle(predicate)",
            () => subject.Should(), () => subject.Should().ContainSingle(x => x == 5));
    }

    /// <summary>
    /// <c>NotContainSingle</c> is the subtler one: it passes when the count is anything other than
    /// one, <b>including two or more</b>. A lazy list would therefore be allocated AND populated on
    /// a successful call over a collection where several items match — which is the case asserted
    /// here deliberately.
    /// </summary>
    [Fact]
    public void NotContainSingleAllocatesNothingEvenWhenManyMatch()
    {
        int[] subject = [1, 2, 3, 4, 5, 6, 7, 8];
        AssertNoExtraAllocation("NotContainSingle(predicate), many matches",
            () => subject.Should(), () => subject.Should().NotContainSingle(x => x > 2));
    }

    #endregion

    #region Scopes

    /// <summary>
    /// Inside a scope the failure funnel reads an <c>AsyncLocal</c>, but only when something fails —
    /// a passing assertion must not pay for the scope it happens to be inside.
    /// </summary>
    [Fact]
    public void AssertionInsideAScopeAllocatesNothingExtra()
    {
        var value = 42;
        using var scope = new AssertionScope();
        AssertNoExtraAllocation("Be(int) inside a scope", () => value.Should(), () => value.Should().Be(42));
    }

    #endregion
}
