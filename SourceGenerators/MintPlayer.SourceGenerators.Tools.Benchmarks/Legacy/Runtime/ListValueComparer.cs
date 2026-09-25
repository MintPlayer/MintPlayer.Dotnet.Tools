// Vendored verbatim from master:SourceGenerators/MintPlayer.SourceGenerators.Tools/ValueComparers/ListValueComparer.cs
// for benchmark B1 (the "Legacy" variant). Only the namespace is changed. Do not fix or optimise it:
// the point is to measure what master ships.
namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;

internal sealed class ListValueComparer<TValue> : ValueComparer<List<TValue>>
{
    protected override bool AreEqual(List<TValue> x, List<TValue> y)
    {
        if (!IsEquals(x.Count, y.Count))
            return false;

        for (var i = 0; i < x.Count; i++)
        {
            if (!IsEquals(x[i], y[i]))
                return false;
        }

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, List<TValue>? obj)
    {
        if (obj is null) { h.Add(0); return; }
        foreach (var item in obj)
            AddHash(ref h, item);
    }
}