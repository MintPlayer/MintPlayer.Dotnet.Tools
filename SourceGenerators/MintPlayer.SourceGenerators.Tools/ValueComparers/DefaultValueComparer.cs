namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal sealed class DefaultValueComparer<T> : ValueComparer<T>
{
    protected override bool AreEqual(T x, T y)
    {
        return EqualityComparer<T>.Default.Equals(x, y);
    }
}