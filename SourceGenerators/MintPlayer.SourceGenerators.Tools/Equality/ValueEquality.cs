using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// Reflection-free structural equality for the collection shapes that incremental-pipeline models carry.
/// The code generated for <c>[GenerateEquality]</c> calls into this class; hand-written
/// <see cref="IEquatable{T}"/> implementations can use it too.
/// </summary>
/// <remarks>
/// Every sequence comparison is order-sensitive and element-wise, and compares by the <em>declared</em>
/// shape: an <see cref="IReadOnlyList{T}"/> backed by a <c>T[]</c> equals one backed by a <see cref="List{T}"/>.
/// Dictionaries compare order-insensitively. Hashes include every element, so equal values hash equally.
/// Elements compare through <see cref="EqualityComparer{T}.Default"/> unless an element comparer is passed,
/// which is how nested collections compose (<c>ImmutableArray&lt;ImmutableArray&lt;T&gt;&gt;</c>).
/// </remarks>
public static class ValueEquality
{
    private const int Seed = 17;
    private const int Factor = 31;

    /// <summary>Folds <paramref name="value"/> into the running hash <paramref name="hash"/>.</summary>
    public static int Combine(int hash, int value) => unchecked(hash * Factor + value);

    #region Arrays
    public static bool Array<T>(T[]? x, T[]? y, IEqualityComparer<T>? comparer = null)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Length != y.Length) return false;
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < x.Length; i++)
            if (!comparer.Equals(x[i], y[i])) return false;
        return true;
    }

    public static int ArrayHash<T>(T[]? x, IEqualityComparer<T>? comparer = null)
    {
        if (x is null) return 0;
        comparer ??= EqualityComparer<T>.Default;
        var hash = Seed;
        for (var i = 0; i < x.Length; i++)
            hash = Combine(hash, x[i] is null ? 0 : comparer.GetHashCode(x[i]!));
        return hash;
    }
    #endregion

    #region ImmutableArray
    /// <summary>A default array equals only another default array; it never equals an empty one.</summary>
    public static bool ImmutableArray<T>(ImmutableArray<T> x, ImmutableArray<T> y, IEqualityComparer<T>? comparer = null)
    {
        if (x.IsDefault || y.IsDefault) return x.IsDefault == y.IsDefault;
        if (x.Length != y.Length) return false;
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < x.Length; i++)
            if (!comparer.Equals(x[i], y[i])) return false;
        return true;
    }

    public static int ImmutableArrayHash<T>(ImmutableArray<T> x, IEqualityComparer<T>? comparer = null)
    {
        if (x.IsDefault) return 0;
        comparer ??= EqualityComparer<T>.Default;
        var hash = Seed;
        for (var i = 0; i < x.Length; i++)
            hash = Combine(hash, x[i] is null ? 0 : comparer.GetHashCode(x[i]!));
        return hash;
    }
    #endregion

    #region IReadOnlyList
    /// <summary>For <see cref="List{T}"/>, <see cref="IReadOnlyList{T}"/> and anything else indexable.</summary>
    public static bool List<T>(IReadOnlyList<T>? x, IReadOnlyList<T>? y, IEqualityComparer<T>? comparer = null)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Count != y.Count) return false;
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < x.Count; i++)
            if (!comparer.Equals(x[i], y[i])) return false;
        return true;
    }

    public static int ListHash<T>(IReadOnlyList<T>? x, IEqualityComparer<T>? comparer = null)
    {
        if (x is null) return 0;
        comparer ??= EqualityComparer<T>.Default;
        var hash = Seed;
        for (var i = 0; i < x.Count; i++)
        {
            var item = x[i];
            hash = Combine(hash, item is null ? 0 : comparer.GetHashCode(item));
        }
        return hash;
    }
    #endregion

    #region IEnumerable
    /// <summary>For <see cref="IEnumerable{T}"/>, <see cref="IList{T}"/>, <see cref="ICollection{T}"/> and other sequences.</summary>
    public static bool Sequence<T>(IEnumerable<T>? x, IEnumerable<T>? y, IEqualityComparer<T>? comparer = null)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        if (x is IReadOnlyList<T> xl && y is IReadOnlyList<T> yl) return List(xl, yl, comparer);
        if (TryGetCount(x, out var xc) && TryGetCount(y, out var yc) && xc != yc) return false;

        comparer ??= EqualityComparer<T>.Default;
        using var ex = x.GetEnumerator();
        using var ey = y.GetEnumerator();
        while (true)
        {
            var hasX = ex.MoveNext();
            if (hasX != ey.MoveNext()) return false;
            if (!hasX) return true;
            if (!comparer.Equals(ex.Current, ey.Current)) return false;
        }
    }

    public static int SequenceHash<T>(IEnumerable<T>? x, IEqualityComparer<T>? comparer = null)
    {
        if (x is null) return 0;
        if (x is IReadOnlyList<T> list) return ListHash(list, comparer);
        comparer ??= EqualityComparer<T>.Default;
        var hash = Seed;
        foreach (var item in x)
            hash = Combine(hash, item is null ? 0 : comparer.GetHashCode(item));
        return hash;
    }

    private static bool TryGetCount<T>(IEnumerable<T> source, out int count)
    {
        switch (source)
        {
            case ICollection<T> c: count = c.Count; return true;
            case IReadOnlyCollection<T> rc: count = rc.Count; return true;
            default: count = 0; return false;
        }
    }
    #endregion

    #region Dictionaries
    /// <summary>
    /// Order-insensitive: equal when both hold the same keys (looked up with <paramref name="x"/>'s own key
    /// comparer) with equal values.
    /// </summary>
    public static bool Dictionary<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? x, IReadOnlyDictionary<TKey, TValue>? y, IEqualityComparer<TValue>? valueComparer = null)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Count != y.Count) return false;
        valueComparer ??= EqualityComparer<TValue>.Default;
        foreach (var pair in x)
        {
            if (!y.TryGetValue(pair.Key, out var other)) return false;
            if (!valueComparer.Equals(pair.Value, other)) return false;
        }
        return true;
    }

    /// <summary>Order-insensitive: the sum of the entry hashes.</summary>
    public static int DictionaryHash<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? x, IEqualityComparer<TValue>? valueComparer = null)
    {
        if (x is null) return 0;
        valueComparer ??= EqualityComparer<TValue>.Default;
        var hash = 0;
        foreach (var pair in x)
        {
            var entry = Combine(pair.Key is null ? 0 : pair.Key.GetHashCode(), pair.Value is null ? 0 : valueComparer.GetHashCode(pair.Value));
            hash = unchecked(hash + entry);
        }
        return hash;
    }

    /// <summary>Overload for <see cref="IDictionary{TKey, TValue}"/>, which does not implement the read-only interface.</summary>
    public static bool Dictionary<TKey, TValue>(IDictionary<TKey, TValue>? x, IDictionary<TKey, TValue>? y, IEqualityComparer<TValue>? valueComparer = null)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Count != y.Count) return false;
        valueComparer ??= EqualityComparer<TValue>.Default;
        foreach (var pair in x)
        {
            if (!y.TryGetValue(pair.Key, out var other)) return false;
            if (!valueComparer.Equals(pair.Value, other)) return false;
        }
        return true;
    }

    public static int DictionaryHash<TKey, TValue>(IDictionary<TKey, TValue>? x, IEqualityComparer<TValue>? valueComparer = null)
    {
        if (x is null) return 0;
        valueComparer ??= EqualityComparer<TValue>.Default;
        var hash = 0;
        foreach (var pair in x)
        {
            var entry = Combine(pair.Key is null ? 0 : pair.Key.GetHashCode(), pair.Value is null ? 0 : valueComparer.GetHashCode(pair.Value));
            hash = unchecked(hash + entry);
        }
        return hash;
    }
    #endregion

    #region Comparer instances, for nesting
    /// <summary>Compares <c>T[]</c> element-wise.</summary>
    public sealed class ArrayComparer<T>(IEqualityComparer<T>? element = null) : IEqualityComparer<T[]?>
    {
        public static readonly ArrayComparer<T> Instance = new();
        public bool Equals(T[]? x, T[]? y) => Array(x, y, element);
        public int GetHashCode(T[]? obj) => ArrayHash(obj, element);
    }

    /// <summary>Compares <see cref="ImmutableArray{T}"/> element-wise.</summary>
    public sealed class ImmutableArrayComparer<T>(IEqualityComparer<T>? element = null) : IEqualityComparer<ImmutableArray<T>>
    {
        public static readonly ImmutableArrayComparer<T> Instance = new();
        public bool Equals(ImmutableArray<T> x, ImmutableArray<T> y) => ImmutableArray(x, y, element);
        public int GetHashCode(ImmutableArray<T> obj) => ImmutableArrayHash(obj, element);
    }

    /// <summary>Compares <see cref="IReadOnlyList{T}"/> element-wise.</summary>
    public sealed class ListComparer<T>(IEqualityComparer<T>? element = null) : IEqualityComparer<IReadOnlyList<T>?>
    {
        public static readonly ListComparer<T> Instance = new();
        public bool Equals(IReadOnlyList<T>? x, IReadOnlyList<T>? y) => List(x, y, element);
        public int GetHashCode(IReadOnlyList<T>? obj) => ListHash(obj, element);
    }

    /// <summary>Compares <see cref="IEnumerable{T}"/> element-wise.</summary>
    public sealed class SequenceComparer<T>(IEqualityComparer<T>? element = null) : IEqualityComparer<IEnumerable<T>?>
    {
        public static readonly SequenceComparer<T> Instance = new();
        public bool Equals(IEnumerable<T>? x, IEnumerable<T>? y) => Sequence(x, y, element);
        public int GetHashCode(IEnumerable<T>? obj) => SequenceHash(obj, element);
    }

    /// <summary>Compares <see cref="IReadOnlyDictionary{TKey, TValue}"/> order-insensitively.</summary>
    public sealed class DictionaryComparer<TKey, TValue>(IEqualityComparer<TValue>? value = null) : IEqualityComparer<IReadOnlyDictionary<TKey, TValue>?>
    {
        public static readonly DictionaryComparer<TKey, TValue> Instance = new();
        public bool Equals(IReadOnlyDictionary<TKey, TValue>? x, IReadOnlyDictionary<TKey, TValue>? y) => Dictionary(x, y, value);
        public int GetHashCode(IReadOnlyDictionary<TKey, TValue>? obj) => DictionaryHash(obj, value);
    }

    /// <summary>
    /// A comparer built from an equality and a hash delegate. Generated code uses it for an element type that no
    /// comparer above covers but that still needs a structural comparison, such as a tuple holding a list.
    /// </summary>
    public sealed class DelegateComparer<T>(Func<T, T, bool> equals, Func<T, int> hash) : IEqualityComparer<T>
    {
        public bool Equals(T x, T y) => equals(x, y);
        public int GetHashCode(T obj) => hash(obj);
    }

    /// <summary>Ordinal string comparison, matching what generated code emits for <see cref="string"/> properties.</summary>
    public static IEqualityComparer<string?> String => StringComparer.Ordinal;
    #endregion
}
