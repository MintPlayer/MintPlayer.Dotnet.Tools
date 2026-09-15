using System.Runtime.CompilerServices;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The enforceable half of the "no performance loss" boundary: a passing assertion must not allocate.
/// </summary>
/// <remarks>
/// <para>
/// Phase 2's hard boundary is stated operationally as <b>nothing may be added to the passing path</b>.
/// Until this file existed, that was unenforceable — the only benchmark covered
/// <c>BeEquivalentTo</c> and was explicitly not gating, so a change that added an allocation to every
/// passing assertion in the library would have been caught by nothing at all.
/// </para>
/// <para>
/// Allocation is measured rather than time because it is <b>deterministic</b>. Wall-clock in CI is
/// noise, and a timing gate would either be so loose it catches nothing or so tight it fails at
/// random. Bytes allocated per operation do not vary with machine load, so they can fail a build
/// honestly. The benchmarks remain for wall-clock; this is the gate.
/// </para>
/// <para>
/// Each test measures a bare <c>Should()</c> as its own baseline and asserts the assertion call adds
/// nothing on top. That is deliberately relative: it isolates the cost of the <i>assertion</i> from
/// the cost of the subject wrapper, so the test states the rule ("the assertion adds nothing")
/// instead of pinning a byte count that drifts with unrelated changes.
/// </para>
/// </remarks>
public class PassingPathAllocationTests
{
    /// <summary>Iterations to average over. High enough that per-op noise rounds away.</summary>
    private const int Iterations = 20_000;

    /// <summary>
    /// Warm-up iterations, run before measuring. Tiered compilation promotes a method after ~30
    /// calls and the promoted code can allocate differently from tier-0; measuring before that
    /// settles produces a number that has nothing to do with the code under test.
    /// </summary>
    private const int WarmUp = 2_000;

    /// <summary>
    /// Bytes per operation the assertion may add over its baseline. Zero is the intent; a small
    /// allowance absorbs the odd boxed enum or interned literal without letting a per-call array or
    /// a closure through — those cost tens of bytes each and would blow past this immediately.
    /// </summary>
    private const long AllowanceBytesPerOp = 8;

    /// <summary>Consumes results so nothing under test can be optimised away as dead code.</summary>
    private static volatile object? sink;

    [Fact]
    public void ANumericAssertionAddsNothing()
        => AssertAddsNothing(
            baseline: static () => sink = 42.Should(),
            measured: static () => sink = 42.Should().Be(42).And);

    [Fact]
    public void AStringAssertionAddsNothing()
        => AssertAddsNothing(
            baseline: static () => sink = "abc".Should(),
            measured: static () => sink = "abc".Should().Be("abc").And);

    [Fact]
    public void AStringContainAssertionAddsNothing()
        => AssertAddsNothing(
            baseline: static () => sink = "the quick brown fox".Should(),
            measured: static () => sink = "the quick brown fox".Should().Contain("brown").And);

    [Fact]
    public void ABooleanAssertionAddsNothing()
        => AssertAddsNothing(
            baseline: static () => sink = true.Should(),
            measured: static () => sink = true.Should().BeTrue().And);

    [Fact]
    public void ADateTimeAssertionAddsNothing()
    {
        var subject = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
        var earlier = subject.AddDays(-1);

        AssertAddsNothing(
            baseline: () => sink = subject.Should(),
            measured: () => sink = subject.Should().BeAfter(earlier).And);
    }

    // Deliberately NOT covered here, and why:
    //
    // - Exception assertions. `Should().Throw<T>()` allocates the exception itself, which dominates
    //   and is the caller's cost, not the library's. "Adds nothing" is the wrong question; the right
    //   one is a ceiling, which would be brittle. Covered by the benchmarks instead.
    // - Collection assertions. A collection subject is materialised (at most once per assertions
    //   instance, cached in a field) so `.And.`-chains and lazy sequences are not re-enumerated.
    //   Whether that materialisation allocates for an already-materialised array is a real question
    //   worth settling — but it is an EXISTING cost, so pinning it before measuring it would either
    //   bake in a regression or ship a red test. Settle it, then add the test.

    /// <summary>
    /// A passing assertion inside an <see cref="AssertionScope"/> must not allocate either — the
    /// scope collects failures, and there are none to collect on this path.
    /// </summary>
    [Fact]
    public void AnAssertionInsideAScopeAddsNothing()
        => AssertAddsNothing(
            baseline: static () =>
            {
                using var scope = new AssertionScope();
                sink = 42.Should();
            },
            measured: static () =>
            {
                using var scope = new AssertionScope();
                sink = 42.Should().Be(42).And;
            });

    /// <summary>
    /// Guards the specific regression this boundary was written for: <c>FailWith</c>'s
    /// <c>params object?[]</c> overload allocates its array at the CALL SITE, so before the generic
    /// overloads existed every passing assertion paid for a message it never rendered. If someone
    /// removes those overloads, the per-assertion tests above go red — this one explains why.
    /// </summary>
    [Fact]
    public void AMultiArgumentFailureTemplateDoesNotAllocateWhilePassing()
        => AssertAddsNothing(
            baseline: static () => sink = 42.Should(),
            // BeInRange renders three arguments on failure; passing must still cost nothing.
            measured: static () => sink = 42.Should().BeInRange(1, 100).And);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertAddsNothing(Action baseline, Action measured)
    {
        // Two independent samples, lower wins. A background GC or a lazily-initialised static that
        // lands inside one measurement window inflates that sample; it will not inflate both, so the
        // minimum is the honest reading. Without this the test flakes on a loaded machine.
        var first = BytesPerOp(measured) - BytesPerOp(baseline);
        var second = BytesPerOp(measured) - BytesPerOp(baseline);
        var overhead = Math.Min(first, second);

        Assert.True(
            overhead <= AllowanceBytesPerOp,
            $"A passing assertion allocated {overhead} bytes/op more than a bare Should(), over the " +
            $"allowance of {AllowanceBytesPerOp}. Something was added to the passing path. The usual " +
            $"causes are a params array or closure at the call site, an eagerly built failure " +
            $"message, or a value returned by reference type where a struct would do. See " +
            $"Assertions/prd/Assertions-phase2-prd.md §0.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long BytesPerOp(Action op)
    {
        for (var i = 0; i < WarmUp; i++) op();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++) op();
        var after = GC.GetAllocatedBytesForCurrentThread();

        return (after - before) / Iterations;
    }
}
