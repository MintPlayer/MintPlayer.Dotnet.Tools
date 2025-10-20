using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

/// <summary>
/// Abstract base Value Comparer class
/// </summary>
public abstract partial class ValueComparer<T> : IEqualityComparer<T?>
{
    public bool Equals(T? x, T? y)
    {
        if (x is null && y is null) return true;
        if (x is null ^ y is null) return false;
        if (ReferenceEquals(x, y)) return true;
        return AreEqual(x, y);
    }

    public int GetHashCode(T? obj)
    {
        var h = new HashCode();
        AddHash(ref h, obj);
        return h.ToHashCode();
    }

    protected abstract bool AreEqual(T x, T y);
    /// <summary>
    /// Override to contribute a stable hash from all fields you compare in AreEqual.
    /// If you don’t override, we’ll try a best-effort default.
    /// </summary>
    protected virtual void AddHash(ref HashCode h, T? obj)
    {
        // Default: if T has a default comparer, use it; otherwise do nothing.
        h.Add(obj);
    }

    /// <summary>
    /// Generic equality helper your generated comparers can call for each property.
    /// Plugs in a registered comparer when available, then falls back to sensible defaults,
    /// including structural comparison for ImmutableArray and IReadOnlyList.
    /// </summary>
    protected static bool IsEquals<TProp>(TProp x, TProp y)
    {
        // Fast path: same reference or both null
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        // 1) Registry (optional plugin comparers; your Tools can expose ComparerRegistry.TryGet<TProp>)
        if (ComparerRegistry.TryGet<TProp>(out var reg))
            return reg.Equals(x, y);

        // 2) ImmutableArray<T>
        if (typeof(TProp).IsGenericType &&
            typeof(TProp).GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        {
            return ImmutableArrayEquals<TProp>(x!, y!);
        }

        // 3) IReadOnlyList<T> structural compare
        if (x is System.Collections.IEnumerable && TryListEquals(x, y, out var eq))
            return eq;

        // 4) Fallback: default comparer (covers string, primitives, records, etc.)
        return EqualityComparer<TProp>.Default.Equals(x, y);
    }

    /// <summary>Add structural hash for a property; call alongside IsEquals for properties you compare.</summary>
    protected static void AddHash<TProp>(ref HashCode h, TProp value)
    {
        if (value is null) { h.Add(0); return; }

        // Registry first
        if (ComparerRegistry.TryGet<TProp>(out var reg))
        {
            h.Add(value, reg);
            return;
        }

        // ImmutableArray<T>
        if (typeof(TProp).IsGenericType &&
            typeof(TProp).GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        {
            ImmutableArrayHash<TProp>(ref h, value);
            return;
        }

        // IReadOnlyList<T>
        if (value is System.Collections.IEnumerable && TryListHash(value, ref h))
            return;

        h.Add(value);
    }
}
