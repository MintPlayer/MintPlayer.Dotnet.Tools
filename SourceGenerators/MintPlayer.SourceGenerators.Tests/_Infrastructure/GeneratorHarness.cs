using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Testing;

namespace MintPlayer.SourceGenerators.Tests._Infrastructure;

/// <summary>
/// This solution folder's four generators, wired up for
/// <see cref="MintPlayer.SourceGenerators.Testing.GeneratorHarness"/>.
/// </summary>
/// <remarks>
/// The mechanics — loading the component from the test output directory so coverage attributes,
/// the stub analyzer-config options, tolerating a partial type load — now live in the
/// MintPlayer.SourceGenerators.Testing package. What stays here is only what is specific to this
/// repo: which assemblies to probe, and which libraries the fixtures compile against.
///
/// The probing exists because tests name a generator by type name alone and the four generators
/// live in four assemblies. Tests that already know the assembly can pass it and skip the search.
/// </remarks>
internal static class GeneratorHarness
{
    private static readonly string[] KnownGeneratorAssemblies =
    [
        "MintPlayer.SourceGenerators",
        "MintPlayer.Mapper",
        "MintPlayer.CliGenerator",
        "MintPlayer.ValueComparerGenerator",
    ];

    /// <summary>
    /// Everything a fixture in this project might reference. Attribute packages a generator
    /// triggers on, plus the libraries the generated code itself compiles against —
    /// ServiceRegistrationsGenerator returns nothing at all without
    /// Microsoft.Extensions.DependencyInjection in the compilation.
    /// </summary>
    private static readonly Type[] FixtureReferences =
    [
        typeof(System.ComponentModel.DescriptionAttribute),
        typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
        typeof(Microsoft.Extensions.DependencyInjection.ServiceCollection),
        typeof(IServiceProvider),
        typeof(System.CommandLine.Command),
        typeof(Microsoft.Extensions.Hosting.IHost),
        typeof(Microsoft.Extensions.Hosting.Host),
        typeof(Attributes.RegisterAttribute),
        typeof(Mapper.Attributes.GenerateMapperAttribute),
        typeof(CliGenerator.Attributes.CliCommandAttribute),
        typeof(Tools.ValueComparerAttribute),
        typeof(ValueComparerGenerator.Attributes.AutoValueComparerAttribute),
    ];

    private static readonly Dictionary<string, Testing.GeneratorHarness> _harnesses =
        KnownGeneratorAssemblies.ToDictionary(
            name => name,
            name => Testing.GeneratorHarness.ForAssembly(name).AddReferences(FixtureReferences),
            StringComparer.Ordinal);

    public static GeneratorRun Run(
        string generatorTypeName,
        IEnumerable<string> sources,
        string? rootNamespace = "TestRoot",
        IEnumerable<Type>? referenceTypes = null,
        string? generatorAssemblyName = null,
        string assemblyName = "TestInput")
        => Probe(
            generatorAssemblyName,
            rootNamespace,
            referenceTypes,
            h => h.RunGenerator(generatorTypeName, sources.ToArray()));

    public static IncrementalGeneratorResult RunIncremental(
        string generatorTypeName,
        IEnumerable<string> initialSources,
        IEnumerable<string> modifiedSources,
        string? rootNamespace = "TestRoot",
        IEnumerable<Type>? referenceTypes = null,
        string? generatorAssemblyName = null)
        => Probe(
            generatorAssemblyName,
            rootNamespace,
            referenceTypes,
            h => h.RunGeneratorTwice(generatorTypeName, initialSources.ToArray(), modifiedSources.ToArray()));

    public static Task<IReadOnlyList<Diagnostic>> RunAnalyzerAsync(
        string analyzerTypeName,
        IEnumerable<string> sources,
        IEnumerable<Type>? referenceTypes = null,
        string? analyzerAssemblyName = null)
        => ProbeAsync(
            analyzerAssemblyName,
            referenceTypes,
            h => h.RunAnalyzerAsync(analyzerTypeName, sources.ToArray()));

    /// <summary>
    /// Runs <paramref name="action"/> against each candidate assembly until one does not report an
    /// unknown type.
    /// </summary>
    /// <remarks>
    /// The package throws <see cref="InvalidOperationException"/> when a component type is not in
    /// the assembly it was asked about, which for a probe is a "try the next one" rather than a
    /// failure. The final assembly's exception is allowed to escape, so a genuinely missing type
    /// still fails with the package's message listing what it did find.
    /// </remarks>
    private static T Probe<T>(
        string? assemblyName,
        string? rootNamespace,
        IEnumerable<Type>? referenceTypes,
        Func<Testing.GeneratorHarness, T> action)
    {
        var candidates = Candidates(assemblyName, rootNamespace, referenceTypes);

        for (var i = 0; i < candidates.Count; i++)
        {
            try { return action(candidates[i]); }
            catch (InvalidOperationException) when (i < candidates.Count - 1) { }
        }

        throw new InvalidOperationException("No candidate generator assemblies configured.");
    }

    private static async Task<T> ProbeAsync<T>(
        string? assemblyName,
        IEnumerable<Type>? referenceTypes,
        Func<Testing.GeneratorHarness, Task<T>> action)
    {
        var candidates = Candidates(assemblyName, rootNamespace: "TestRoot", referenceTypes);

        for (var i = 0; i < candidates.Count; i++)
        {
            try { return await action(candidates[i]); }
            catch (InvalidOperationException) when (i < candidates.Count - 1) { }
        }

        throw new InvalidOperationException("No candidate analyzer assemblies configured.");
    }

    private static IReadOnlyList<Testing.GeneratorHarness> Candidates(
        string? assemblyName,
        string? rootNamespace,
        IEnumerable<Type>? referenceTypes)
    {
        var names = assemblyName is null ? KnownGeneratorAssemblies : [assemblyName];
        var extra = referenceTypes?.ToArray() ?? [];

        return names
            .Select(n => _harnesses.TryGetValue(n, out var h)
                ? h
                : Testing.GeneratorHarness.ForAssembly(n).AddReferences(FixtureReferences))
            .Select(h => extra.Length == 0 ? h : h.AddReferences(extra))
            .Select(h => h.WithRootNamespace(rootNamespace))
            .ToList();
    }
}
