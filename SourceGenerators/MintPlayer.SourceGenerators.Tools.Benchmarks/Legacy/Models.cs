using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;

// The B1 models as master's ValueComparerGenerator saw them, each followed by a hand-written copy of what
// master's TreeValueComparerProducer emitted for it (see master:SourceGenerators/ValueComparerGenerator/
// MintPlayer.ValueComparerGenerator/Generators/ValueComparerGenerator.Producer.cs):
//
//  - the model partial carries [ValueComparer(typeof(XValueComparer))];
//  - AreEqual calls IsEquals(x.P, y.P) for every property, then `return true`;
//  - a base type with derived types gets `(x, y) switch { (D a, D b) => IsEquals(a, b), ..., _ => false }`
//    over its DIRECT derived types, and an abstract base compares no properties of its own;
//  - AddHash starts with the null sentinel and calls ValueComparer<T>.AddHash(ref h, obj.P) per property.
//
// A pipeline reached these through ComparerRegistry.For<T>(), which returns the same Instance.

[ValueComparer(typeof(FlatValueComparer))]
public sealed class Flat
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public string C { get; set; } = "";
}

public sealed class FlatValueComparer : ValueComparer<Flat>
{
    public static readonly FlatValueComparer Instance = new FlatValueComparer();
    protected override bool AreEqual(Flat x, Flat y)
    {
        if (!IsEquals(x.A, y.A)) return false;
        if (!IsEquals(x.B, y.B)) return false;
        if (!IsEquals(x.C, y.C)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, Flat? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<Flat>.AddHash(ref h, obj.A);
        ValueComparer<Flat>.AddHash(ref h, obj.B);
        ValueComparer<Flat>.AddHash(ref h, obj.C);
    }
}

[ValueComparer(typeof(StringListValueComparer))]
public sealed class StringList
{
    public string Name { get; set; } = "";
    public IReadOnlyList<string> Items { get; set; } = [];
}

public sealed class StringListValueComparer : ValueComparer<StringList>
{
    public static readonly StringListValueComparer Instance = new StringListValueComparer();
    protected override bool AreEqual(StringList x, StringList y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Items, y.Items)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, StringList? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<StringList>.AddHash(ref h, obj.Name);
        ValueComparer<StringList>.AddHash(ref h, obj.Items);
    }
}

[ValueComparer(typeof(ChildValueComparer))]
public sealed class Child
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

public sealed class ChildValueComparer : ValueComparer<Child>
{
    public static readonly ChildValueComparer Instance = new ChildValueComparer();
    protected override bool AreEqual(Child x, Child y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Value, y.Value)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, Child? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<Child>.AddHash(ref h, obj.Name);
        ValueComparer<Child>.AddHash(ref h, obj.Value);
    }
}

[ValueComparer(typeof(ChildArrayValueComparer))]
public sealed class ChildArray
{
    public string Name { get; set; } = "";
    public ImmutableArray<Child> Children { get; set; }
}

public sealed class ChildArrayValueComparer : ValueComparer<ChildArray>
{
    public static readonly ChildArrayValueComparer Instance = new ChildArrayValueComparer();
    protected override bool AreEqual(ChildArray x, ChildArray y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Children, y.Children)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, ChildArray? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<ChildArray>.AddHash(ref h, obj.Name);
        ValueComparer<ChildArray>.AddHash(ref h, obj.Children);
    }
}

[ValueComparer(typeof(ChildPairValueComparer))]
public sealed class ChildPair
{
    public string Name { get; set; } = "";
    public (Child, Child) Pair { get; set; }
}

public sealed class ChildPairValueComparer : ValueComparer<ChildPair>
{
    public static readonly ChildPairValueComparer Instance = new ChildPairValueComparer();
    protected override bool AreEqual(ChildPair x, ChildPair y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Pair, y.Pair)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, ChildPair? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<ChildPair>.AddHash(ref h, obj.Name);
        ValueComparer<ChildPair>.AddHash(ref h, obj.Pair);
    }
}

// ---- 3-level abstract tree: Node (abstract) <- Expr (abstract) <- Leaf, Binary (sealed) ----

[ValueComparer(typeof(NodeValueComparer))]
public abstract class Node
{
    public string Name { get; set; } = "";
}

public sealed class NodeValueComparer : ValueComparer<Node>
{
    public static readonly NodeValueComparer Instance = new NodeValueComparer();
    protected override bool AreEqual(Node x, Node y)
    {
        return (x, y) switch
        {
            (Expr a, Expr b) => IsEquals(a, b),
            _ => false,
        };
    }
    protected override void AddHash(ref HashCodeCompat h, Node? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<Node>.AddHash(ref h, obj.Name);
    }
}

