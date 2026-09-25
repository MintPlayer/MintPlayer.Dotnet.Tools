using MintPlayer.ValueComparerGenerator.Attributes;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Generated;

// The same B1 models as Legacy/Models.cs, decorated with [AutoValueComparer]. The ValueComparerGenerator of
// this branch runs over them at build time (it is referenced as an analyzer), so what gets measured is the
// real generated IEquatable<T> calling MintPlayer.SourceGenerators.Tools.ValueEquality. Set
// EmitCompilerGeneratedFiles to inspect it under obj/.

[AutoValueComparer]
public sealed partial class Flat
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public string C { get; set; } = "";
}

[AutoValueComparer]
public sealed partial class StringList
{
    public string Name { get; set; } = "";
    public IReadOnlyList<string> Items { get; set; } = [];
}

[AutoValueComparer]
public sealed partial class Child
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

[AutoValueComparer]
public sealed partial class ChildArray
{
    public string Name { get; set; } = "";
    public ImmutableArray<Child> Children { get; set; }
}

[AutoValueComparer]
public sealed partial class ChildPair
{
    public string Name { get; set; } = "";
    public (Child, Child) Pair { get; set; }
}

// ---- 3-level abstract tree. Only the root is decorated: the generator covers every derived type (D2). ----

[AutoValueComparer]
public abstract partial class Node
{
    public string Name { get; set; } = "";
}

public abstract partial class Expr : Node
{
    public string Kind { get; set; } = "";
}

public sealed partial class Leaf : Expr
{
    public string Value { get; set; } = "";
}

public sealed partial class Binary : Expr
{
    public Node Left { get; set; } = null!;
    public Node Right { get; set; } = null!;
}

// ---- Spark-like ----

[AutoValueComparer]
public sealed partial class SparkProperty
{
    public string Name { get; set; } = "";
    public string ClrType { get; set; } = "";
    public bool IsNullable { get; set; }
    public string Label { get; set; } = "";
}

[AutoValueComparer]
public sealed partial class SparkEntity
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public List<SparkProperty> Properties { get; set; } = [];
}
