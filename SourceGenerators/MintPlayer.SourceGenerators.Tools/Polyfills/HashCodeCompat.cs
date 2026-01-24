// Polyfill for System.HashCode (missing on .NET Standard 2.0)
namespace MintPlayer.SourceGenerators.Tools.Polyfills
{
#if NETSTANDARD2_0
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
#else
    // If you ever multi-target later, you can alias to the real one:
    global using HashCodeCompat = System.HashCode;
#endif
}