[ValueComparer(typeof(ExprValueComparer))]
public abstract class Expr : Node
{
    public string Kind { get; set; } = "";
}

public sealed class ExprValueComparer : ValueComparer<Expr>
{
    public static readonly ExprValueComparer Instance = new ExprValueComparer();
    protected override bool AreEqual(Expr x, Expr y)
    {
        return (x, y) switch
        {
            (Leaf a, Leaf b) => IsEquals(a, b),
            (Binary a, Binary b) => IsEquals(a, b),
            _ => false,
        };
    }
    protected override void AddHash(ref HashCodeCompat h, Expr? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<Expr>.AddHash(ref h, obj.Name);
        ValueComparer<Expr>.AddHash(ref h, obj.Kind);
    }
}

[ValueComparer(typeof(LeafValueComparer))]
public sealed class Leaf : Expr
{
    public string Value { get; set; } = "";
}

public sealed class LeafValueComparer : ValueComparer<Leaf>
{
    public static readonly LeafValueComparer Instance = new LeafValueComparer();
    protected override bool AreEqual(Leaf x, Leaf y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Kind, y.Kind)) return false;
        if (!IsEquals(x.Value, y.Value)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, Leaf? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<Leaf>.AddHash(ref h, obj.Name);
        ValueComparer<Leaf>.AddHash(ref h, obj.Kind);
        ValueComparer<Leaf>.AddHash(ref h, obj.Value);
    }
}

[ValueComparer(typeof(BinaryValueComparer))]
public sealed class Binary : Expr
{
    public Node Left { get; set; } = null!;
    public Node Right { get; set; } = null!;
}

public sealed class BinaryValueComparer : ValueComparer<Binary>
{
    public static readonly BinaryValueComparer Instance = new BinaryValueComparer();
    protected override bool AreEqual(Binary x, Binary y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Kind, y.Kind)) return false;
        if (!IsEquals(x.Left, y.Left)) return false;
        if (!IsEquals(x.Right, y.Right)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, Binary? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<Binary>.AddHash(ref h, obj.Name);
        ValueComparer<Binary>.AddHash(ref h, obj.Kind);
        ValueComparer<Binary>.AddHash(ref h, obj.Left);
        ValueComparer<Binary>.AddHash(ref h, obj.Right);
    }
}

// ---- Spark-like: an entity definition holding a List<PropertyDefinition> ----

[ValueComparer(typeof(SparkPropertyValueComparer))]
public sealed class SparkProperty
{
    public string Name { get; set; } = "";
    public string ClrType { get; set; } = "";
    public bool IsNullable { get; set; }
    public string Label { get; set; } = "";
}

public sealed class SparkPropertyValueComparer : ValueComparer<SparkProperty>
{
    public static readonly SparkPropertyValueComparer Instance = new SparkPropertyValueComparer();
    protected override bool AreEqual(SparkProperty x, SparkProperty y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.ClrType, y.ClrType)) return false;
        if (!IsEquals(x.IsNullable, y.IsNullable)) return false;
        if (!IsEquals(x.Label, y.Label)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, SparkProperty? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<SparkProperty>.AddHash(ref h, obj.Name);
        ValueComparer<SparkProperty>.AddHash(ref h, obj.ClrType);
        ValueComparer<SparkProperty>.AddHash(ref h, obj.IsNullable);
        ValueComparer<SparkProperty>.AddHash(ref h, obj.Label);
    }
}

[ValueComparer(typeof(SparkEntityValueComparer))]
public sealed class SparkEntity
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public List<SparkProperty> Properties { get; set; } = [];
}

public sealed class SparkEntityValueComparer : ValueComparer<SparkEntity>
{
    public static readonly SparkEntityValueComparer Instance = new SparkEntityValueComparer();
    protected override bool AreEqual(SparkEntity x, SparkEntity y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Namespace, y.Namespace)) return false;
        if (!IsEquals(x.Properties, y.Properties)) return false;
        return true;
    }
    protected override void AddHash(ref HashCodeCompat h, SparkEntity? obj)
    {
        if (obj is null) { h.Add(0); return; }
        ValueComparer<SparkEntity>.AddHash(ref h, obj.Name);
        ValueComparer<SparkEntity>.AddHash(ref h, obj.Namespace);
        ValueComparer<SparkEntity>.AddHash(ref h, obj.Properties);
    }
}
