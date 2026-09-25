using BenchmarkDotNet.Attributes;
using MintPlayer.SourceGenerators.Tools.Benchmarks.Generated;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Equality;

/// <summary>
/// B1: one call of the generated <c>IEquatable&lt;T&gt;</c> per operation, for one model shape.
/// </summary>
/// <remarks>
/// Each model is compared through <see cref="EqualityComparer{T}.Default"/>, which is what Roslyn uses in an
/// incremental pipeline when no comparer is passed, and which dispatches to the generated <c>Equals</c>.
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(EqualityConfig))]
public abstract class ShapeBenchmarks<T> where T : class
{
    private IEqualityComparer<T> comparer = null!;
    private T a = null!, b = null!, diff = null!;

    protected abstract T Create(bool differInLast);

    [GlobalSetup]
    public void Setup()
    {
        comparer = EqualityComparer<T>.Default;
        a = Create(false); b = Create(false); diff = Create(true);
    }

    [Benchmark]
    public bool Equal() => comparer.Equals(a, b);

    [Benchmark]
    public bool LastDiffers() => comparer.Equals(a, diff);

    [Benchmark]
    public int GetHashCodeOf() => comparer.GetHashCode(a);
}

public class FlatStrings : ShapeBenchmarks<Flat>
{
    protected override Flat Create(bool d) => Shapes.CreateFlat(d);
}

public class StringList20 : ShapeBenchmarks<StringList>
{
    protected override StringList Create(bool d) => Shapes.CreateStringList(d);
}

public class ImmutableArray50 : ShapeBenchmarks<ChildArray>
{
    protected override ChildArray Create(bool d) => Shapes.CreateChildArray(d);
}

public class TupleProperty : ShapeBenchmarks<ChildPair>
{
    protected override ChildPair Create(bool d) => Shapes.CreateChildPair(d);
}

public class AbstractTree : ShapeBenchmarks<Node>
{
    protected override Node Create(bool d) => Shapes.CreateTree(d);
}

public class SparkLikeList : ShapeBenchmarks<SparkEntity>
{
    protected override SparkEntity Create(bool d) => Shapes.CreateSpark(d);
}
