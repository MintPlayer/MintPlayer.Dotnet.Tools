using System.Reflection;
using MintPlayer.SourceGenerators.Tests._Infrastructure;
using MintPlayer.SourceGenerators.Tests.Snapshots;

namespace MintPlayer.SourceGenerators.Tests.Runtime;

/// <summary>
/// Layer 2 for [GenerateEquality]: compiles the generated equality, loads it, and asserts what Equals and
/// GetHashCode actually DO. Until these existed no test ever executed a generated comparer, which is how the P2
/// stack overflow and the [EqualityIgnore] hash bug of #184 went unnoticed.
/// </summary>
/// <remarks>
/// Each fixture comes with a small <c>Make</c> class compiled next to it, so instances are built in C# rather than
/// through reflection over init-only setters and ImmutableArray constructors.
/// </remarks>
public class GeneratedEqualityBehaviourTests
{
    private sealed class Fixture(Assembly assembly)
    {
        private readonly Type make = assembly.GetGeneratedType("Demo.Make");

        public object New(string method, params object?[] args)
            => make.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args)!;
    }

    private static Fixture Compile(string make, params string[] shapes)
    {
        var run = GeneratorHarness.Run("ValueComparerGenerator", [.. shapes, make], "Demo",
            generatorAssemblyName: "MintPlayer.ValueComparerGenerator");
        run.Errors.Should().BeEmpty(run.ErrorText);
        return new Fixture(run.Emit());
    }

    /// <summary>Equal both ways, through the object overload and the default comparer, and hashing equally.</summary>
    private static void ShouldBeValueEqual(object a, object b)
    {
        a.Should().NotBeSameAs(b);
        a.Equals(b).Should().BeTrue();
        b.Equals(a).Should().BeTrue();
        EqualityComparer<object>.Default.Equals(a, b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    private static void ShouldDiffer(object a, object b)
    {
        a.Equals(b).Should().BeFalse();
        b.Equals(a).Should().BeFalse();
    }

    private const string SealedClassMake = """
        using System.Collections.Generic;
        using System.Collections.Immutable;

        namespace Demo
        {
            public static class Make
            {
                public static CliOption Option(string name, string cached, bool aliasesAsList, string child)
                    => new CliOption
                    {
                        Name = name,
                        CachedText = cached,
                        Aliases = aliasesAsList ? new List<string> { "a", "b" } : new[] { "a", "b" },
                        Children = ImmutableArray.Create(new Child { Value = child }),
                    };

                public static CliOption WithChildren(bool isDefault)
                    => new CliOption { Children = isDefault ? default : ImmutableArray<Child>.Empty };
            }
        }
        """;

    [Fact]
    public void ASealedClass_ComparesEveryPropertyByValue_Symmetrically_AndHashesEqually()
    {
        var f = Compile(SealedClassMake, EqualityShapes.SealedClass);

        ShouldBeValueEqual(f.New("Option", "n", "c", false, "x"), f.New("Option", "n", "c", false, "x"));
        ShouldDiffer(f.New("Option", "n", "c", false, "x"), f.New("Option", "other", "c", false, "x"));
        // A nested model inside an ImmutableArray compares by value too.
        ShouldDiffer(f.New("Option", "n", "c", false, "x"), f.New("Option", "n", "c", false, "y"));
    }

    [Fact]
    public void AnIReadOnlyList_ComparesByItsDeclaredType_NotItsRuntimeType()
    {
        var f = Compile(SealedClassMake, EqualityShapes.SealedClass);

        // string[] on one side, List<string> on the other: equal, exactly as before (PRD D6).
        ShouldBeValueEqual(f.New("Option", "n", "c", false, "x"), f.New("Option", "n", "c", true, "x"));
    }

    [Fact]
    public void AnEqualityIgnoreProperty_IsLeftOutOfEqualsAndOutOfTheHash()
    {
        var f = Compile(SealedClassMake, EqualityShapes.SealedClass);

        // The #184 bug: equal instances that differ only in an ignored property hashed differently.
        ShouldBeValueEqual(f.New("Option", "n", "one", false, "x"), f.New("Option", "n", "two", false, "x"));
    }

    [Fact]
    public void NullAndReferenceShortcuts_Hold()
    {
        var f = Compile(SealedClassMake, EqualityShapes.SealedClass);
        var a = f.New("Option", "n", "c", false, "x");

        a.Equals(null).Should().BeFalse();
        a.Equals(a).Should().BeTrue();
        a.Equals("not an option").Should().BeFalse();
    }

    [Fact]
    public void ADefaultImmutableArray_EqualsOnlyAnotherDefault_NeverAnEmptyOne()
    {
        var f = Compile(SealedClassMake, EqualityShapes.SealedClass);

        ShouldBeValueEqual(f.New("WithChildren", true), f.New("WithChildren", true));
        ShouldBeValueEqual(f.New("WithChildren", false), f.New("WithChildren", false));
        ShouldDiffer(f.New("WithChildren", true), f.New("WithChildren", false));
    }

    private const string TreeMake = """
        namespace Demo
        {
            public static class Make
            {
                public static Leaf Leaf(string name, int weight) => new Leaf { Name = name, Weight = weight };
                public static ColoredLeaf Colored(string name, int weight, string color) => new ColoredLeaf { Name = name, Weight = weight, Color = color };
                public static Branch Branch(string name, double length) => new Branch { Name = name, Length = length };
            }
        }
        """;

    [Fact]
    public void ADerivedTypeWithoutTheAttribute_ComparesItsOwnProperties_AndDoesNotOverflowTheStack()
    {
        var f = Compile(TreeMake, EqualityShapes.AbstractTree);

        // P2: this exact shape recursed forever through the old ShapeValueComparer switch.
        ShouldBeValueEqual(f.New("Leaf", "n", 1), f.New("Leaf", "n", 1));
        ShouldDiffer(f.New("Leaf", "n", 1), f.New("Leaf", "n", 2));
        ShouldDiffer(f.New("Leaf", "n", 1), f.New("Leaf", "m", 1));
    }

    [Fact]
    public void ATransitiveGrandchild_ComparesEveryLevel()
    {
        var f = Compile(TreeMake, EqualityShapes.AbstractTree);

        ShouldBeValueEqual(f.New("Colored", "n", 1, "red"), f.New("Colored", "n", 1, "red"));
        ShouldDiffer(f.New("Colored", "n", 1, "red"), f.New("Colored", "n", 1, "blue"));
        ShouldDiffer(f.New("Colored", "n", 1, "red"), f.New("Colored", "n", 2, "red"));
    }

    [Fact]
    public void InstancesOfDifferentTypesInOneTree_AreNeverEqual()
    {
        var f = Compile(TreeMake, EqualityShapes.AbstractTree);

        // Same base properties, different runtime type: the exact-type check makes this false both ways.
        ShouldDiffer(f.New("Leaf", "n", 1), f.New("Colored", "n", 1, "red"));
        ShouldDiffer(f.New("Leaf", "n", 0), f.New("Branch", "n", 0d));
        ShouldBeValueEqual(f.New("Branch", "n", 2.5), f.New("Branch", "n", 2.5));
    }

    private const string NestedCollectionsMake = """
        using System.Collections.Generic;
        using System.Collections.Immutable;

        namespace Demo
        {
            public static class Make
            {
                public static Holder Holder(string deep, int tupleValue, bool reverseMap, double ratio)
                {
                    var map = new Dictionary<string, List<int>>();
                    if (reverseMap) { map["b"] = new List<int> { 2 }; map["a"] = new List<int> { 1 }; }
                    else { map["a"] = new List<int> { 1 }; map["b"] = new List<int> { 2 }; }

                    return new Holder
                    {
                        Single = new Item { Name = "single", Kind = Kind.Some },
                        List = new List<Item> { new Item { Name = "l" } },
                        Array = ImmutableArray.Create(new Item { Name = "a" }),
                        Grid = ImmutableArray.Create(ImmutableArray.Create(new Item { Name = deep })),
                        Pair = (new Item { Name = "p1" }, new Item { Name = "p2" }),
                        Tuple = ("t", new List<int> { tupleValue }),
                        TupleList = new List<(string Key, List<int> Values)> { ("k", new List<int> { tupleValue }) },
                        Rows = new List<string[]> { new[] { "r" } },
                        Map = map,
                        Maybe = 3,
                        Ratio = ratio,
                    };
                }
            }
        }
        """;

    [Fact]
    public void NestedModelsCollectionsAndTuples_CompareByValueAtEveryDepth()
    {
        var f = Compile(NestedCollectionsMake, EqualityShapes.NestedCollections);

        ShouldBeValueEqual(f.New("Holder", "deep", 1, false, 0.5), f.New("Holder", "deep", 1, false, 0.5));
        // ImmutableArray<ImmutableArray<Item>>, two levels down.
        ShouldDiffer(f.New("Holder", "deep", 1, false, 0.5), f.New("Holder", "deeper", 1, false, 0.5));
        // A List<int> inside a tuple, and inside a tuple inside a List.
        ShouldDiffer(f.New("Holder", "deep", 1, false, 0.5), f.New("Holder", "deep", 2, false, 0.5));
    }

    [Fact]
    public void Dictionaries_CompareOrderInsensitively()
    {
        var f = Compile(NestedCollectionsMake, EqualityShapes.NestedCollections);

        // PRD D6: the one intended behaviour change. No model in any repo has a dictionary property.
        ShouldBeValueEqual(f.New("Holder", "deep", 1, false, 0.5), f.New("Holder", "deep", 1, true, 0.5));
    }

    [Fact]
    public void ANaNDouble_IsEqualToItself()
    {
        var f = Compile(NestedCollectionsMake, EqualityShapes.NestedCollections);

        ShouldBeValueEqual(f.New("Holder", "deep", 1, false, double.NaN), f.New("Holder", "deep", 1, false, double.NaN));
    }

    [Fact]
    public void EqualModels_CollapseInAHashSet()
    {
        var f = Compile(NestedCollectionsMake, EqualityShapes.NestedCollections);

        var set = new HashSet<object>
        {
            f.New("Holder", "deep", 1, false, 0.5),
            f.New("Holder", "deep", 1, true, 0.5),
            f.New("Holder", "other", 1, false, 0.5),
        };
        set.Count.Should().Be(2);
    }

    private const string RecordsMake = """
        using System.Collections.Generic;

        namespace Demo
        {
            public static class Make
            {
                public static Person Person(string name, string tag) => new Person(name, 3) { Tags = new List<string> { tag } };
                public static Dog Dog(string trick) => new Dog("rex", "lab") { Tricks = new List<string> { trick } };
                public static Puppy Puppy(string trick) => new Puppy("rex", "lab", 8) { Tricks = new List<string> { trick } };
            }
        }
        """;

    [Fact]
    public void Records_CompareTheirCollectionsByValue_WhichTheCompilersEqualsWouldNot()
    {
        var f = Compile(RecordsMake, EqualityShapes.Record, EqualityShapes.DerivedRecord);

        ShouldBeValueEqual(f.New("Person", "n", "t"), f.New("Person", "n", "t"));
        ShouldDiffer(f.New("Person", "n", "t"), f.New("Person", "n", "u"));

        ShouldBeValueEqual(f.New("Dog", "sit"), f.New("Dog", "sit"));
        ShouldDiffer(f.New("Dog", "sit"), f.New("Dog", "roll"));
        ShouldBeValueEqual(f.New("Puppy", "sit"), f.New("Puppy", "sit"));
        // A derived record is never equal to its base, even with the same base values.
        ShouldDiffer(f.New("Dog", "sit"), f.New("Puppy", "sit"));
    }

    private const string StructsMake = """
        using System.Collections.Generic;

        namespace Demo
        {
            public static class Make
            {
                public static Range Range(string label) => new Range { Start = 1, End = 2, Labels = new[] { label } };
                public static Coordinate Coordinate(string name) => new Coordinate(1, 2) { Names = new List<string> { name } };
                public static Box<int> Box(int item) => new Box<int> { Value = 1, Items = new List<int> { item } };
                public static IntBox IntBox(string label) => new IntBox { Value = 1, Label = label };
            }
        }
        """;

    [Fact]
    public void StructsRecordStructsAndGenerics_CompareByValue()
    {
        var f = Compile(StructsMake, EqualityShapes.Struct, EqualityShapes.RecordStruct, EqualityShapes.Generic);

        ShouldBeValueEqual(f.New("Range", "a"), f.New("Range", "a"));
        ShouldDiffer(f.New("Range", "a"), f.New("Range", "b"));
        ShouldBeValueEqual(f.New("Coordinate", "a"), f.New("Coordinate", "a"));
        ShouldDiffer(f.New("Coordinate", "a"), f.New("Coordinate", "b"));
        ShouldBeValueEqual(f.New("Box", 1), f.New("Box", 1));
        ShouldDiffer(f.New("Box", 1), f.New("Box", 2));
        ShouldBeValueEqual(f.New("IntBox", "a"), f.New("IntBox", "a"));
        ShouldDiffer(f.New("IntBox", "a"), f.New("IntBox", "b"));
    }

    private const string ComparerMake = """
        namespace Demo
        {
            public static class Make
            {
                public static Account Account(string email, string? code) => new Account { Email = email, Code = code };
            }
        }
        """;

    [Fact]
    public void UseEqualityComparer_ReplacesTheChosenComparison_InEqualsAndInTheHash()
    {
        var f = Compile(ComparerMake, EqualityShapes.UseEqualityComparer);

        ShouldBeValueEqual(f.New("Account", "A@x.be", "123"), f.New("Account", "a@X.BE", "456"));
        ShouldDiffer(f.New("Account", "a@x.be", "123"), f.New("Account", "b@x.be", "123"));
        ShouldDiffer(f.New("Account", "a@x.be", "123"), f.New("Account", "a@x.be", "12"));
        ShouldBeValueEqual(f.New("Account", "a@x.be", null), f.New("Account", "a@x.be", null));
    }
}
