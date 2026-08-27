using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.SourceGenerators.Tests._Infrastructure;

/// <summary>
/// Runs one of this repo's source generators against an in-memory compilation and hands back
/// what it produced.
/// </summary>
/// <remarks>
/// Ported from MintPlayer.Spark's tests/MintPlayer.Spark.SourceGenerators.Tests harness, which
/// is the proven shape for this. Two things about it are load-bearing:
///
/// 1. The generator assemblies are kept OUT of this project's compile-time graph
///    (ReferenceOutputAssembly="false") because MintPlayer.SourceGenerators.Tools polyfills
///    BCL types for netstandard2.0 and they collide with the real ones on net10.0 (CS0433).
///    They are copied into the test bin root instead and loaded here by name.
///
/// 2. <see cref="Assembly.Load(AssemblyName)"/> loads into the DEFAULT load context, from the
///    test output directory — which is the copy coverlet instrumented. Loading them any other
///    way (an Analyzer reference, AssemblyLoadContext, LoadFrom of the generator's own bin)
///    means the code runs but contributes nothing to coverage.
/// </remarks>
internal static class GeneratorHarness
{
    private static readonly Dictionary<string, Assembly> _assemblies = new(StringComparer.Ordinal);

    /// <summary>Every assembly a generator might live in, in load order preference.</summary>
    private static readonly string[] KnownGeneratorAssemblies =
    [
        "MintPlayer.SourceGenerators",
        "MintPlayer.Mapper",
        "MintPlayer.CliGenerator",
        "MintPlayer.ValueComparerGenerator",
    ];

