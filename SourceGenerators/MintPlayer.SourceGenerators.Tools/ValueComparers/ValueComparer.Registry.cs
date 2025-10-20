using System.Collections.Concurrent;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers
{
    public static class ComparerRegistry
    {
        private static readonly ConcurrentDictionary<Type, object> _byType = new();

        public static void Register(Type type, object comparer)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (comparer is null) throw new ArgumentNullException(nameof(comparer));

            var expected = typeof(IEqualityComparer<>).MakeGenericType(type);
            if (!expected.IsInstanceOfType(comparer))
                throw new ArgumentException($"Comparer must implement {expected}.");

            _byType[type] = comparer;
        }

        public static bool TryGet<T>(out IEqualityComparer<T> comparer)
        {
            if (_byType.TryGetValue(typeof(T), out var obj))
            {
                comparer = (IEqualityComparer<T>)obj;
                return true;
            }
            comparer = default!;
            return false;
        }
    }
}
