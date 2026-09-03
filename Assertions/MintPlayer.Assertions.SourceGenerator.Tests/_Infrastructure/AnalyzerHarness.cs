using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.Assertions.SourceGenerator.Tests._Infrastructure;

/// <summary>
/// Loads MintPlayer.Assertions.SourceGenerator out of this project's own output directory and runs
/// its analyzers, code fixes and generators against in-memory compilations.
/// </summary>
/// <remarks>
/// The load path is the whole point. <see cref="Assembly.Load(AssemblyName)"/> resolves the copy
/// that <c>CopyGeneratorRuntimeAssets</c> put in the test bin root — which is the copy coverlet
/// instrumented. Referencing the project as <c>OutputItemType="Analyzer"</c> instead, as
/// MintPlayer.Assertions.Tests does, has Roslyn load it from the generator's own bin/ through
/// AnalyzerFileReference, and coverage is then exactly zero however many tests run.
///
/// This is a trimmed sibling of SourceGenerators/MintPlayer.SourceGenerators.Tests'
/// GeneratorHarness rather than a shared file, because that one hardcodes a reference set and an
/// assembly list for the four generators in that solution folder. Folding the two into one
/// parameterised harness is R4.1 in docs/PRD-TestCoverage-Phase2.md.
/// </remarks>
internal static class AnalyzerHarness
{
    private const string GeneratorAssembly = "MintPlayer.Assertions.SourceGenerator";

    private static Assembly? _assembly;

    private static Assembly GeneratorAsm
    {
        get
        {
            lock (typeof(AnalyzerHarness))
                return _assembly ??= Assembly.Load(new AssemblyName(GeneratorAssembly));
        }
    }

    /// <summary>Runs one <see cref="DiagnosticAnalyzer"/> and returns only the ids it declares.</summary>
    /// <remarks>
    /// Filtering to the analyzer's own <see cref="DiagnosticAnalyzer.SupportedDiagnostics"/> keeps
    /// unrelated compile errors in a fixture out of the assertion, so a test fails for the reason
    /// it names rather than because a using was missing.
    /// </remarks>
    public static async Task<IReadOnlyList<Diagnostic>> RunAnalyzerAsync(string analyzerTypeName, string source)
    {
        var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(
            FindType(analyzerTypeName, typeof(DiagnosticAnalyzer)))!;

        var compilation = BuildCompilation(source);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync(default);

        var ownIds = analyzer.SupportedDiagnostics.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        return diagnostics.Where(d => ownIds.Contains(d.Id)).ToList();
    }

    /// <summary>The descriptors an analyzer advertises, without running it.</summary>
    public static ImmutableArray<DiagnosticDescriptor> DescriptorsOf(string analyzerTypeName)
        => ((DiagnosticAnalyzer)Activator.CreateInstance(
                FindType(analyzerTypeName, typeof(DiagnosticAnalyzer)))!).SupportedDiagnostics;

    /// <summary>Runs one incremental generator and returns what it produced.</summary>
    public static GeneratorRun RunGenerator(string generatorTypeName, string source)
    {
        var generator = (IIncrementalGenerator)Activator.CreateInstance(
            FindType(generatorTypeName, typeof(IIncrementalGenerator)))!;

        var compilation = BuildCompilation(source);
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new StubAnalyzerConfigOptionsProvider("TestRoot"));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var driverDiagnostics);

        var generated = driver.GetRunResult().GeneratedTrees
            .Select(t => (HintName: Path.GetFileName(t.FilePath), Source: t.GetText().ToString()))
            .OrderBy(x => x.HintName, StringComparer.Ordinal)
            .ToList();

        return new GeneratorRun(driverDiagnostics, generated, updated);
    }

    /// <summary>Every code-fix provider in the assembly that offers a fix for <paramref name="diagnosticId"/>.</summary>
    public static IReadOnlyList<Type> CodeFixProvidersFor(string diagnosticId)
        => GetLoadableTypes()
            .Where(t => !t.IsAbstract && typeof(Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider).IsAssignableFrom(t))
            .Where(t => ((Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider)Activator.CreateInstance(t)!)
                .FixableDiagnosticIds.Contains(diagnosticId))
            .ToList();

    private static Type FindType(string typeName, Type assignableTo)
    {
        var type = GetLoadableTypes().FirstOrDefault(t => t.Name == typeName && assignableTo.IsAssignableFrom(t));
        if (type is not null) return type;

        var known = string.Join(", ", GetLoadableTypes().Where(assignableTo.IsAssignableFrom).Select(t => t.Name));
        throw new InvalidOperationException(
            $"'{typeName}' not found as a {assignableTo.Name} in {GeneratorAssembly}. Candidates: {known}");
    }

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> throws if any single type fails to load, and one missing
    /// optional dependency loses the whole assembly. Keep whatever did load.
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes()
    {
        try
        {
            return GeneratorAsm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>();
        }
    }

    private static CSharpCompilation BuildCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Source0.cs");

        var references = new HashSet<MetadataReference>(
            new[]
            {
                typeof(object).Assembly,
                typeof(List<>).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Task).Assembly,
                // The fixtures call .Should(), so the assertion surface has to be resolvable.
                typeof(MintPlayer.Assertions.AssertionExtensions).Assembly,
            }
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Where(a => a.GetName().Name is { } n &&
                            (n.StartsWith("System.", StringComparison.Ordinal) || n is "netstandard" or "mscorlib")))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)));

        return CSharpCompilation.Create(
            assemblyName: "AnalyzerInput",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

internal sealed record GeneratorRun(
    IEnumerable<Diagnostic> Diagnostics,
    IReadOnlyList<(string HintName, string Source)> GeneratedSources,
    Compilation UpdatedCompilation)
{
    public IReadOnlyList<Diagnostic> Errors =>
        Diagnostics.Concat(UpdatedCompilation.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

    public string ErrorText => string.Join(Environment.NewLine, Errors.Select(d => d.ToString()));

    public string AllSources => string.Join(Environment.NewLine, GeneratedSources.Select(s => s.Source));
}

/// <summary>
/// Supplies <c>build_property.rootnamespace</c>. The keys are lowercase and Roslyn's real global
/// options dictionary is case-INSENSITIVE; a case-sensitive one here silently yields a null
/// RootNamespace and producers then emit a bare <c>namespace</c>, i.e. CS1001.
/// </summary>
internal sealed class StubAnalyzerConfigOptionsProvider(string? rootNamespace) : AnalyzerConfigOptionsProvider
{
    private readonly StubOptions _options = new(rootNamespace);

    public override AnalyzerConfigOptions GlobalOptions => _options;
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;

    private sealed class StubOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public StubOptions(string? rootNamespace)
        {
            if (!string.IsNullOrEmpty(rootNamespace))
                _values["build_property.rootnamespace"] = rootNamespace!;
        }

        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}
