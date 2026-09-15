using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using MP = MintPlayer.Assertions.AssertionExtensions;
using FA = FluentAssertions.AssertionExtensions;

namespace MintPlayer.Assertions.Benchmarks;

/// <summary>
/// The per-assertion cost of the PASSING path, for one representative assertion per family.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EquivalencyBenchmarks"/> measures the object-graph walker, which is where the
/// headline ~15×/~20× comes from. It is also, until this file existed, the <i>only</i> thing
/// measured — so the cost paid by every ordinary <c>Be</c>/<c>Contain</c>/<c>BeAfter</c> call in a
/// real suite was completely unobserved. A change that added an allocation to every passing
/// assertion in the library would not have moved a single number.
/// </para>
/// <para>
/// A real test suite executes overwhelmingly passing assertions, so this — not equivalency — is the
/// cost most users actually pay. Both libraries are measured on identical work.
/// </para>
/// <para>
/// The deterministic half of this boundary lives in <c>PassingPathAllocationTests</c> and fails the
/// build. This file is for wall-clock, which is too noisy in CI to gate on.
/// </para>
/// <para>
/// Extension methods are invoked through their static classes because both libraries define
/// <c>Should()</c> and the usings would collide.
/// </para>
/// </remarks>
/// <remarks>
/// Each family is its own logical group with its own FluentAssertions baseline, so the report reads
/// as one ratio per family. BenchmarkDotNet rejects more than one <c>Baseline = true</c> per group,
/// which is what <see cref="GroupBenchmarksByAttribute"/> with
/// <see cref="BenchmarkLogicalGroupRule.ByCategory"/> is establishing here — without it the run
/// fails outright rather than merely reporting oddly.
/// </remarks>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class PassingPathBenchmarks
{
    private const string Text = "the quick brown fox";
    private static readonly DateTime Timestamp = new(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Earlier = Timestamp.AddDays(-1);
    private static readonly int[] Numbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    // ---- numeric equality -------------------------------------------------------------------

    [BenchmarkCategory("Numeric"), Benchmark(Baseline = true)]
    public void FluentAssertions_Numeric_Be() => FA.Should(42).Be(42);

    [BenchmarkCategory("Numeric"), Benchmark]
    public void MintPlayerAssertions_Numeric_Be() => MP.Should(42).Be(42);

    // ---- numeric range: three template arguments, none rendered while passing ---------------

    [BenchmarkCategory("Range"), Benchmark(Baseline = true)]
    public void FluentAssertions_Numeric_BeInRange() => FA.Should(42).BeInRange(1, 100);

    [BenchmarkCategory("Range"), Benchmark]
    public void MintPlayerAssertions_Numeric_BeInRange() => MP.Should(42).BeInRange(1, 100);

    // ---- string equality --------------------------------------------------------------------

    [BenchmarkCategory("String"), Benchmark(Baseline = true)]
    public void FluentAssertions_String_Be() => FA.Should(Text).Be(Text);

    [BenchmarkCategory("String"), Benchmark]
    public void MintPlayerAssertions_String_Be() => MP.Should(Text).Be(Text);

    // ---- substring --------------------------------------------------------------------------

    [BenchmarkCategory("Contain"), Benchmark(Baseline = true)]
    public void FluentAssertions_String_Contain() => FA.Should(Text).Contain("brown");

    [BenchmarkCategory("Contain"), Benchmark]
    public void MintPlayerAssertions_String_Contain() => MP.Should(Text).Contain("brown");

    // ---- temporal ordering --------------------------------------------------------------------

    [BenchmarkCategory("DateTime"), Benchmark(Baseline = true)]
    public void FluentAssertions_DateTime_BeAfter() => FA.Should(Timestamp).BeAfter(Earlier);

    [BenchmarkCategory("DateTime"), Benchmark]
    public void MintPlayerAssertions_DateTime_BeAfter() => MP.Should(Timestamp).BeAfter(Earlier);

    // ---- collection membership ----------------------------------------------------------------

    [BenchmarkCategory("Collection"), Benchmark(Baseline = true)]
    public void FluentAssertions_Collection_Contain() => FA.Should(Numbers).Contain(7);

    [BenchmarkCategory("Collection"), Benchmark]
    public void MintPlayerAssertions_Collection_Contain() => MP.Should(Numbers).Contain(7);

    // ---- exceptions: the throw dominates, so this measures the wrapper around it ---------------

    [BenchmarkCategory("Exception"), Benchmark(Baseline = true)]
    public void FluentAssertions_Throw()
        => FA.Should(Thrower).Throw<InvalidOperationException>();

    [BenchmarkCategory("Exception"), Benchmark]
    public void MintPlayerAssertions_Throw()
        => MP.Should(Thrower).Throw<InvalidOperationException>();

    private static readonly Action Thrower = static () => throw new InvalidOperationException("boom");
}
