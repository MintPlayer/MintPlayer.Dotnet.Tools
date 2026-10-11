using System.CodeDom.Compiler;
using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// PRD-TypeKindRobustness D1 (spike S1): <c>OpenPathSpec(spec, includeTarget: true)</c> re-declares any partial user type,
/// and the result compiles and merges with the user's type at C# 9, 11 and latest.
/// </summary>
public class PathSpecDeclarationTests
{
    private const string Member = "internal static int S1Probe() => 1;";

    // name, user source (inside `namespace N { }`), target metadata name, minimum C# version
    public static TheoryData<string, string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var version in new[] { "9", "11", "latest" })
            foreach (var (name, source, target, min) in All)
                if (Supported(version, min))
                    data.Add(name, source, target, version);
        return data;
    }

    private static bool Supported(string version, string min)
        => version != "9" || min == "9";

    private static readonly (string Name, string Source, string Target, string Min)[] All =
    [
        // The five kinds (and both record spellings) at top level.
        ("class", "public partial class T { }", "N.T", "9"),
        ("record", "public partial record T(int A);", "N.T", "9"),
        ("record class", "public partial record class T(int A);", "N.T", "11"),
        ("struct", "public partial struct T { }", "N.T", "9"),
        ("record struct", "public partial record struct T(int A);", "N.T", "11"),
        ("interface", "public partial interface T { }", "N.T", "9"),
        // Modifiers the re-declaration may omit.
        ("readonly struct", "public readonly partial struct T { }", "N.T", "9"),
        ("ref struct", "public ref partial struct T { }", "N.T", "9"),
        ("readonly ref struct", "public readonly ref partial struct T { }", "N.T", "9"),
        ("readonly record struct", "public readonly partial record struct T(int A);", "N.T", "11"),
        ("static class", "public static partial class T { }", "N.T", "9"),
        ("internal sealed class", "internal sealed partial class T { }", "N.T", "9"),
        ("abstract record", "public abstract partial record T(int A);", "N.T", "9"),
        ("keyword name", "public partial class @event { }", "N.event", "9"),
        // Type parameters: constraints are never repeated; interface variance is.
        ("generic with constraints", "public partial class T<TKey, TValue> where TKey : notnull where TValue : class, new() { }", "N.T`2", "9"),
        ("variant interface", "public partial interface T<in TIn, out TOut> { }", "N.T`2", "9"),
        ("keyword type parameter", "public partial class T<@class> { }", "N.T`1", "9"),
        // Nested in each kind, and in an interface.
        ("in class", "public partial class O { private partial class T { } }", "N.O+T", "9"),
        ("in record", "public partial record O(int A) { protected partial class T { } }", "N.O+T", "9"),
        ("in record class", "public partial record class O(int A) { internal partial struct T { } }", "N.O+T", "11"),
        ("in struct", "public partial struct O { internal partial record T(int A); }", "N.O+T", "9"),
        ("in record struct", "public partial record struct O(int A) { public partial interface T { } }", "N.O+T", "11"),
        ("in interface (class)", "public partial interface O { partial class T { } }", "N.O+T", "9"),
        ("in interface (record struct)", "public partial interface O { partial record struct T(int A); }", "N.O+T", "11"),
        ("in interface (interface)", "public partial interface O { partial interface T { } }", "N.O+T", "9"),
        ("deep mix", "public partial record class A(int X) { partial struct B { partial interface C { partial record struct T(int Y); } } }", "N.A+B+C+T", "11"),
        // Generic parents.
        ("generic parents",
            "public partial class O<TO> where TO : class { partial struct M<TM> where TM : struct { partial record T<TT>(TO A, TM B, TT C) where TT : notnull; } }",
            "N.O`1+M`1+T`1", "9"),
        ("generic interface parent", "public partial interface O<TO> { partial class T<TT> { } }", "N.O`1+T`1", "9"),
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheReDeclarationCompilesAndMerges(string name, string source, string target, string languageVersion)
    {
        var user = "namespace N { " + source + " }";
        var symbol = PathSpecCompiler.Compile(languageVersion, user).GetTypeByMetadataName(target);
        symbol.Should().NotBeNull(name);

        var spec = symbol!.GetPathSpec()!;
        spec.UnsupportedReason.Should().Be(EPathSpecUnsupportedReason.None, name);

        var generated = Emit(spec);
        var compilation = PathSpecCompiler.Compile(languageVersion, user, generated);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString()).ToList();
        errors.Should().BeEmpty($"{name} at C# {languageVersion}:{Environment.NewLine}{generated}{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");

        // Merged, not a phantom type beside the user's: the member lands on the user's own symbol.
        compilation.GetTypeByMetadataName(target)!.GetMembers("S1Probe").Should().NotBeEmpty($"{name}: {generated}");
    }

    [Fact]
    public void TheHeaderNeverCarriesAccessibilityModifiersOrConstraints()
    {
        var spec = Spec("public partial class O<TO> where TO : class { private sealed partial record T<TT>(TT A) where TT : notnull; }", "N.O`1+T`1");
        spec.Parents.Single().Header.Should().Be("partial class O<TO>");
        spec.Target!.Header.Should().Be("partial record T<TT>");
    }

    [Theory]
    [InlineData("public partial record class T(int A);", "partial record T")]
    [InlineData("public readonly partial record struct T(int A);", "partial record struct T")]
    [InlineData("public ref partial struct T { }", "partial struct T")]
    [InlineData("public partial interface T { }", "partial interface T")]
    [InlineData("public partial class @event { }", "partial class @event")]
    public void Headers(string source, string header)
    {
        var target = source.Contains("@event") ? "N.event" : "N.T";
        Spec(source, target).Target!.Header.Should().Be(header);
    }

    [Theory]
    [InlineData("public class T { }", "N.T", EPathSpecUnsupportedReason.TargetNotPartial, "T")]
    [InlineData("public class O { public partial class T { } }", "N.O+T", EPathSpecUnsupportedReason.ParentNotPartial, "O")]
    [InlineData("public partial class A { public class O { public partial class T { } } }", "N.A+O+T", EPathSpecUnsupportedReason.ParentNotPartial, "O")]
    [InlineData("file partial class T { }", "N.T", EPathSpecUnsupportedReason.FileLocal, "T")]
    [InlineData("file partial class F { partial class T { } }", "N.F+T", EPathSpecUnsupportedReason.FileLocal, "F")]
    [InlineData("public enum T { A }", "N.T", EPathSpecUnsupportedReason.UnsupportedKind, "T")]
    [InlineData("public delegate void T();", "N.T", EPathSpecUnsupportedReason.UnsupportedKind, "T")]
    [InlineData("public partial union T(int, string);", "N.T", EPathSpecUnsupportedReason.UnsupportedKind, "T")]
    [InlineData("public partial union O(int, string) { partial class T { } }", "N.O+T", EPathSpecUnsupportedReason.UnsupportedKind, "O")]
    public void UnsupportedReasons(string source, string target, EPathSpecUnsupportedReason reason, string offender)
    {
        var spec = Spec(source, target, "preview");
        spec.UnsupportedReason.Should().Be(reason);
        spec.UnsupportedTypeName.Should().Be(offender);
    }

    [Fact]
    public void AFileTypeReDeclarationWouldNotMerge()
    {
        // Why FileLocal is a reason: the same emission compiles, but declares a second, unrelated type.
        var user = "namespace N { file partial class T { int Use() => S1Probe(); } }";
        var spec = PathSpecCompiler.Compile("latest", user).GetSymbolsWithName("T").OfType<INamedTypeSymbol>().Single().GetPathSpec()!;
        var errors = PathSpecCompiler.Errors("latest", user, Emit(spec, fileLocal: true));
        errors.Select(e => e.Id).Should().Contain("CS0103");
    }

    [Fact]
    public void TypeParameterNamesInScopeAvoidShadowing()
    {
        var spec = Spec("public partial class O<T1> { partial class T<T2, TKey> { } }", "N.O`1+T`2");
        spec.TypeParameterNamesInScope.Should().Equal(["T1", "T2", "TKey"]);
        spec.UniqueTypeParameterName("T1").Should().Be("T11");
        spec.UniqueTypeParameterName("T3").Should().Be("T3");
    }

    [Fact]
    public void TypeParameterPrefixIsFreeForEveryIndex()
    {
        // UniqueTypeParameterName("T1") gives T11, which a numbered series would hit again at arity 11.
        var spec = Spec("public partial class O<T1> { partial class T<T2, TKey> { } }", "N.O`1+T`2");
        spec.UniqueTypeParameterPrefix("T").Should().Be("T_");
        spec.UniqueTypeParameterPrefix("TArg").Should().Be("TArg");
        // "TKey" is not "T" plus digits, so it does not block "T" on its own.
        Spec("public partial class T<TKey> { }", "N.T`1").UniqueTypeParameterPrefix("T").Should().Be("T");
        Spec("public partial class T<TArg11, TArg_2> { }", "N.T`2").UniqueTypeParameterPrefix("TArg").Should().Be("TArg__");
        Spec("public partial class T { }", "N.T").UniqueTypeParameterPrefix("T").Should().Be("T");
    }

    [Fact]
    public void TheSpecIsAnEquatableValue()
    {
        const string source = "public partial class O<TO> { partial record struct T(int A); }";
        var a = Spec(source, "N.O`1+T", "11");
        var b = Spec(source, "N.O`1+T", "11");
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        Spec("public partial class O<TO> { partial record T(int A); }", "N.O`1+T", "11").Should().NotBe(a);
    }

    private static PathSpec Spec(string source, string target, string languageVersion = "latest")
        => PathSpecCompiler.Compile(languageVersion, "namespace N { " + source + " }").Find(target).GetPathSpec()!;

    private static string Emit(PathSpec spec, bool fileLocal = false)
    {
        using var text = new StringWriter();
        using var writer = new IndentedTextWriter(text, "    ");
        using (writer.OpenBlock($"namespace {spec.ContainingNamespace}"))
        using (writer.OpenPathSpec(spec, includeTarget: true, targetLeadingLines: fileLocal ? ["// file"] : null))
            writer.WriteLine(Member);
        writer.Flush();
        var result = text.ToString();
        // The only way to aim at a file type from another file is to write `file` too, which OpenPathSpec never does.
        return fileLocal ? result.Replace("partial class T", "file partial class T") : result;
    }
}
