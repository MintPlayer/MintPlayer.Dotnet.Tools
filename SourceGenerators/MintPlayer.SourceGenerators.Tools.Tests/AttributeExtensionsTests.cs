using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// PRD-TypeKindRobustness D0.1 and D0.2: reading attribute arguments as constants, and finding attributes by
/// metadata name.
/// </summary>
public class AttributeExtensionsTests
{
    private const string Attributes = """
        using System;
        namespace N
        {
            public enum Kind { A, B = 5 }

            [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
            public class MyAttribute : Attribute
            {
                public MyAttribute(int count, Kind kind = Kind.B, Type? type = null, params string[] names) { }
                public string? Text { get; set; }
                public Kind Mode { get; set; }
                public long Big { get; set; }
                public double Ratio { get; set; }
                public int Count { get; set; }
            }

            public class Outer { public class InnerAttribute : Attribute { } }
            public class GenAttribute<T> : Attribute { }
            public static class Consts { public const int Five = 5; }
        }
        namespace Other
        {
            public class MyAttribute : Attribute { }
        }
        public class GlobalAttribute : System.Attribute { }
        """;

    private static AttributeData Attribute(string usage, string metadataName = "N.MyAttribute")
        => Target(usage).FindAttribute(metadataName)!;

    private static INamedTypeSymbol Target(string usage)
    {
        var compilation = PathSpecCompiler.Compile("latest", Attributes, "namespace N { " + usage + " class Target { } }");
        return compilation.GetTypeByMetadataName("N.Target")!;
    }

    [Theory]
    [InlineData("[My(3)]", 3)]
    [InlineData("[My(Consts.Five)]", 5)]
    [InlineData("[My(Consts.Five * 2 + 1)]", 11)]
    [InlineData("[My((int)Kind.B)]", 5)]
    [InlineData("[My((int)4.9)]", 4)]
    [InlineData("[My(count: 7)]", 7)]
    [InlineData("[My(names: new[] { \"a\" }, count: 8)]", 8)]
    public void ConstructorArgumentsAreReadAsConstants(string usage, int expected)
    {
        Attribute(usage).GetArgument("count", -1).Should().Be(expected);
        // The parameter name matches ignoring case.
        Attribute(usage).GetArgument("COUNT", -1).Should().Be(expected);
    }

    [Fact]
    public void NamedArgumentWinsOverAConstructorParameterOfTheSameName()
        => Attribute("[My(1, Count = 2)]").GetArgument("count", -1).Should().Be(2);

    [Fact]
    public void Enums()
    {
        var attribute = Attribute("[My(1, Kind.A, Mode = Kind.B)]");
        attribute.GetArgument("kind", Kind.B).Should().Be(Kind.A);
        attribute.GetArgument("Mode", Kind.A).Should().Be(Kind.B);
        attribute.GetArgument<Kind?>("Mode", null).Should().Be(Kind.B);
        // An enum also reads as its integer value.
        attribute.GetArgument("Mode", -1).Should().Be(5);
    }

    [Fact]
    public void OptionalParameterGivesItsDefault()
        => Attribute("[My(1)]").GetArgument("kind", Kind.A).Should().Be(Kind.B);

    [Fact]
    public void TypeOfReadsAsATypeSymbol()
    {
        var type = Attribute("[My(1, type: typeof(System.Collections.Generic.List<int>))]").GetArgument<INamedTypeSymbol?>("type", null);
        type.Should().NotBeNull();
        type!.ToDisplayString().Should().Be("System.Collections.Generic.List<int>");
        Attribute("[My(1, type: typeof(string))]").GetArgument<ITypeSymbol?>("type", null)!.SpecialType.Should().Be(SpecialType.System_String);
    }

    [Fact]
    public void ATypeOfThatDoesNotBindGivesTheFallback()
        => Attribute("[My(1, type: typeof(DoesNotExist))]").TryGetArgument<INamedTypeSymbol>("type", out _).Should().BeFalse();

    [Fact]
    public void Arrays()
    {
        var attribute = Attribute("[My(1, Kind.A, null, \"x\", \"y\")]");
        attribute.GetArgument<string[]>("names", []).Should().Equal(["x", "y"]);
        var constants = attribute.GetArgument("names", ImmutableArray<TypedConstant>.Empty);
        constants.Select(c => (string)c.Value!).Should().Equal(["x", "y"]);
        // An element that doesn't convert fails the whole array.
        attribute.TryGetArgument<int[]>("names", out _).Should().BeFalse();
    }

