using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

public partial class ValueComparer<T>
{
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

    private static void ImmutableArrayHash<TArr>(ref HashCode h, object o)
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

    private static bool TryListHash(object value, ref HashCode h)
    {
        var e = (value as System.Collections.IEnumerable)?.GetEnumerator();
        if (e is null) return false;

        try
        {
            while (e.MoveNext())
            {
                var v = e.Current;
                if (v is null) { h.Add(0); continue; }
                AddDynamic(ref h, v);
            }
            return true;
        }
        finally
        {
            (e as IDisposable)?.Dispose();
        }
    }

    // Dynamic fallbacks for list elements
    private static bool ObjectEqualsDynamic(object a, object b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        var t = a.GetType();
        if (t != b.GetType()) return false;

        var method = typeof(ValueComparer<T>).GetMethod(nameof(EqualsGeneric), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                                             .MakeGenericMethod(t);
        return (bool)method.Invoke(null, new[] { a, b })!;
    }

    private static bool EqualsGeneric<TV>(TV a, TV b)
        => EqualityComparer<TV>.Default.Equals(a, b);

    private static void AddDynamic(ref global::System.HashCode h, object v)
    {
        var t = v.GetType();
        var method = typeof(ValueComparer<T>).GetMethod(nameof(AddHashGeneric), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                                             .MakeGenericMethod(t);
        method.Invoke(null, new[] { h, v });
    }

    private static void AddHashGeneric<TV>(ref HashCode h, TV v)
        => h.Add(v, EqualityComparer<TV>.Default);
}
