using System.Collections.Concurrent;
using System.Reflection;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

public static class ComparerRegistry
{
    private static readonly ConcurrentDictionary<Type, object> _byType = new();

    /// <summary>Types whose entry came from a <see cref="ValueComparerAttribute"/>, not an explicit registration.</summary>
    private static readonly ConcurrentDictionary<Type, bool> _fromAttribute = new();

    /// <summary>Types already inspected for a <see cref="ValueComparerAttribute"/> and found to have none.</summary>
    private static readonly ConcurrentDictionary<Type, bool> _noAttribute = new();

    public static void Register(Type type, object comparer)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (comparer is null) throw new ArgumentNullException(nameof(comparer));

        var expected = typeof(IEqualityComparer<>).MakeGenericType(type);
        if (!expected.IsInstanceOfType(comparer))
            throw new ArgumentException($"Comparer must implement {expected}.");

        _byType[type] = comparer;
        _fromAttribute.TryRemove(type, out _);
    }

    /// <returns>
    /// False if <paramref name="type"/> already has an explicit registration. A comparer that was only
    /// picked up from the type's <see cref="ValueComparerAttribute"/> is replaced: an explicit
    /// registration always wins, whichever happened first.
    /// </returns>
    public static bool TryRegister(Type type, object comparer)
    {
        if (type is null) return false;
        if (comparer is null) return false;

        var expected = typeof(IEqualityComparer<>).MakeGenericType(type);
        if (!expected.IsInstanceOfType(comparer))
            throw new ArgumentException($"Comparer must implement {expected}.");

        if (_byType.TryAdd(type, comparer))
            return true;

        if (!_fromAttribute.TryRemove(type, out _))
            return false; // already explicitly registered

        _byType[type] = comparer;
        return true;
    }

    /// <remarks>
    /// Falls back to the comparer named by <typeparamref name="T"/>'s <see cref="ValueComparerAttribute"/>.
    /// The ValueComparerGenerator puts that attribute on every <c>[AutoValueComparer]</c> type, and the
    /// hand-written models carry it too, but until this fallback existed nothing ever read it: nothing
    /// was registered, <see cref="For{T}"/> returned <see cref="EqualityComparer{T}.Default"/>, and every
    /// model class in every generator pipeline compared by reference. Each syntax transform allocates new
    /// models, so no pipeline step past it could ever be served from cache.
    /// </remarks>
    public static bool TryGet<T>(out IEqualityComparer<T> comparer)
    {
        if (_byType.TryGetValue(typeof(T), out var obj) || TryResolveFromAttribute(typeof(T), out obj))
        {
            comparer = (IEqualityComparer<T>)obj;
            return true;
        }
        comparer = default!;
        return false;
    }

    public static IEqualityComparer<T> For<T>()
        => TryGet<T>(out var comparer) ? comparer : EqualityComparer<T>.Default;

    private static bool TryResolveFromAttribute(Type type, out object comparer)
    {
        comparer = default!;
        if (_noAttribute.ContainsKey(type)) return false;

        var created = CreateFromAttribute(type);
        if (created is null)
        {
            _noAttribute.TryAdd(type, true);
            return false;
        }

        if (_byType.TryAdd(type, created))
            _fromAttribute.TryAdd(type, true);

        // Another thread, or an explicit registration, may have won the race; use whatever is there.
        comparer = _byType[type];
        return true;
    }

    private static object? CreateFromAttribute(Type type)
    {
        var attribute = type.GetCustomAttribute<ValueComparerAttribute>(inherit: true);
        var comparerType = attribute?.ComparerType;
        if (comparerType is null || comparerType.ContainsGenericParameters)
            return null;

        // The generated comparers expose a static Instance; the hand-written ones mostly do not, and
        // several are internal, so fall back to a non-public-capable constructor.
        var instance = comparerType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? Activator.CreateInstance(comparerType, nonPublic: true);

        // IEqualityComparer<in T> is contravariant, so a base type's comparer serves a derived type
        // that inherited the attribute. Anything else is a misconfiguration; ignore it rather than
        // throwing inside a generator pipeline.
        var expected = typeof(IEqualityComparer<>).MakeGenericType(type);
        return expected.IsInstanceOfType(instance) ? instance : null;
    }
}
