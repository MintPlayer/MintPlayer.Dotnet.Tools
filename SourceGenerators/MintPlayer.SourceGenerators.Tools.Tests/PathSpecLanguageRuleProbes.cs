using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// PRD-TypeKindRobustness D1 (spike S1): which parts of a user's declaration must a generated partial repeat?
/// Each case is a user part in one file and a generated part in another, compiled at C# 9, 11 and latest.
/// The expected outcome is the C# rule this spike relies on; a changed compiler would turn it red.
/// </summary>
public class PathSpecLanguageRuleProbes
{
    public static TheoryData<string, string, string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string, string, string>();
        void Add(string name, string user, string generated, string expected9, string expected11)
            => data.Add(name, user, generated, expected9, expected11);

        // expected: "ok" or the error id. Latest is expected to behave like 11.
        Add("record + record", "public partial record R(int A);", "partial record R { }", "ok", "ok");
        Add("record class (user) + record (gen)", "public partial record class R(int A);", "partial record R { }", "CS8773", "ok");
        Add("record (user) + record class (gen)", "public partial record R(int A);", "partial record class R { }", "CS8773", "ok");
        Add("record + class", "public partial record R(int A);", "partial class R { }", "CS0261", "CS0261");
        Add("record struct + struct", "public partial record struct R(int A);", "partial struct R { }", "CS8773,CS0261", "CS0261");
        Add("readonly struct, gen omits readonly", "public readonly partial struct S { public int A { get; } }", "partial struct S { }", "ok", "ok");
        Add("ref struct, gen omits ref", "public ref partial struct S { }", "partial struct S { }", "ok", "ok");
        Add("readonly record struct, gen omits readonly", "public readonly partial record struct S(int A);", "partial record struct S { }", "CS8773", "ok");
        Add("static class, gen omits static", "public static partial class C { }", "partial class C { public static int X => 1; }", "ok", "ok");
        Add("abstract sealed unsafe omitted", "public abstract partial class C { }", "partial class C { }", "ok", "ok");
        Add("accessibility omitted", "public partial class C { }", "partial class C { }", "ok", "ok");
        Add("accessibility conflicts", "public partial class C { }", "internal partial class C { }", "CS0262", "CS0262");
        Add("constraints omitted", "public partial class C<T> where T : class, new() { }", "partial class C<T> { T? M() => null; }", "ok", "ok");
        Add("constraints repeated identically", "public partial class C<T> where T : class { }", "partial class C<T> where T : class { }", "ok", "ok");
        Add("constraints differ", "public partial class C<T> where T : class { }", "partial class C<T> where T : struct { }", "CS0265", "CS0265");
        Add("type parameter renamed", "public partial class C<T> { }", "partial class C<U> { }", "CS0264", "CS0264");
        Add("interface variance omitted", "public partial interface I<out T> { }", "partial interface I<T> { }", "CS1067", "CS1067");
        Add("interface variance repeated", "public partial interface I<out T> { }", "partial interface I<out T> { }", "ok", "ok");
        Add("keyword-named type", "public partial class @event { }", "partial class @event { }", "ok", "ok");
        // A `file` partial in another file is a DIFFERENT type: it compiles, and the generated members never reach
        // the user's type (CS1061 below). So a file-local type, or anything nested in one, is unsupported.
        Add("file type from another file", "file partial class F { }", "file partial class F { }", "CS8773", "ok");
        Add("file type members do not merge", "file partial class F { int Y => new F().X; }", "file partial class F { public int X => 1; }", "CS8773,CS1061", "CS1061");
        Add("record with primary ctor, gen with no list", "public partial record R(int A);", "partial record R { public int B => A; }", "ok", "ok");
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Rule(string name, string user, string generated, string expected9, string expected11)
    {
        foreach (var (version, expected) in new[] { ("9", expected9), ("11", expected11), ("latest", expected11) })
        {
            var errors = PathSpecCompiler.Errors(version, "namespace N { " + user + " }", "namespace N { " + generated + " }");
            var actual = errors.Count == 0 ? "ok" : string.Join(",", errors.Select(e => e.Id).Distinct());
            actual.Should().Be(expected, $"{name} at C# {version}: {string.Join(" | ", errors)}");
        }
    }
}

#pragma warning disable RSEXPERIMENTAL006
public class PathSpecUnionProbe
{
    // A C# 15 union is a sixth kind: only `partial union` re-declares it, and its symbol looks like a struct.
    [Theory]
    [InlineData("partial union U { }", "ok")]
    [InlineData("partial struct U { }", "CS0261")]
    [InlineData("partial record struct U { }", "CS0261")]
    public void Union(string generated, string expected)
    {
        var compilation = PathSpecCompiler.Compile("preview", "namespace N { public partial union U(int, string); }", "namespace N { " + generated + " }");
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        (errors.Count == 0 ? "ok" : string.Join(",", errors.Select(e => e.Id).Distinct())).Should().Be(expected, string.Join(" | ", errors));
    }

    [Fact]
    public void UnionSymbolShape()
    {
        var type = PathSpecCompiler.Compile("preview", "namespace N { public partial union U(int, string); }").GetTypeByMetadataName("N.U")!;
        $"{type.TypeKind} IsRecord={type.IsRecord} IsValueType={type.IsValueType} IsUnion={type.IsUnion}"
            .Should().Be("Struct IsRecord=False IsValueType=True IsUnion=True");
    }
}

internal static class PathSpecCompiler
{
    private static readonly MetadataReference[] References = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
        .Where(a => a.GetName().Name is { } n && (n.StartsWith("System.", StringComparison.Ordinal) || n is "netstandard" or "System" or "mscorlib"))
        .Append(typeof(object).Assembly)
        .Distinct()
        .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
        .ToArray();

    /// <summary>By metadata name; a file-local type has a mangled one, so fall back to its simple name.</summary>
    public static INamedTypeSymbol Find(this Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName)
           ?? compilation.GetSymbolsWithName(metadataName.Split('.', '+').Last().Split('`')[0]).OfType<INamedTypeSymbol>().Single();

    public static IReadOnlyList<Diagnostic> Errors(string languageVersion, params string[] sources)
    {
        LanguageVersionFacts.TryParse(languageVersion, out var version).Should().BeTrue();
        var parseOptions = new CSharpParseOptions(version);
        var compilation = CSharpCompilation.Create(
            "S1Probe",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, parseOptions, path: $"File{i}.cs")),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    public static CSharpCompilation Compile(string languageVersion, params string[] sources)
    {
        LanguageVersionFacts.TryParse(languageVersion, out var version).Should().BeTrue();
        var parseOptions = new CSharpParseOptions(version);
        return CSharpCompilation.Create(
            "S1Probe",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, parseOptions, path: $"File{i}.cs")),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
    }
}
