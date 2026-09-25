// Vendored verbatim from master:SourceGenerators/MintPlayer.SourceGenerators.Tools/ValueComparers/ArrayValueComparer.cs
// for benchmark B1 (the "Legacy" variant). Only the namespace is changed. Do not fix or optimise it:
// the point is to measure what master ships.
namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;

internal sealed class ArrayValueComparer<TValue> : ValueComparer<TValue[]>
{
    protected override bool AreEqual(TValue[] x, TValue[] y)
    {
        if (!IsEquals(x.Length, y.Length))
            return false;

        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var i = 0; i < x.Length; i++)
        {
            if (!IsEquals(x[i], y[i]))
                return false;
        }

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, TValue[]? obj)
    {
        if (obj is null) { h.Add(0); return; }
        foreach (var item in obj)
            AddHash(ref h, item);
    }
}
