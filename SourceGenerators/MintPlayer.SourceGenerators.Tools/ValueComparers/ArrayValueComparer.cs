namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

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
}
