using Microsoft.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Pipeline;

/// <summary>
/// Where a build of the repo's generators lives, and how to load its <see cref="IIncrementalGenerator"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Each build loads into its own <see cref="AssemblyLoadContext"/>, the way the compiler loads an analyzer
/// directory. That is what lets master's build and this branch's build run in one process although both
/// contain an assembly called MintPlayer.SourceGenerators.Tools (with and without the value-comparer
/// runtime). Roslyn and the BCL resolve from the host, so both builds run against the same compiler.
/// </para>
/// <para>
/// <b>Branch</b> is this checkout's generator projects, built in the benchmark's configuration (the csproj
/// builds them first). <b>Master</b> is a master checkout built in Release; point <c>BENCH_MASTER_ROOT</c> at
/// its repository root. Without it the Master scenarios are left out.
/// </para>
/// </remarks>
public sealed class GeneratorBuild
{
    public const string SourceGenerators = "MintPlayer.SourceGenerators";
    public const string Mapper = "MintPlayer.Mapper";
    public const string ValueComparerGenerator = "MintPlayer.ValueComparerGenerator";

    private static readonly (string Assembly, string ProjectDir)[] Projects =
    [
        (SourceGenerators, @"SourceGenerators\SourceGenerators\MintPlayer.SourceGenerators"),
        (Mapper, @"SourceGenerators\Mapper\MintPlayer.Mapper"),
        (ValueComparerGenerator, @"SourceGenerators\ValueComparerGenerator\MintPlayer.ValueComparerGenerator"),
    ];

    private readonly Dictionary<string, string> binByAssembly;
    private LoadContext? context;

    private GeneratorBuild(string name, string repoRoot, string configuration)
    {
        Name = name;
        binByAssembly = Projects.ToDictionary(
            p => p.Assembly,
            p => Path.Combine(repoRoot, p.ProjectDir.Replace('\\', Path.DirectorySeparatorChar), "bin", configuration, "netstandard2.0"));
    }

    public string Name { get; }

    public static GeneratorBuild Branch { get; } = new("Branch", RepoRoot(), BuildConfiguration());

    public static GeneratorBuild? Master { get; } =
        Environment.GetEnvironmentVariable("BENCH_MASTER_ROOT") is { Length: > 0 } root ? new("Master", root, "Release") : null;

    public bool Has(string assembly) => File.Exists(Path.Combine(binByAssembly[assembly], assembly + ".dll"));

    /// <summary>Every [Generator] incremental generator in <paramref name="assemblies"/>, ordered by type name.</summary>
    public IIncrementalGenerator[] Load(params string[] assemblies)
    {
        context ??= new LoadContext(Name, binByAssembly.Values.Distinct().ToArray());
        return assemblies
            .Select(a => context.LoadFromAssemblyPath(Path.Combine(binByAssembly[a], a + ".dll")))
            .SelectMany(LoadableTypes)
            .Where(t => !t.IsAbstract && typeof(IIncrementalGenerator).IsAssignableFrom(t) && t.GetCustomAttribute<GeneratorAttribute>() is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IIncrementalGenerator)Activator.CreateInstance(t)!)
            .ToArray();
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

    private static string RepoRoot() =>
        typeof(GeneratorBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepoRoot").Value!;

    private static string BuildConfiguration() =>
        typeof(GeneratorBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "Configuration").Value!;

    private sealed class LoadContext(string name, string[] directories) : AssemblyLoadContext("generators:" + name)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var simple = assemblyName.Name;
            // The compiler and the BCL come from the host, exactly as inside csc/VBCSCompiler.
            if (simple is null || simple.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                || simple.StartsWith("System.", StringComparison.Ordinal) || simple is "netstandard" or "mscorlib")
                return null;

            foreach (var dir in directories)
            {
                var path = Path.Combine(dir, simple + ".dll");
                if (File.Exists(path)) return LoadFromAssemblyPath(path);
            }
            return null; // e.g. Newtonsoft.Json for master's build: resolved from the host's own reference
        }
    }
}
