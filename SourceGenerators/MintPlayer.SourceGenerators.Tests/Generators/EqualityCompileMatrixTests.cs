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

        // The generator's own diagnostics: only the user-declared shape reports anything, and only MINT002.
        generatorDiagnostics.Where(d => d.Id != "MINT002").Should().BeEmpty();
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

        var generatorType = Testing.GeneratorHarness.ForAssembly("MintPlayer.ValueComparerGenerator")
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
            typeof(ValueComparerGenerator.Attributes.AutoValueComparerAttribute).Assembly,
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