    [Fact]
    public void NullAndMissing()
    {
        var attribute = Attribute("[My(1, Text = null)]");
        attribute.GetArgument<string?>("Text", "fallback").Should().BeNull();
        attribute.TryGetArgument<string?>("Text", out _).Should().BeTrue();
        attribute.GetArgument("Ratio", 1.5).Should().Be(1.5);
        attribute.GetArgument("NoSuchArgument", "fallback").Should().Be("fallback");
        attribute.GetArgument("", "fallback").Should().Be("fallback");
        ((AttributeData?)null).GetArgument("count", 9).Should().Be(9);
        // An explicit null cannot become a non-nullable value type.
        Attribute("[My(1, Text = null)]").TryGetArgument<int>("Text", out _).Should().BeFalse();
    }

    [Fact]
    public void NumbersConvertOnlyWhenTheyFit()
    {
        Attribute("[My(1, Big = 42)]").GetArgument("Big", 0).Should().Be(42);
        Attribute("[My(1, Big = long.MaxValue)]").GetArgument("Big", -1).Should().Be(-1);
        Attribute("[My(1)]").GetArgument("count", 0L).Should().Be(1L);
        Attribute("[My(1, Ratio = 2.5)]").GetArgument("Ratio", 0m).Should().Be(2.5m);
        // A fractional value never silently becomes an integer.
        Attribute("[My(1, Ratio = 2.5)]").GetArgument("Ratio", -1).Should().Be(-1);
        // Nor does a number become a string.
        Attribute("[My(1)]").GetArgument("count", "fallback").Should().Be("fallback");
    }

    [Fact]
    public void AnAttributeThatDoesNotBindGivesTheFallback()
    {
        var compilation = PathSpecCompiler.Compile("latest", "[Missing(1)] class T { }");
        var attribute = compilation.GetTypeByMetadataName("T")!.GetAttributes().Single();
        attribute.GetArgument("count", -1).Should().Be(-1);
    }

    [Fact]
    public void FindAttributeComparesTheNamespace()
    {
        var target = Target("[Other.My] [My(1)] [My(2)]");
        target.FindAttribute("N.MyAttribute")!.GetArgument("count", -1).Should().Be(1);
        target.FindAttributes("N.MyAttribute").Select(a => a.GetArgument("count", -1)).Should().Equal([1, 2]);
        target.FindAttributes("Other.MyAttribute").Count().Should().Be(1);
        target.FindAttribute("MyAttribute").Should().BeNull();
        target.FindAttribute("X.N.MyAttribute").Should().BeNull();
        target.FindAttribute("N.MyAttributeX").Should().BeNull();
        target.HasAttribute("N.MyAttribute").Should().BeTrue();
        Target("[Other.My]").FindAttribute("N.MyAttribute").Should().BeNull();
        ((ISymbol?)null).FindAttribute("N.MyAttribute").Should().BeNull();
        ((ISymbol?)null).FindAttributes("N.MyAttribute").Should().BeEmpty();
    }

    [Fact]
    public void IsAttributeHandlesNestedGenericAndGlobalTypes()
    {
        Target("[Outer.Inner]").FindAttribute("N.Outer+InnerAttribute").Should().NotBeNull();
        Target("[Outer.Inner]").FindAttribute("N.Outer.InnerAttribute").Should().BeNull();
        Target("[Gen<int>]").FindAttribute("N.GenAttribute`1").Should().NotBeNull();
        Target("[Gen<int>]").FindAttribute("N.GenAttribute").Should().BeNull();
        Target("[global::Global]").FindAttribute("GlobalAttribute").Should().NotBeNull();
        Target("[global::Global]").FindAttribute(".GlobalAttribute").Should().BeNull();
        ((INamedTypeSymbol?)null).IsAttribute("N.MyAttribute").Should().BeFalse();
    }

    // Mirrors N.Kind in the compiled source, so the test can read it as a CLR enum.
    public enum Kind { A, B = 5 }
}
