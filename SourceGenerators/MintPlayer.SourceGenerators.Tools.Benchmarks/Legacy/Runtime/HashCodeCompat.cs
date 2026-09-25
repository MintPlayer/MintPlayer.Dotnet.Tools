// Vendored from master:SourceGenerators/MintPlayer.SourceGenerators.Tools/Polyfills/HashCodeCompat.cs for
// benchmark B1. Master's Tools targets netstandard2.0 only, so the shipped package always compiles the struct
// below (the #else alias to System.HashCode is dead code there). The #if is dropped so this project measures
// the struct the generators actually run with, on net11.0 as well as on net481.
namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;

public struct HashCodeCompat
{
    private int _hash;

    public void Add<T>(T value) =>
        _hash = Combine(_hash, value?.GetHashCode() ?? 0);

    public void Add<T>(T value, IEqualityComparer<T>? comparer) =>
        _hash = Combine(_hash,
            comparer is not null ? comparer.GetHashCode(value!) :
            value?.GetHashCode() ?? 0);

    public static int Combine(int h1, int h2)
    {
        unchecked
        {
            // Simple rotation-xor mix (matches built-in HashCode pattern)
            var rol5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
            return ((int)rol5 + h1) ^ h2;
        }
    }

    public int ToHashCode() => _hash;
}
