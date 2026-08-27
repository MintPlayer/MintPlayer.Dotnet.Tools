using MintPlayer.SourceGenerators.Tools.Polyfills;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

public partial class ValueComparer<T>
{
    // Delegate caches to avoid repeated MakeGenericMethod + Invoke overhead
    private static readonly ConcurrentDictionary<Type, Func<object, object, bool>> _equalsDelegateCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, int>> _hashDelegateCache = new();

    private static readonly ConcurrentDictionary<Type, MethodInfo> _immutableArrayEqualsCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> _immutableArrayHashCache = new();

    /// <summary>
    /// Closes <see cref="ImmutableArrayEquals{TArr}"/> over the array's ELEMENT type. The
    /// generic parameter is the element type, not the ImmutableArray type — getting that
    /// wrong is what made every ImmutableArray comparison throw InvalidCastException.
    /// </summary>
    private static bool ImmutableArrayEqualsDynamic(Type immutableArrayType, object a, object b)
    {
        var closed = _immutableArrayEqualsCache.GetOrAdd(immutableArrayType, static t =>
            typeof(ValueComparer<T>)
                .GetMethod(nameof(ImmutableArrayEquals), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(t.GetGenericArguments()[0]));

        return (bool)closed.Invoke(null, new[] { a, b })!;
    }

    private static void ImmutableArrayHashDynamic(Type immutableArrayType, ref HashCodeCompat h, object o)
    {
        var closed = _immutableArrayHashCache.GetOrAdd(immutableArrayType, static t =>
            typeof(ValueComparer<T>)
                .GetMethod(nameof(ImmutableArrayHash), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(t.GetGenericArguments()[0]));

        // h is a by-ref struct parameter, so Invoke writes the mutated value back into the
        // argument array and it has to be copied out again.
        var args = new object[] { h, o };
        closed.Invoke(null, args);
        h = (HashCodeCompat)args[0];
    }

    private static bool ImmutableArrayEquals<TArr>(object a, object b)
    {
        var x = (ImmutableArray<TArr>)a;
        var y = (ImmutableArray<TArr>)b;
        if (x.Length != y.Length) return false;
        var elem = GetElementComparer<TArr>();
        for (int i = 0; i < x.Length; i++)
            if (!elem.Equals(x[i], y[i])) return false;
        return true;
    }

    private static void ImmutableArrayHash<TArr>(ref HashCodeCompat h, object o)
    {
        var a = (ImmutableArray<TArr>)o;
        var elem = GetElementComparer<TArr>();
        foreach (var item in a)
            h.Add(item, elem);
    }

    private static IEqualityComparer<TArr> GetElementComparer<TArr>()
    {
        if (ComparerRegistry.TryGet<TArr>(out var reg))
            return reg;
        return EqualityComparer<TArr>.Default;
    }

    private static bool TryListEquals<TProp>(TProp x, TProp y, out bool equals)
    {
        // Handle IReadOnlyList<T> structurally
        equals = false;
        var xList = x as System.Collections.IEnumerable;
        var yList = y as System.Collections.IEnumerable;
        if (xList is null || yList is null) return false;

        var xe = xList.GetEnumerator();
        var ye = yList.GetEnumerator();

        try
        {
            bool xm, ym;
            while ((xm = xe.MoveNext()) & (ym = ye.MoveNext()))
            {
                var xv = xe.Current;
                var yv = ye.Current;
                if (!ObjectEqualsDynamic(xv, yv)) { equals = false; return true; }
            }
            equals = xm == ym; // both ended?
            return true;
        }
        finally
        {
            (xe as IDisposable)?.Dispose();
            (ye as IDisposable)?.Dispose();
        }
    }

    private static bool TryListHash(object value, ref HashCodeCompat h)
    {
        var e = (value as System.Collections.IEnumerable)?.GetEnumerator();
        if (e is null) return false;

        try
        {
            while (e.MoveNext())
            {
                var v = e.Current;
                if (v is null) { h.Add(0); continue; }
                h.Add(GetHashCodeDynamic(v));
            }
            return true;
        }
        finally
        {
            (e as IDisposable)?.Dispose();
        }
    }

    // Dynamic fallbacks for list elements with cached delegates
    private static bool ObjectEqualsDynamic(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        var t = a.GetType();
        if (t != b.GetType()) return false;

        var fn = _equalsDelegateCache.GetOrAdd(t, CreateEqualsDelegate);
        return fn(a, b);
    }

    private static Func<object, object, bool> CreateEqualsDelegate(Type type)
    {
        var method = typeof(ValueComparer<T>)
            .GetMethod(nameof(EqualsGeneric), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type);

        // Create a wrapper delegate that handles the boxing/unboxing
        return (a, b) => (bool)method.Invoke(null, new[] { a, b })!;
    }

    private static bool EqualsGeneric<TV>(TV a, TV b)
    {
        if (ComparerRegistry.TryGet<TV>(out var reg))
            return reg.Equals(a, b);
        return EqualityComparer<TV>.Default.Equals(a, b);
    }

    private static int GetHashCodeDynamic(object v)
    {
        var t = v.GetType();
        var fn = _hashDelegateCache.GetOrAdd(t, CreateHashDelegate);
        return fn(v);
    }

    private static Func<object, int> CreateHashDelegate(Type type)
    {
        var method = typeof(ValueComparer<T>)
            .GetMethod(nameof(GetHashCodeGeneric), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type);

        return v => (int)method.Invoke(null, new[] { v })!;
    }

    private static int GetHashCodeGeneric<TV>(TV v)
    {
        if (v is null) return 0;
        if (ComparerRegistry.TryGet<TV>(out var reg))
            return reg.GetHashCode(v);
        return EqualityComparer<TV>.Default.GetHashCode(v);
    }
}
