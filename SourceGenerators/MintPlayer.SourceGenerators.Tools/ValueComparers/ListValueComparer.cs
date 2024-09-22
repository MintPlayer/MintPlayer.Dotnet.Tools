using System.Collections.Generic;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

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
}