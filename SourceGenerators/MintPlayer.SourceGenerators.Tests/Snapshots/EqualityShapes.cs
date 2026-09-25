namespace MintPlayer.SourceGenerators.Tests.Snapshots;

/// <summary>
/// A fixture for every shape [GenerateEquality] generates (PRD D1 to D4). The snapshots, the S3 compile matrix and
/// the runtime behaviour tests all use these, so all three look at the same code.
/// </summary>
/// <remarks>
/// Written for C# 9 (block namespaces, no collection expressions) so the compile matrix can run every shape at
/// LangVersion 9; only <see cref="RecordStruct"/> and <see cref="Nested"/> need C# 10. Each one compiles without a
/// single warning under nullable, which the matrix checks with warnings as errors.
/// </remarks>
internal static class EqualityShapes
{
    /// <summary>The shapes that need C# 10 (record struct, <c>record class</c>).</summary>
    public static readonly string[] NeedCSharp10 = [nameof(RecordStruct), nameof(Nested)];

    public static readonly string[] All =
    [
        nameof(SealedClass), nameof(NonSealedClass), nameof(AbstractTree), nameof(Record), nameof(SealedRecord),
        nameof(DerivedRecord), nameof(Struct), nameof(RecordStruct), nameof(Generic), nameof(Nested),
        nameof(UserDeclared), nameof(UseEqualityComparer), nameof(NestedCollections),
    ];

    public static string Get(string name)
        => (string)typeof(EqualityShapes).GetField(name)!.GetValue(null)!;

    public const string SealedClass = """
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public sealed partial class CliOption
            {
                public string Name { get; set; } = "";
                public IReadOnlyList<string> Aliases { get; set; } = new List<string>();
                public ImmutableArray<Child> Children { get; set; }
                [EqualityIgnore] public string CachedText { get; set; } = "";
            }

            [GenerateEquality]
            public sealed partial class Child
            {
                public string Value { get; set; } = "";
            }
        }
        """;

    public const string NonSealedClass = """
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public partial class Point
            {
                public int X { get; set; }
                public int Y { get; set; }
            }
        }
        """;

    /// <summary>A derived type WITHOUT the attribute, a transitive grandchild, and a derived type that has it too.</summary>
    public const string AbstractTree = """
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public abstract partial class Node
            {
                public string Name { get; set; } = "";
            }

            public partial class Leaf : Node
            {
                public int Weight { get; set; }
            }

            public sealed partial class ColoredLeaf : Leaf
            {
                public string Color { get; set; } = "";
            }

            [GenerateEquality]
            public partial class Branch : Node
            {
                public double Length { get; set; }
            }
        }
        """;

    public const string Record = """
        using System.Collections.Generic;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public partial record Person(string Name, int Age)
            {
                public List<string> Tags { get; init; } = new List<string>();
            }
        }
        """;

    public const string SealedRecord = """
        using System.Collections.Generic;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public sealed partial record Tagged(string Name)
            {
                public IReadOnlyList<string> Tags { get; init; } = new List<string>();
            }
        }
        """;

    public const string DerivedRecord = """
        using System.Collections.Generic;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public abstract partial record Animal(string Name);

            public partial record Dog(string Name, string Breed) : Animal(Name)
            {
                public List<string> Tricks { get; init; } = new List<string>();
            }

            public sealed partial record Puppy(string Name, string Breed, int Weeks) : Dog(Name, Breed);
        }
        """;

    public const string Struct = """
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public partial struct Range
            {
                public int Start { get; set; }
                public int End { get; set; }
                public string[]? Labels { get; set; }
            }

            [GenerateEquality]
            public readonly partial struct Span
            {
                public Span(int length) => Length = length;
                public int Length { get; }
            }
        }
        """;

    public const string RecordStruct = """
        using System.Collections.Generic;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public partial record struct Coordinate(double Lat, double Lon)
            {
                public List<string>? Names { get; set; }
            }
        }
        """;

    public const string Generic = """
        using System.Collections.Generic;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public partial class Box<T>
            {
                public T? Value { get; set; }
                public List<T> Items { get; set; } = new List<T>();
            }

            public partial class IntBox : Box<int>
            {
                public string Label { get; set; } = "";
            }
        }
        """;

    public const string Nested = """
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            public partial record class Outer
            {
                public partial struct Middle
                {
                    [GenerateEquality]
                    public sealed partial class Inner
                    {
                        public string Text { get; set; } = "";
                    }
                }

                [GenerateEquality]
                public partial record struct Pair(int A, int B);
            }
        }
        """;

    /// <summary>
    /// <c>Handmade</c> declares Equals(T) and both halves of the object pair, so none of them is generated.
    /// <c>HalfMade</c> declares only Equals(object): GetHashCode() is generated, with a MINT002 warning.
    /// </summary>
    public const string UserDeclared = """
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            [GenerateEquality]
            public sealed partial class Handmade
            {
                public string Name { get; set; } = "";

                public bool Equals(Handmade? other) => other is not null && other.Name == Name;
                public override bool Equals(object? obj) => Equals(obj as Handmade);
                public override int GetHashCode() => Name.GetHashCode();
            }

            [GenerateEquality]
            public sealed partial class HalfMade
            {
                public string Name { get; set; } = "";

                public override bool Equals(object? obj) => obj is HalfMade other && other.Name == Name;
            }
        }
        """;

    /// <summary>One comparer with a static Instance, one with only a parameterless constructor.</summary>
    public const string UseEqualityComparer = """
        using System;
        using System.Collections.Generic;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            public sealed class CaseInsensitive : IEqualityComparer<string>
            {
                public static readonly CaseInsensitive Instance = new();
                public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
                public int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
            }

            public sealed class ByLength : IEqualityComparer<string>
            {
                public bool Equals(string? x, string? y) => x?.Length == y?.Length;
                public int GetHashCode(string obj) => obj.Length;
            }

            [GenerateEquality]
            public sealed partial class Account
            {
                [UseEqualityComparer(typeof(CaseInsensitive))] public string Email { get; set; } = "";
                [UseEqualityComparer(typeof(ByLength))] public string? Code { get; set; }
            }
        }
        """;

    /// <summary>Nested models, nested collections and tuples: the element comparers compose through cached fields.</summary>
    public const string NestedCollections = """
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo
        {
            public enum Kind : long { None, Some }

            [GenerateEquality]
            public sealed partial class Item
            {
                public string Name { get; set; } = "";
                public Kind Kind { get; set; }
            }

            [GenerateEquality]
            public sealed partial class Holder
            {
                public Item? Single { get; set; }
                public List<Item> List { get; set; } = new List<Item>();
                public ImmutableArray<Item> Array { get; set; }
                public ImmutableArray<ImmutableArray<Item>> Grid { get; set; }
                public (Item First, Item Second) Pair { get; set; }
                public (string Key, List<int> Values) Tuple { get; set; }
                public List<(string Key, List<int> Values)> TupleList { get; set; } = new List<(string Key, List<int> Values)>();
                public IEnumerable<string[]> Rows { get; set; } = new List<string[]>();
                public Dictionary<string, List<int>> Map { get; set; } = new Dictionary<string, List<int>>();
                public int? Maybe { get; set; }
                public double Ratio { get; set; }
            }
        }
        """;
}
