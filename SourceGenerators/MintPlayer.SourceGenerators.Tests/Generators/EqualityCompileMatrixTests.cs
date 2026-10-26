using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tests.Snapshots;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// PRD spike S3: every generated equality shape compiles with ZERO warnings, with warnings as errors and nullable
/// enabled, at LangVersion 9, 11 and latest. The compiler's equality contract warnings are what this is for:
/// CS8851 (record Equals without GetHashCode), CS8872 (record Equals must be virtual), CS0659 and CS0661 (Equals
/// without GetHashCode), CS0436 (a generated type clashing with a referenced one).
/// </summary>
/// <remarks>
/// Drives the generator directly rather than through the harness, because the harness has no language-version
/// or compilation-options knob.
/// </remarks>
public class EqualityCompileMatrixTests
{
    private static readonly string[] LanguageVersions = ["9", "11", "latest"];

    public static TheoryData<string, string> Matrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var shape in EqualityShapes.All)
            foreach (var version in LanguageVersions)
                if (!(version == "9" && EqualityShapes.NeedCSharp10.Contains(shape)))
                    data.Add(shape, version);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void EveryShapeCompilesWithoutASingleWarning(string shape, string languageVersion)
    {
        LanguageVersionFacts.TryParse(languageVersion, out var version).Should().BeTrue();
        var (compilation, generatorDiagnostics, generated) = Run(EqualityShapes.Get(shape), version);

        generated.Should().NotBeEmpty();

        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToList();
        problems.Should().BeEmpty(string.Join(Environment.NewLine, problems));

        // The generator's own diagnostics: only the user-declared shape reports anything, and only MINT002/MINT008.
        generatorDiagnostics.Where(d => d.Id is not ("MINT002" or "MINT008")).Should().BeEmpty();
    }

    /// <summary>The five kinds a model can be declared as, with the keyword the generated part re-declares it with.</summary>
    private static readonly (string Declared, string Generated)[] Kinds =
    [
        ("class", "class"), ("record class", "record"), ("record", "record"), ("struct", "struct"), ("record struct", "record struct"),
    ];

    /// <summary>Where the model sits: at top level, or nested in a parent of each kind or an interface.</summary>
    private static readonly (string Declared, string Generated)[] Containers =
    [
        ("", ""), .. Kinds, ("interface", "interface"),
    ];

    public static TheoryData<string, string> KindsAndContainers()
    {
        var data = new TheoryData<string, string>();
        foreach (var kind in Kinds)
            foreach (var container in Containers)
                data.Add(kind.Declared, container.Declared);
        return data;
    }

    /// <summary>
    /// The five-kind matrix (PRD-TypeKindRobustness test plan 1 and 7): every kind, at top level and nested in each kind
    /// and in an interface, generates with the user's keyword and compiles without a single warning under nullable.
    /// </summary>
    [Theory]
    [MemberData(nameof(KindsAndContainers))]
    public void EveryKind_AtTopLevelAndNestedInEveryKind_CompilesWithoutAWarning(string kind, string container)
    {
        var model = $$"""
            [GenerateEquality]
            public partial {{kind}} Model
            {
                public int Count { get; set; }
                public string? Name { get; set; }
            }
            """;
        var body = container.Length == 0 ? model : $$"""
            public partial {{container}} {{(container == "interface" ? "IContainer" : "Container")}}
            {
            {{model}}
            }
            """;
        var source = $$"""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo
            {
            {{body}}
            }
            """;

        var (compilation, generatorDiagnostics, generated) = Run(source, LanguageVersion.Latest);

        generatorDiagnostics.Should().BeEmpty();
        var problems = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString()).ToList();
        problems.Should().BeEmpty(string.Join(Environment.NewLine, problems));

        var file = generated.Should().ContainSingle().Which;
        file.Should().MatchRegex($@"partial {Kinds.Single(k => k.Declared == kind).Generated} Model( :|\r?\n)");
        if (container.Length > 0)
            file.Should().MatchRegex($@"partial {Containers.Single(c => c.Declared == container).Generated} {(container == "interface" ? "IContainer" : "Container")}\r?\n");
    }

    /// <summary>K7: a ref struct cannot be boxed, so <c>obj is S</c> was CS8121. Now it is MINT007, and nothing is generated.</summary>
    [Fact]
    public void ARefStruct_IsMINT007_AndGeneratesNothing()
    {
        var (compilation, generatorDiagnostics, generated) = Run(EqualityShapes.RefStruct, LanguageVersion.Latest);

        generated.Should().BeEmpty();
        var error = generatorDiagnostics.Should().ContainSingle().Which;
        error.Id.Should().Be("MINT007");
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage().Should().Contain("'Cursor'").And.Contain("ref struct");
        compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
    }

    /// <summary>
    /// S1: a C# 15 union binds as a struct, and Equality used to emit <c>partial struct U</c> for it (CS0261). It is
    /// MINT007 now. The fixture's own compile errors (if the runtime lacks union support) don't matter here.
    /// </summary>
    [Fact]
    public void AUnion_IsMINT007_AndGeneratesNothing()
    {
        var (_, generatorDiagnostics, generated) = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo
            {
                [GenerateEquality]
                public partial union Shape(int, string);
            }
            """, LanguageVersion.Preview);

        generated.Should().BeEmpty();
        var error = generatorDiagnostics.Should().ContainSingle().Which;
        error.Id.Should().Be("MINT007");
        error.GetMessage().Should().Contain("'Shape' is not a class, struct or record");
    }

    /// <summary>
    /// The #193 case: unrelated partial code (a <c>[GeneratedRegex]</c> partial method in a record, a record struct, a
    /// struct and an interface) next to one model. Nothing crashes, and the model still generates.
    /// </summary>
    [Fact]
    public void UnrelatedPartialCode_InEveryKind_LeavesTheModelAlone()
    {
        var source = """
            using System.Text.RegularExpressions;
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo
            {
                public partial record R { [GeneratedRegex("a")] private static partial Regex A(); }
                public partial record struct RS { [GeneratedRegex("b")] private static partial Regex B(); }
                public partial struct S { [GeneratedRegex("c")] private static partial Regex C(); }
                public partial interface I { [GeneratedRegex("d")] private static partial Regex D(); }

                [GenerateEquality]
                public sealed partial class Model
                {
                    public int Count { get; set; }
                }
            }
            """;

        var (compilation, generatorDiagnostics, generated) = Run(source, LanguageVersion.Latest);

        generatorDiagnostics.Should().BeEmpty();
        generated.Should().ContainSingle().Which.Should().Contain("partial class Model : global::System.IEquatable<global::Demo.Model>");
        // The regex methods have no implementation (the regex generator does not run here); the generated file is clean.
        compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
            .Should().BeEmpty();
    }

    [Fact]
    public void TheGeneratedCodeReferencesNoneOfTheOldComparerRuntime()
    {
        foreach (var shape in EqualityShapes.All)
        {
            var (_, _, generated) = Run(EqualityShapes.Get(shape), LanguageVersion.Latest);
            foreach (var source in generated)
            {
                source.Should().NotContain("ValueComparer<");
                source.Should().NotContain("ComparerRegistry");
                source.Should().NotContain("ValueComparerAttribute");
                source.Should().NotContain("WithComparer");
                source.Should().NotContain("WithNullableComparer");
            }
        }
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> GeneratorDiagnostics, IReadOnlyList<string> Generated) Run(string source, LanguageVersion version)
    {
        var parseOptions = new CSharpParseOptions(version);
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            generalDiagnosticOption: ReportDiagnostic.Error,
            // The SDK suppresses these two assembly-unification warnings by default; a raw compilation does not.
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                ["CS1701"] = ReportDiagnostic.Suppress,
                ["CS1702"] = ReportDiagnostic.Suppress,
            });

        var compilation = CSharpCompilation.Create(
            "EqualityMatrix",
            [CSharpSyntaxTree.ParseText(source, parseOptions, path: "Fixture.cs")],
            References(),
            options);

        var generatorType = TestHarness.GeneratorHarness.ForAssembly("MintPlayer.ValueComparerGenerator")
            .GeneratorTypes()
            .Single(t => t.Name == "ValueComparerGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;

        var driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);

        var generated = driver.GetRunResult().GeneratedTrees.Select(t => t.GetText().ToString()).ToList();
        return (updated, diagnostics, generated);
    }

    private static IReadOnlyList<MetadataReference> References()
    {
        var assemblies = new HashSet<Assembly>
        {
            typeof(object).Assembly,
            typeof(ImmutableArray).Assembly,
            typeof(MintPlayer.SourceGenerators.Tools.ValueEquality).Assembly,
            typeof(ValueComparerGenerator.Attributes.GenerateEqualityAttribute).Assembly,
        };
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.IsDynamic || string.IsNullOrEmpty(a.Location)) continue;
            var name = a.GetName().Name;
            if (name is not null && (name.StartsWith("System.", StringComparison.Ordinal) || name is "netstandard" or "System"))
                assemblies.Add(a);
        }
        return assemblies.Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)).ToList();
    }
}
