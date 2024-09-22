namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal sealed class IReadOnlyCollectionValueComparer<TValue> : ValueComparer<IReadOnlyCollection<TValue>>
{
    protected override bool AreEqual(IReadOnlyCollection<TValue> x, IReadOnlyCollection<TValue> y)
    {
        if (!IsEquals(x.Count, y.Count))
            return false;

        using var enumX = x.GetEnumerator();
        using var enumY = y.GetEnumerator();

        while (true)
        {
            bool moveNextX = enumX.MoveNext();
            bool moveNextY = enumY.MoveNext();

            if (moveNextX != moveNextY)
                return false;

            if (!moveNextX)
                return true;

            if (!IsEquals(enumX.Current, enumY.Current))
                return false;
        }
    }
}
