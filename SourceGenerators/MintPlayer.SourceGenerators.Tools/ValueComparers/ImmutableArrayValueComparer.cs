using MintPlayer.SourceGenerators.Tools.Polyfills;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

/// <summary>
/// Element-wise comparison for <see cref="ImmutableArray{T}"/>, whose own equality compares the
/// underlying array by reference. That is what the output of <c>Collect()</c> is, so without this a
/// collected step can never compare equal to its previous run.
/// </summary>
internal sealed class ImmutableArrayValueComparer<TValue> : ValueComparer<ImmutableArray<TValue>>
{
    protected override bool AreEqual(ImmutableArray<TValue> x, ImmutableArray<TValue> y)
    {
        if (x.IsDefault || y.IsDefault)
            return x.IsDefault == y.IsDefault;

        if (x.Length != y.Length)
            return false;

        for (var i = 0; i < x.Length; i++)
        {
            if (!IsEquals(x[i], y[i]))
                return false;
        }

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, ImmutableArray<TValue> obj)
    {
        if (obj.IsDefault) { h.Add(0); return; }
        foreach (var item in obj)
            AddHash(ref h, item);
    }
}
