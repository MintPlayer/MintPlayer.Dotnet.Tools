namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

/// <summary>
/// Abstract base Value Comparer class
/// </summary>
public abstract class ValueComparer<T> : IEqualityComparer<T?>
{
    /// <summary>
    /// Register a custom comparer for <typeparamref name="T"/>, where you cannot use the <see cref="ValueComparerAttribute"/>.
    /// </summary>
    public static void RegisterCustomComparer<TComparer>() where TComparer : ValueComparer<T>
        => ValueComparerCache.AddCustomComparer<T, TComparer>();

    public static IEqualityComparer<T?> Instance { get; } = ValueComparerCache.GetComparer<T>();

    public static bool IsEquals<TValue>(TValue? x, TValue? y) => ValueComparerCache.GetComparer<TValue>().Equals(x, y);

    protected abstract bool AreEqual(T x, T y);

    public bool Equals(T? x, T? y)
    {
        if (x is null && y is null) // Note: When both are null then Value Equals
            return true;

        if (x is null ^ y is null) // Note: When one of the 2 is null then Value Unequals
            return false;

        return AreEqual(x!, y!); // Note: When both are not null
    }

    public virtual int GetHashCode(T? obj) => throw new NotImplementedException();
}