    public static GeneratorRun Run(
        string generatorTypeName,
        IEnumerable<string> sources,
        string? rootNamespace = "TestRoot",
        IEnumerable<Type>? referenceTypes = null,
        string? generatorAssemblyName = null,
        string assemblyName = "TestInput")
    {
        var generator = InstantiateGenerator(generatorTypeName, generatorAssemblyName);

        var sourceList = sources.ToList();
        if (sourceList.Count == 0)
            sourceList.Add("// intentionally empty");

        var compilation = BuildCompilation(sourceList, referenceTypes ?? [], assemblyName);
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new StubAnalyzerConfigOptionsProvider(rootNamespace));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var driverDiagnostics);

        var generated = driver.GetRunResult().GeneratedTrees
            .Select(tree => (HintName: Path.GetFileName(tree.FilePath), Source: tree.GetText().ToString()))
            .OrderBy(x => x.HintName, StringComparer.Ordinal)
            .ToList();

        return new GeneratorRun(driverDiagnostics, generated, updated);
    }

    /// <summary>
    /// Runs a <see cref="DiagnosticAnalyzer"/> and returns only the diagnostics it declares,
    /// so unrelated compile errors in a test fixture do not leak into the assertion.
    /// </summary>
    public static async Task<IReadOnlyList<Diagnostic>> RunAnalyzerAsync(
        string analyzerTypeName,
        IEnumerable<string> sources,
        IEnumerable<Type>? referenceTypes = null,
        string? analyzerAssemblyName = null)
    {
        var analyzer = InstantiateAnalyzer(analyzerTypeName, analyzerAssemblyName);
        var compilation = BuildCompilation(sources, referenceTypes ?? [], "AnalyzerInput");

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync(default);

        var ownIds = analyzer.SupportedDiagnostics.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        return diagnostics.Where(d => ownIds.Contains(d.Id)).ToList();
    }

    #region Reflection over the copied generator assemblies

    private static IIncrementalGenerator InstantiateGenerator(string typeName, string? assemblyName)
        => (IIncrementalGenerator)Activator.CreateInstance(
            FindType(typeName, assemblyName, typeof(IIncrementalGenerator)))!;

    private static DiagnosticAnalyzer InstantiateAnalyzer(string typeName, string? assemblyName)
        => (DiagnosticAnalyzer)Activator.CreateInstance(
            FindType(typeName, assemblyName, typeof(DiagnosticAnalyzer)))!;

    private static Type FindType(string typeName, string? assemblyName, Type assignableTo)
    {
        string[] candidates = assemblyName is null ? KnownGeneratorAssemblies : [assemblyName];

        foreach (var candidate in candidates)
        {
            var type = GetLoadableTypes(Load(candidate))
                .FirstOrDefault(t => t.Name == typeName && assignableTo.IsAssignableFrom(t));

            if (type is not null) return type;
        }

        var known = string.Join(", ", candidates
            .SelectMany(c => GetLoadableTypes(Load(c)))
            .Where(assignableTo.IsAssignableFrom)
            .Select(t => t.Name));

        throw new InvalidOperationException(
            $"'{typeName}' not found as a {assignableTo.Name} in [{string.Join(", ", candidates)}]. Candidates: {known}");
    }

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> throws if ANY type fails to load, and it only takes one
    /// missing optional dependency to lose the whole assembly. Keep whatever did load.
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>();
        }
    }

    private static Assembly Load(string assemblyName)
    {
        lock (_assemblies)
        {
            if (_assemblies.TryGetValue(assemblyName, out var cached)) return cached;

            var loaded = Assembly.Load(new AssemblyName(assemblyName));
            _assemblies[assemblyName] = loaded;
            return loaded;
        }
    }

    #endregion

    private static CSharpCompilation BuildCompilation(
        IEnumerable<string> sources,
        IEnumerable<Type> referenceTypes,
        string assemblyName)
    {
        var trees = sources
            .Select((src, i) => CSharpSyntaxTree.ParseText(src, path: $"Source{i}.cs"))
            .ToList();

        var references = new HashSet<MetadataReference>(
            new[]
            {
                typeof(object).Assembly,
                typeof(List<>).Assembly,
                typeof(Enumerable).Assembly,
                typeof(System.ComponentModel.DescriptionAttribute).Assembly,
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly,
                typeof(Microsoft.Extensions.DependencyInjection.ServiceCollection).Assembly,
                typeof(IServiceProvider).Assembly,
                typeof(System.CommandLine.Command).Assembly,
                typeof(Microsoft.Extensions.Hosting.IHost).Assembly,
                typeof(Microsoft.Extensions.Hosting.Host).Assembly,
                typeof(Attributes.RegisterAttribute).Assembly,
                typeof(Mapper.Attributes.GenerateMapperAttribute).Assembly,
                typeof(CliGenerator.Attributes.CliCommandAttribute).Assembly,
                typeof(Tools.ValueComparerAttribute).Assembly,
                typeof(ValueComparerGenerator.Attributes.AutoValueComparerAttribute).Assembly,
            }
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Where(a => a.GetName().Name is { } n &&
                            (n.StartsWith("System.", StringComparison.Ordinal) || n is "netstandard" or "mscorlib")))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)));

        foreach (var t in referenceTypes)
            references.Add(MetadataReference.CreateFromFile(t.Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

internal sealed record GeneratorRun(
    IEnumerable<Diagnostic> Diagnostics,
    IReadOnlyList<(string HintName, string Source)> GeneratedSources,
    Compilation UpdatedCompilation)
{
    /// <summary>Errors from the generator itself plus errors in the code it produced.</summary>
    public IReadOnlyList<Diagnostic> Errors =>
        Diagnostics.Concat(UpdatedCompilation.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

    public string ErrorText => string.Join(Environment.NewLine, Errors.Select(d => d.ToString()));

    public string? SourceFor(string hintName)
        => GeneratedSources.FirstOrDefault(s => s.HintName == hintName).Source;

    /// <summary>All generated files concatenated, for a "contains this member anywhere" check.</summary>
    public string AllSources => string.Join(Environment.NewLine, GeneratedSources.Select(s => s.Source));
}

/// <summary>
/// Supplies <c>build_property.rootnamespace</c>. Two traps this deliberately handles: the keys
/// are LOWERCASE, and Roslyn's real global-options dictionary is CASE-INSENSITIVE — a
/// case-sensitive dictionary here silently yields a null RootNamespace, and several producers
/// pass it as <c>RootNamespace!</c> and then emit a bare <c>namespace</c>, i.e. CS1001.
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
