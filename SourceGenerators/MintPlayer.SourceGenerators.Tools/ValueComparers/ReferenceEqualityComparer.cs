using System.Runtime.CompilerServices;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly ReferenceEqualityComparer<T> Instance = new();

    private ReferenceEqualityComparer() { }

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}

internal static class ReferenceEqualityComparer
{
    public static IEqualityComparer<object> Instance => ReferenceEqualityComparer<object>.Instance;
}