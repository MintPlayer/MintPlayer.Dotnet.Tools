using Microsoft.CodeAnalysis;
using System.Reflection;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Pipeline;

/// <summary>
/// Loads this checkout's generator assemblies, built in the benchmark's configuration (the csproj builds them
/// first), from their own <c>bin</c> folders.
/// </summary>
/// <remarks>
/// They load into the default context. Every dependency they have (MintPlayer.SourceGenerators.Tools, the
/// attribute assemblies, Roslyn) is a netstandard2.0 or host assembly the benchmark itself references, so it
/// resolves to the host's copy, as it does inside csc/VBCSCompiler.
/// </remarks>
public static class GeneratorBuild
{
    public const string SourceGenerators = "MintPlayer.SourceGenerators";
    public const string Mapper = "MintPlayer.Mapper";
    public const string ValueComparerGenerator = "MintPlayer.ValueComparerGenerator";

    private static readonly Dictionary<string, string> ProjectDirs = new()
    {
        [SourceGenerators] = @"SourceGenerators\SourceGenerators\MintPlayer.SourceGenerators",
        [Mapper] = @"SourceGenerators\Mapper\MintPlayer.Mapper",
        [ValueComparerGenerator] = @"SourceGenerators\ValueComparerGenerator\MintPlayer.ValueComparerGenerator",
    };

    /// <summary>Every [Generator] incremental generator in <paramref name="assemblies"/>, ordered by type name.</summary>
    public static IIncrementalGenerator[] Load(params string[] assemblies) => assemblies
        .Select(a => Assembly.LoadFrom(PathOf(a)))
        .SelectMany(LoadableTypes)
        .Where(t => !t.IsAbstract && typeof(IIncrementalGenerator).IsAssignableFrom(t) && t.GetCustomAttribute<GeneratorAttribute>() is not null)
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .Select(t => (IIncrementalGenerator)Activator.CreateInstance(t)!)
        .ToArray();

    private static string PathOf(string assembly)
    {
        var metadata = typeof(GeneratorBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value!);
        return Path.Combine(metadata["RepoRoot"], ProjectDirs[assembly].Replace('\\', Path.DirectorySeparatorChar),
            "bin", metadata["Configuration"], "netstandard2.0", assembly + ".dll");
    }

    /// <summary>
    /// The generator assemblies also hold code fixes, whose base types live in Workspaces; a type that fails to
    /// load is not a generator, so it is skipped rather than failing the whole assembly.
    /// </summary>
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
