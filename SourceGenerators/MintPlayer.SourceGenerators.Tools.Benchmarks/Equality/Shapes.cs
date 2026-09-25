using System.Collections.Immutable;
using L = MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;
using G = MintPlayer.SourceGenerators.Tools.Benchmarks.Generated;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Equality;

/// <summary>
/// Builds the B1 instances. Every call returns a fresh graph with freshly allocated strings, so an "equal pair"
/// is two distinct graphs whose strings are equal but never the same reference: nothing can short-circuit on
/// <see cref="object.ReferenceEquals"/> below the root. <c>differInLast</c> changes only the LAST property
/// the comparison visits (the last character of the last string, or the last int), so an early-out cannot
/// skip the work either.
/// </summary>
public static class Shapes
{
    /// <summary>A copy of <paramref name="s"/> that is not the same instance (literals are interned).</summary>
    private static string Fresh(string s) => new string(s.ToCharArray());

    private static string Str(string prefix, int i, bool differ = false)
    {
        // 30 characters: "<prefix>-<i>-" padded with 'x', and the last one flipped when differ is set.
        var core = $"{prefix}-{i}-".PadRight(29, 'x');
        return Fresh(core + (differ ? 'Y' : 'Z'));
    }

    // ---- Flat: 3 strings of 30 chars ----
    public static L.Flat LegacyFlat(bool differ) => new() { A = Str("alpha", 1), B = Str("bravo", 2), C = Str("charlie", 3, differ) };
    public static G.Flat GeneratedFlat(bool differ) => new() { A = Str("alpha", 1), B = Str("bravo", 2), C = Str("charlie", 3, differ) };

    // ---- IReadOnlyList<string> x20 (backed by List<string>, the common case) ----
    public const int StringCount = 20;
    private static List<string> Strings(bool differ)
    {
        var list = new List<string>(StringCount);
        for (var i = 0; i < StringCount; i++) list.Add(Str("item", i, differ && i == StringCount - 1));
        return list;
    }
    public static L.StringList LegacyStringList(bool differ) => new() { Name = Str("list", 0), Items = Strings(differ) };
    public static G.StringList GeneratedStringList(bool differ) => new() { Name = Str("list", 0), Items = Strings(differ) };

    // ---- ImmutableArray<Child> x50 ----
    public const int ChildCount = 50;
    public static L.ChildArray LegacyChildArray(bool differ)
    {
        var b = ImmutableArray.CreateBuilder<L.Child>(ChildCount);
        for (var i = 0; i < ChildCount; i++) b.Add(new L.Child { Name = Str("child", i), Value = differ && i == ChildCount - 1 ? -1 : i });
        return new() { Name = Str("array", 0), Children = b.MoveToImmutable() };
    }
    public static G.ChildArray GeneratedChildArray(bool differ)
    {
        var b = ImmutableArray.CreateBuilder<G.Child>(ChildCount);
        for (var i = 0; i < ChildCount; i++) b.Add(new G.Child { Name = Str("child", i), Value = differ && i == ChildCount - 1 ? -1 : i });
        return new() { Name = Str("array", 0), Children = b.MoveToImmutable() };
    }

    // ---- (Child, Child) tuple property ----
    public static L.ChildPair LegacyChildPair(bool differ) => new()
    {
        Name = Str("pair", 0),
        Pair = (new L.Child { Name = Str("left", 1), Value = 1 }, new L.Child { Name = Str("right", 2), Value = differ ? -2 : 2 }),
    };
    public static G.ChildPair GeneratedChildPair(bool differ) => new()
    {
        Name = Str("pair", 0),
        Pair = (new G.Child { Name = Str("left", 1), Value = 1 }, new G.Child { Name = Str("right", 2), Value = differ ? -2 : 2 }),
    };

    // ---- 3-level abstract tree: a complete binary tree of depth 3 (7 nodes, 4 leaves), compared as Node ----
    public static L.Node LegacyTree(bool differ)
    {
        var n = 0;
        L.Node Build(int depth, bool last)
        {
            var id = n++;
            if (depth == 0)
                return new L.Leaf { Name = Str("leaf", id), Kind = Str("lit", id), Value = Str("value", id, differ && last) };
            var left = Build(depth - 1, false);
            var right = Build(depth - 1, last);
            return new L.Binary { Name = Str("bin", id), Kind = Str("add", id), Left = left, Right = right };
        }
        return Build(2, true);
    }
    public static G.Node GeneratedTree(bool differ)
    {
        var n = 0;
        G.Node Build(int depth, bool last)
        {
            var id = n++;
            if (depth == 0)
                return new G.Leaf { Name = Str("leaf", id), Kind = Str("lit", id), Value = Str("value", id, differ && last) };
            var left = Build(depth - 1, false);
            var right = Build(depth - 1, last);
            return new G.Binary { Name = Str("bin", id), Kind = Str("add", id), Left = left, Right = right };
        }
        return Build(2, true);
    }

    // ---- Spark-like: an entity with a List<PropertyDefinition> x20 ----
    public const int SparkPropertyCount = 20;
    public static L.SparkEntity LegacySpark(bool differ)
    {
        var props = new List<L.SparkProperty>(SparkPropertyCount);
        for (var i = 0; i < SparkPropertyCount; i++)
            props.Add(new L.SparkProperty { Name = Str("prop", i), ClrType = Str("System.String", i), IsNullable = i % 2 == 0, Label = Str("label", i, differ && i == SparkPropertyCount - 1) });
        return new() { Name = Str("entity", 0), Namespace = Str("Demo.Entities", 0), Properties = props };
    }
    public static G.SparkEntity GeneratedSpark(bool differ)
    {
        var props = new List<G.SparkProperty>(SparkPropertyCount);
        for (var i = 0; i < SparkPropertyCount; i++)
            props.Add(new G.SparkProperty { Name = Str("prop", i), ClrType = Str("System.String", i), IsNullable = i % 2 == 0, Label = Str("label", i, differ && i == SparkPropertyCount - 1) });
        return new() { Name = Str("entity", 0), Namespace = Str("Demo.Entities", 0), Properties = props };
    }
}
