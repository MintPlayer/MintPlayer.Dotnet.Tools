// Vendored verbatim from master:SourceGenerators/MintPlayer.SourceGenerators.Tools/ValueComparers/ValueTupleValueComparer.cs
// for benchmark B1 (the "Legacy" variant). Only the namespace is changed. Do not fix or optimise it:
// the point is to measure what master ships.
namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;

internal sealed class ValueTupleValueComparer<T1, T2> : ValueComparer<(T1, T2)>
{
    protected override bool AreEqual((T1, T2) x, (T1, T2) y)
    {
        if (!IsEquals(x.Item1, y.Item1))
            return false;

        if (!IsEquals(x.Item2, y.Item2))
            return false;

        return true;
    }

    // Hash the items the way AreEqual compares them. The default hashes the tuple itself, which
    // falls back to each item's own GetHashCode — by reference for a model class — so two tuples
    // this comparer calls equal could hash differently.
    protected override void AddHash(ref HashCodeCompat h, (T1, T2) obj)
    {
        AddHash(ref h, obj.Item1);
        AddHash(ref h, obj.Item2);
    }
}

internal sealed class ValueTupleValueComparer<T1, T2, T3> : ValueComparer<(T1, T2, T3)>
{
    protected override bool AreEqual((T1, T2, T3) x, (T1, T2, T3) y)
    {
        if (!IsEquals(x.Item1, y.Item1))
            return false;

        if (!IsEquals(x.Item2, y.Item2))
            return false;

        if (!IsEquals(x.Item3, y.Item3))
            return false;

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, (T1, T2, T3) obj)
    {
        AddHash(ref h, obj.Item1);
        AddHash(ref h, obj.Item2);
        AddHash(ref h, obj.Item3);
    }
}

internal sealed class ValueTupleValueComparer<T1, T2, T3, T4> : ValueComparer<(T1, T2, T3, T4)>
{
    protected override bool AreEqual((T1, T2, T3, T4) x, (T1, T2, T3, T4) y)
    {
        if (!IsEquals(x.Item1, y.Item1))
            return false;

        if (!IsEquals(x.Item2, y.Item2))
            return false;

        if (!IsEquals(x.Item3, y.Item3))
            return false;

        if (!IsEquals(x.Item4, y.Item4))
            return false;

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, (T1, T2, T3, T4) obj)
    {
        AddHash(ref h, obj.Item1);
        AddHash(ref h, obj.Item2);
        AddHash(ref h, obj.Item3);
        AddHash(ref h, obj.Item4);
    }
}

internal sealed class ValueTupleValueComparer<T1, T2, T3, T4, T5> : ValueComparer<(T1, T2, T3, T4, T5)>
{
    protected override bool AreEqual((T1, T2, T3, T4, T5) x, (T1, T2, T3, T4, T5) y)
    {
        if (!IsEquals(x.Item1, y.Item1))
            return false;

        if (!IsEquals(x.Item2, y.Item2))
            return false;

        if (!IsEquals(x.Item3, y.Item3))
            return false;

        if (!IsEquals(x.Item4, y.Item4))
            return false;
        
        if (!IsEquals(x.Item5, y.Item5))
            return false;

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, (T1, T2, T3, T4, T5) obj)
    {
        AddHash(ref h, obj.Item1);
        AddHash(ref h, obj.Item2);
        AddHash(ref h, obj.Item3);
        AddHash(ref h, obj.Item4);
        AddHash(ref h, obj.Item5);
    }
}

internal sealed class ValueTupleValueComparer<T1, T2, T3, T4, T5, T6> : ValueComparer<(T1, T2, T3, T4, T5, T6)>
{
    protected override bool AreEqual((T1, T2, T3, T4, T5, T6) x, (T1, T2, T3, T4, T5, T6) y)
    {
        if (!IsEquals(x.Item1, y.Item1))
            return false;

        if (!IsEquals(x.Item2, y.Item2))
            return false;

        if (!IsEquals(x.Item3, y.Item3))
            return false;

        if (!IsEquals(x.Item4, y.Item4))
            return false;
        
        if (!IsEquals(x.Item5, y.Item5))
            return false;
        
        if (!IsEquals(x.Item6, y.Item6))
            return false;

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, (T1, T2, T3, T4, T5, T6) obj)
    {
        AddHash(ref h, obj.Item1);
        AddHash(ref h, obj.Item2);
        AddHash(ref h, obj.Item3);
        AddHash(ref h, obj.Item4);
        AddHash(ref h, obj.Item5);
        AddHash(ref h, obj.Item6);
    }
}