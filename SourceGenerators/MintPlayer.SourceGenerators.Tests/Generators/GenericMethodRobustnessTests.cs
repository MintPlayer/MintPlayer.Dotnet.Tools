using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// PRD-TypeKindRobustness for <c>GenericMethodSourceGenerator</c>: C1 (#193), K1 and the #194 findings G0-G17.
/// </summary>
/// <remarks>
/// Every fixture uses the shipped <c>MintPlayer.SourceGenerators.Attributes.GenericMethodAttribute</c>. Behaviour is
/// asserted at runtime where it can be: a generated overload must not only compile but also reach the decorated
/// method and hand back its result.
/// </remarks>
public class GenericMethodRobustnessTests
{
    private const string Generator = "GenericMethodSourceGenerator";

    private static GeneratorRun Run(params string[] sources) => GeneratorHarness.Run(Generator, sources);

    private static GeneratorRun RunInDemo(string body) => Run($$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo
        {
        {{body}}
        }
        """);

    private static object? InvokeProbe(GeneratorRun run, string typeName, params Type[] typeArguments)
    {
        var type = run.Emit().GetGeneratedType(typeName);
        if (typeArguments.Length > 0) type = type.MakeGenericType(typeArguments);
        var probe = type.GetMethod("Probe") ?? throw new InvalidOperationException($"{typeName} has no Probe method");
        return probe.Invoke(null, null);
    }

    #region Five kinds (C1, K1)

    public static readonly string[] Kinds = ["class", "record class", "record", "struct", "record struct"];

    /// <summary>Top level, nested in a parent of each of the five kinds, and nested in an interface.</summary>
    public static readonly string[] Placements = ["top level", .. Kinds, "interface"];

    public static TheoryData<string, string> KindsAndPlacements()
    {
        var data = new TheoryData<string, string>();
        foreach (var kind in Kinds)
            foreach (var placement in Placements)
                data.Add(kind, placement);
        return data;
    }

    /// <summary>
    /// C1: any attributed method in a non-class type crashed the generator (InvalidCastException, CS8785). K1: the
    /// header was always <c>partial class</c> (CS0261). Every kind, in every placement, now compiles and works.
    /// </summary>
    [Theory]
    [MemberData(nameof(KindsAndPlacements))]
    public void ItWorksForEveryKindInEveryPlacement(string kind, string placement)
    {
        var target = $$"""
            public partial {{kind}} Target
            {
                [GenericMethod(3)]
                private int Add(object[] values) => values.Length;

                public static int Probe() => new Target().Add(1, "two", 3.0);
            }
            """;
        var body = placement == "top level"
            ? target
            : $$"""
                public partial {{(placement == "interface" ? "interface IOuter" : placement + " Outer")}}
                {
                {{target}}
                }
                """;

        var run = RunInDemo(body);

        run.Errors.Should().BeEmpty(run.ErrorText);
        var expectedKeyword = kind == "record class" ? "record" : kind;
        run.AllSources.Should().Contain($"partial {expectedKeyword} Target");
        var typeName = placement switch
        {
            "top level" => "Demo.Target",
            "interface" => "Demo.IOuter+Target",
            _ => "Demo.Outer+Target",
        };
        InvokeProbe(run, typeName).Should().Be(3);
    }

    [Fact]
    public void ItWorksForAStaticMethodOfAnInterface()
    {
        var run = RunInDemo("""
            public partial interface IBag
            {
                [GenericMethod(3)]
                private static int Sum(object[] values) => values.Length;
            }

            public static class Caller
            {
                public static int Probe() => IBag.Sum(1, 2, 3);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("partial interface IBag");
        InvokeProbe(run, "Demo.Caller").Should().Be(3);
    }

    /// <summary>
    /// #193: unrelated code that the old syntax predicate picked up (any attributed method, cast to a class) must not
    /// crash the generator, and a valid use next to it must still generate. The harness throws on CS8785/MPSG001.
    /// </summary>
    [Fact]
    public void UnrelatedAttributedMethodsInOtherKinds_DoNotStopAValidUse()
    {
        var run = RunInDemo("""
            public partial record R { [System.Text.RegularExpressions.GeneratedRegex("a+")] private static partial System.Text.RegularExpressions.Regex Rx(); }
            public partial record struct RS { [System.Text.RegularExpressions.GeneratedRegex("a+")] private static partial System.Text.RegularExpressions.Regex Rx(); }
            public partial struct S { [System.Text.RegularExpressions.GeneratedRegex("a+")] private static partial System.Text.RegularExpressions.Regex Rx(); }
            public partial interface I { [System.Text.RegularExpressions.GeneratedRegex("a+")] private static partial System.Text.RegularExpressions.Regex Rx(); }
            public record struct Unresolved { [DoesNotExist] private void M() { } }

            public partial class Valid
            {
                [GenericMethod(2)]
                private void Add(object[] values) { }
            }
            """);

        run.AllSources.Should().Contain("partial class Valid");
        run.Diagnostics.Where(d => d.Id.StartsWith("GENMETHOD")).Should().BeEmpty();
        // The regex generator is not part of this run, so its partial methods stay unimplemented (CS8795), and the
        // unresolved attribute is CS0246. Nothing else may fail.
        var errors = run.Errors.Where(d => d.Id is not ("CS8795" or "CS0246" or "CS0234")).Select(d => d.ToString()).ToList();
        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors));
    }

    #endregion

    #region #194 matrix (test plan item 8)

    public static TheoryData<string, string, bool, bool, string> Matrix()
    {
        var data = new TheoryData<string, string, bool, bool, string>();
        foreach (var kind in new[] { "class", "record", "struct", "record struct" })
            foreach (var accessibility in new[] { "public", "internal" })
                foreach (var generic in new[] { false, true })
                    foreach (var isStatic in new[] { false, true })
                        foreach (var returns in new[] { "void", "value", "Task" })
                            data.Add(kind, accessibility, generic, isStatic, returns);
        return data;
    }

    /// <summary>
    /// Kind x accessibility x generic x static x return shape. Each overload must compile (G3/K1 keyword, G4 no
    /// accessibility on the header, G5 the generic containing type) and forward its result (G6).
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void TheGeneratedOverloadForwardsItsResult(string kind, string accessibility, bool generic, bool isStatic, string returns)
    {
        var typeName = generic ? "Target<TItem>" : "Target";
        var staticKeyword = isStatic ? "static " : "";
        var (method, call) = returns switch
        {
            "void" => ($"private {staticKeyword}void Add(object[] values) {{ Last = values.Length; }}", "{0}; return Last;"),
            "value" => ($"private {staticKeyword}int Add(object[] values) => values.Length;", "return {0};"),
            _ => ($"private {staticKeyword}Task<int> Add(object[] values) => Task.FromResult(values.Length);", "return {0}.Result;"),
        };
        var invocation = (isStatic ? "Add" : $"new {typeName}().Add") + "(1, \"two\", 3.0)";

        var run = RunInDemo($$"""
            {{accessibility}} partial {{kind}} {{typeName}}
            {
                public static int Last;

                [GenericMethod(3)]
                {{method}}

                public static int Probe() { {{string.Format(call, invocation)}} }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        InvokeProbe(run, generic ? "Demo.Target`1" : "Demo.Target", generic ? [typeof(string)] : []).Should().Be(3);
    }

    /// <summary>G1: a bare <c>[GenericMethod]</c> generated nothing. The default is 4: overloads of arity 2, 3 and 4.</summary>
    [Fact]
    public void ABareAttribute_GeneratesTheDefaultArities()
    {
        var run = RunInDemo("""
            public partial class B
            {
                [GenericMethod]
                private void Add(object[] values) { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Add<T1, T2>(");
        run.AllSources.Should().Contain("Add<T1, T2, T3, T4>(");
        run.AllSources.Should().NotContain("T5");
    }

    /// <summary>G2: the count was parsed from syntax text, and Arguments[0] was read whatever its name.</summary>
    [Theory]
    [InlineData("const int MaxArity = 3;", "Count = MaxArity")]
    [InlineData("", "Count = 2 + 1")]
    [InlineData("", "Count = (int)3L")]
    [InlineData("", "Count = 3 /* trivia */")]
    [InlineData("", "3")]
    [InlineData("", "count: 3")]
    [InlineData("", "5, Count = 3")] // the setter runs after the constructor, so the named argument wins
    public void TheCountIsTheCompilersConstant(string member, string arguments)
    {
        var run = RunInDemo($$"""
            public partial class B
            {
                {{member}}

                [GenericMethod({{arguments}})]
                private void Add(object[] values) { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Add<T1, T2, T3>(");
        run.AllSources.Should().NotContain("T4");
    }

    /// <summary>G7: shapes a collection expression of the overload's arguments can reach.</summary>
    [Theory]
    [InlineData("object[] values")]
    [InlineData("object?[] values")]
    [InlineData("params object[] values")]
    [InlineData("List<object> values")]
    [InlineData("IEnumerable<object> values")]
    [InlineData("IReadOnlyList<object> values")]
    [InlineData("IReadOnlyCollection<object> values")]
    [InlineData("ICollection<object> values")]
    [InlineData("IList<object> values")]
    [InlineData("HashSet<object> values")]
    [InlineData("System.ReadOnlySpan<object> values")]
    [InlineData("System.Span<object> values")]
    [InlineData("params System.ReadOnlySpan<object> values")]
    [InlineData("System.Collections.Immutable.ImmutableArray<object> values")]
    public void AValidShape_Compiles(string parameter)
    {
        var run = RunInDemo($$"""
            #nullable enable
            public partial class B
            {
                [GenericMethod(2)]
                private void Add({{parameter}}) { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("GENMETHOD003").Should().BeEmpty();
        run.AllSources.Should().Contain("Add<T1, T2>(");
    }

    /// <summary>G7: each shape that produced CS0029, CS9174, CS0411 or worse in generated code is now GENMETHOD003.</summary>
    [Theory]
    [InlineData("private void Add(string[] values) { }")]
    [InlineData("private void Add(int[] values) { }")]
    [InlineData("private void Add(object value) { }")]
    [InlineData("private void Add() { }")]
    [InlineData("private void Add(object[] values, int extra) { }")]
    [InlineData("private void Add<T>(IEnumerable<T> values) { }")]
    [InlineData("private void Add(ref object[] values) { }")]
    [InlineData("private void Add(System.Collections.IEnumerable values) { }")]
    [InlineData("private void Add(object[,] values) { }")]
    [InlineData("private ref int Add(object[] values) => ref field;")]
    public void AnInvalidShape_IsGENMETHOD003(string method)
    {
        var run = RunInDemo($$"""
            public partial class B
            {
                private int field;

                [GenericMethod(2)]
                {{method}}
            }
            """);

        run.Of("GENMETHOD003").Should().ContainSingle();
        run.GeneratedSources.Should().BeEmpty();
        run.Errors.Where(d => d.Id != "GENMETHOD003").Should().BeEmpty(run.ErrorText);
    }

    /// <summary>
    /// G16 (S8): arity 1 is gone. An outside <c>b.Add(list)</c> no longer binds to a public one-argument overload that
    /// wrapped the list in a one-element collection: it does not compile, because the original is private.
    /// </summary>
    [Fact]
    public void AnOutsideCallWithACollection_DoesNotBindToAnOverload()
    {
        var run = RunInDemo("""
            public partial class B
            {
                [GenericMethod(2)]
                private void Add(List<object> values) { }
            }

            public static class Outside
            {
                public static void Call(B b) => b.Add(new List<object> { 1, 2, 3 });
            }
            """);

        run.AllSources.Should().NotContain("Add<T1>(");
        run.Errors.Should().NotBeEmpty("an outside call has no accessible one-argument Add");
        run.Errors.Where(d => d.Location.SourceTree?.FilePath.EndsWith(".g.cs") == true).Should().BeEmpty(run.ErrorText);
    }

    /// <summary>G16 (S8): an in-class call with a collection reaches the original and receives every element.</summary>
    [Fact]
    public void AnInClassCallWithACollection_ReachesTheOriginal()
    {
        var run = RunInDemo("""
            public partial class B
            {
                public static int Last;

                [GenericMethod(2)]
                private void Add(List<object> values) { Last = values.Count; }

                public static int Probe() { new B().Add(new List<object> { 1, 2, 3 }); return Last; }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        InvokeProbe(run, "Demo.B").Should().Be(3);
    }

    #endregion

    #region Diagnostics GENMETHOD001, 002, 004 (G8, G1)

    [Theory]
    [InlineData("public class B { [GenericMethod] private void Add(object[] values) { } }", "B")]
    [InlineData("public class Outer { public partial class B { [GenericMethod] private void Add(object[] values) { } } }", "Outer")]
    [InlineData("file partial class B { [GenericMethod] private void Add(object[] values) { } }", "B")]
    public void ATypeThatCannotBeExtended_IsGENMETHOD001(string body, string offender)
    {
        var run = RunInDemo(body);

        run.Of("GENMETHOD001").Should().ContainSingle();
        var diagnostic = run.Of("GENMETHOD001")[0];
        diagnostic.GetMessage().Should().Contain($"'{offender}'");
        run.GeneratedSources.Should().BeEmpty();
    }

    /// <summary>G8: a non-private method was skipped silently, and <c>private protected</c> was accepted.</summary>
    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    [InlineData("protected")]
    [InlineData("protected internal")]
    [InlineData("private protected")]
    public void ANonPrivateMethod_IsGENMETHOD002(string accessibility)
    {
        var run = RunInDemo($$"""
            public partial class B
            {
                [GenericMethod]
                {{accessibility}} void Add(object[] values) { }
            }
            """);

        run.Of("GENMETHOD002").Should().ContainSingle();
        run.Of("GENMETHOD002")[0].GetMessage().Should().Contain(accessibility);
        run.GeneratedSources.Should().BeEmpty();
    }

    /// <summary>G8: a method without an accessibility modifier is private, and was skipped silently.</summary>
    [Fact]
    public void AnImplicitlyPrivateMethod_Generates()
    {
        var run = RunInDemo("""
            public partial class B
            {
                [GenericMethod(2)]
                void Add(object[] values) { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Add<T1, T2>(");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    public void ACountOutOfRange_IsGENMETHOD004(int count)
    {
        var run = RunInDemo($$"""
            public partial class B
            {
                [GenericMethod({{count}})]
                private void Add(object[] values) { }
            }
            """);

        run.Of("GENMETHOD004").Should().ContainSingle();
        run.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void TheHighestCount_Compiles()
    {
        var run = RunInDemo("""
            public partial class B
            {
                [GenericMethod(16)]
                private void Add(object[] values) { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("T16");
    }

    #endregion

    #region Output hygiene (G5, G13, G14, G15, G17)

    /// <summary>G5: a containing <c>T1</c> was shadowed (CS0693); the overloads now take TArg1, TArg2, ...</summary>
    [Fact]
    public void ATypeParameterInScope_IsNotShadowed()
    {
        var run = RunInDemo("""
            public partial class Outer<T2>
            {
                public partial class Foo<T1>
                {
                    [GenericMethod(3)]
                    private static int Add(object[] values) => values.Length;

                    public static int Probe() => Add(1, 2, 3);
                }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Add<TArg1, TArg2, TArg3>(");
        run.UpdatedCompilation.GetDiagnostics().Where(d => d.Id == "CS0693").Should().BeEmpty();
    }

    /// <summary>The global namespace: the partial stays there, rather than landing in the root namespace as a new type.</summary>
    [Fact]
    public void ATypeInTheGlobalNamespace_StaysThere()
    {
        var run = Run("""
            using MintPlayer.SourceGenerators.Attributes;

            public partial class GlobalBag
            {
                [GenericMethod(2)]
                private static int Add(object[] values) => values.Length;

                public static int Probe() => Add(1, 2);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("namespace");
        InvokeProbe(run, "GlobalBag").Should().Be(2);
    }

    /// <summary>G13, G14: one header, one <c>#nullable enable</c>, one block per type, and consistent line endings.</summary>
    [Fact]
    public void TheFileHasOnePreambleAndOneBlockPerType()
    {
        var run = Run(
            """
            using MintPlayer.SourceGenerators.Attributes;
            namespace Demo;
            public partial class B
            {
                [GenericMethod(2)] private void Add(object[] values) { }
            }
            public partial class C { [GenericMethod(2)] private void Put(object[] values) { } }
            """,
            """
            using MintPlayer.SourceGenerators.Attributes;
            namespace Demo;
            public partial class B
            {
                [GenericMethod(2)] private void Insert(object[] values) { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().ContainSingle();
        var source = run.GeneratedSources[0];
        source.HintName.Should().Be("GenericMethods.g.cs");
        Occurrences(source.Source, "<auto-generated>").Should().Be(1);
        Occurrences(source.Source, "#nullable enable").Should().Be(1);
        Occurrences(source.Source, "partial class B").Should().Be(1);
        Occurrences(source.Source, "partial class C").Should().Be(1);
        source.Source.Should().NotContain(">( ");

        var crlf = Occurrences(source.Source, "\r\n");
        var lf = Occurrences(source.Source, "\n");
        (crlf == 0 || crlf == lf).Should().BeTrue("every line must end the same way");
    }

    /// <summary>G17: <c>@event</c> lost its <c>@</c> and the generated code did not parse.</summary>
    [Fact]
    public void AnEscapedIdentifier_StaysEscaped()
    {
        var run = RunInDemo("""
            public partial class @class
            {
                [GenericMethod(2)]
                private static int @event(object[] values) => values.Length;

                public static int Probe() => @event(1, 2);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("@event<T1, T2>(");
        InvokeProbe(run, "Demo.class").Should().Be(2);
    }

    /// <summary>
    /// Test plan item 7, G6 and G15: the generated code, compiled with nullable enabled, gives zero warnings in
    /// <c>*.g.cs</c> (CS8601 for a non-nullable element, CS4014 for an async method, CS8603 for an annotated return).
    /// </summary>
    [Fact]
    public void TheGeneratedCodeIsWarningFree()
    {
        var run = RunInDemo("""
            #nullable enable
            public partial class B<T>
            {
                [GenericMethod(3)] private void NonNull(object[] values) { }
                [GenericMethod(3)] private void NullableElements(object?[] values) { }
                [GenericMethod(3)] private string? MaybeNull(IEnumerable<object> values) => null;
                [GenericMethod(3)] private async Task Async(List<object> values) { await Task.Yield(); }
                [GenericMethod(3)] private static ValueTask<T?> Static(System.ReadOnlySpan<object?> values) => default;
            }

            public partial record struct R
            {
                [GenericMethod(2)] private int Count(object[] values) => values.Length;
            }

            #nullable disable
            public partial class Oblivious
            {
                [GenericMethod(2)] private string Add(object[] values) => null;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);

        var options = ((CSharpCompilationOptions)run.UpdatedCompilation.Options).WithNullableContextOptions(NullableContextOptions.Enable);
        var warnings = run.UpdatedCompilation.WithOptions(options).GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Where(d => d.Location.SourceTree?.FilePath.EndsWith(".g.cs") == true)
            .Select(d => d.ToString())
            .ToList();
        warnings.Should().BeEmpty(string.Join(Environment.NewLine, warnings));
    }

    /// <summary>G0: the attribute ships in the attributes package, with a default of 4.</summary>
    [Fact]
    public void TheAttributeShipsWithADefaultCountOfFour()
    {
        new Attributes.GenericMethodAttribute().Count.Should().Be(4);
        new Attributes.GenericMethodAttribute(7).Count.Should().Be(7);
        typeof(Attributes.GenericMethodAttribute).Namespace.Should().Be("MintPlayer.SourceGenerators.Attributes");
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    #endregion
}
