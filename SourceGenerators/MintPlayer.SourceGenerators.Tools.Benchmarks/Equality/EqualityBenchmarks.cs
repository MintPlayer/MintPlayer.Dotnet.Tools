using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;
using L = MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;
using G = MintPlayer.SourceGenerators.Tools.Benchmarks.Generated;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Equality;

/// <summary>
/// B1: one equality call per operation, Legacy against Generated, for one model shape.
/// </summary>
/// <remarks>
/// <para>
/// Each variant is called the way an incremental pipeline calls it. Legacy is the comparer
/// <c>ComparerRegistry.For&lt;T&gt;()</c> hands out (the generated <c>XValueComparer.Instance</c>) which is what
/// master's <c>.WithComparer()</c> passed to Roslyn. Generated is <see cref="EqualityComparer{T}.Default"/>, which
/// is what Roslyn uses when no comparer is passed, and which dispatches to the generated <c>IEquatable&lt;T&gt;</c>.
/// </para>
/// <para>
/// Legacy is the baseline of each category, so the Ratio column reads "Generated / Legacy".
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(EqualityConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public abstract class ShapeBenchmarks<TLegacy, TGenerated>
    where TLegacy : class
    where TGenerated : class
{
    private IEqualityComparer<TLegacy> legacy = null!;
    private IEqualityComparer<TGenerated> generated = null!;
    private TLegacy legacyA = null!, legacyB = null!, legacyDiff = null!;
    private TGenerated generatedA = null!, generatedB = null!, generatedDiff = null!;

    protected abstract TLegacy CreateLegacy(bool differInLast);
    protected abstract TGenerated CreateGenerated(bool differInLast);

    [GlobalSetup]
    public void Setup()
    {
        legacy = ComparerRegistry.For<TLegacy>();
        generated = EqualityComparer<TGenerated>.Default;
        legacyA = CreateLegacy(false); legacyB = CreateLegacy(false); legacyDiff = CreateLegacy(true);
        generatedA = CreateGenerated(false); generatedB = CreateGenerated(false); generatedDiff = CreateGenerated(true);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Equal")]
    public bool Legacy_Equal() => legacy.Equals(legacyA, legacyB);

    [Benchmark, BenchmarkCategory("Equal")]
    public bool Generated_Equal() => generated.Equals(generatedA, generatedB);

    [Benchmark(Baseline = true), BenchmarkCategory("LastDiffers")]
    public bool Legacy_LastDiffers() => legacy.Equals(legacyA, legacyDiff);

    [Benchmark, BenchmarkCategory("LastDiffers")]
    public bool Generated_LastDiffers() => generated.Equals(generatedA, generatedDiff);

    [Benchmark(Baseline = true), BenchmarkCategory("GetHashCode")]
    public int Legacy_GetHashCode() => legacy.GetHashCode(legacyA);

    [Benchmark, BenchmarkCategory("GetHashCode")]
    public int Generated_GetHashCode() => generated.GetHashCode(generatedA);
}

public class FlatStrings : ShapeBenchmarks<L.Flat, G.Flat>
{
    protected override L.Flat CreateLegacy(bool d) => Shapes.LegacyFlat(d);
    protected override G.Flat CreateGenerated(bool d) => Shapes.GeneratedFlat(d);
}

public class StringList20 : ShapeBenchmarks<L.StringList, G.StringList>
{
    protected override L.StringList CreateLegacy(bool d) => Shapes.LegacyStringList(d);
    protected override G.StringList CreateGenerated(bool d) => Shapes.GeneratedStringList(d);
}

public class ImmutableArray50 : ShapeBenchmarks<L.ChildArray, G.ChildArray>
{
    protected override L.ChildArray CreateLegacy(bool d) => Shapes.LegacyChildArray(d);
    protected override G.ChildArray CreateGenerated(bool d) => Shapes.GeneratedChildArray(d);
}

public class TupleProperty : ShapeBenchmarks<L.ChildPair, G.ChildPair>
{
    protected override L.ChildPair CreateLegacy(bool d) => Shapes.LegacyChildPair(d);
    protected override G.ChildPair CreateGenerated(bool d) => Shapes.GeneratedChildPair(d);
}

public class AbstractTree : ShapeBenchmarks<L.Node, G.Node>
{
    protected override L.Node CreateLegacy(bool d) => Shapes.LegacyTree(d);
    protected override G.Node CreateGenerated(bool d) => Shapes.GeneratedTree(d);
}

public class SparkLikeList : ShapeBenchmarks<L.SparkEntity, G.SparkEntity>
{
    protected override L.SparkEntity CreateLegacy(bool d) => Shapes.LegacySpark(d);
    protected override G.SparkEntity CreateGenerated(bool d) => Shapes.GeneratedSpark(d);
}
