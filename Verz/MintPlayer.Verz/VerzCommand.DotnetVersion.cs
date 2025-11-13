using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Verz.Core;
using MintPlayer.Verz.Helpers;
using NuGet.Versioning;

namespace MintPlayer.Verz;

[CliCommand("dotnet-version", Description = "Compute next version for a .NET project")]
[CliParentCommand(typeof(VerzCommand))]
internal partial class DotnetVersionCommand : ICliCommand
{
    private readonly ToolCatalog toolCatalog;

    public DotnetVersionCommand(ToolCatalog toolCatalog)
    {
        this.toolCatalog = toolCatalog;
    }

    [CliOption("--project", Description = ".csproj file"), NoInterfaceMember]
    public string? Project { get; set; }

    [CliOption("--configuration", Description = "Build configuration (for locating bin)", DefaultValue = "Release"), NoInterfaceMember]
    public string Configuration { get; set; } = "Release";

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        var toolset = await toolCatalog.GetToolsetAsync(cancellationToken);
        var registries = toolset.Registries;
        var sdks = toolset.Sdks;

        var project = string.IsNullOrWhiteSpace(Project) ? Program.FindSingleCsprojInCwd() : Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            Console.Error.WriteLine("No project specified and none detected in the current directory.");
            return -1;
        }

        var sdk = sdks.FirstOrDefault(s => s.CanHandle(project));
        if (sdk == null)
        {
            Console.Error.WriteLine("No compatible SDK found to handle project: " + project);
            return -1;
        }

        var packageId = await sdk.GetPackageIdAsync(project, cancellationToken);
        var major = await sdk.GetMajorVersionAsync(project, cancellationToken);

        var versionSet = new HashSet<NuGetVersion>(VersionComparer.VersionRelease);
        var latestPerRegistry = new List<(IPackageRegistry Registry, NuGetVersion Version)>();
        foreach (var registry in registries)
        {
            var versions = await registry.GetAllVersionsAsync(packageId, cancellationToken);
            var inMajor = versions.Where(v => v.Major == major).OrderBy(v => v).ToList();
            if (inMajor.Count == 0)
            {
                continue;
            }

            latestPerRegistry.Add((registry, inMajor.Last()));
            foreach (var version in inMajor)
            {
                versionSet.Add(version);
            }
        }

        NuGetVersion? latest = versionSet.OrderBy(v => v).LastOrDefault();
        if (latest == null)
        {
            Console.WriteLine($"{major}.0.0");
            return -1;
        }

        var source = latestPerRegistry.FirstOrDefault(x => VersionComparer.Version.Equals(x.Version, latest)).Registry
                     ?? registries.First();

        await using var nupkg = await source.DownloadPackageAsync(packageId, latest, cancellationToken) ?? Stream.Null;
        string? prevHash = null;
        if (nupkg != Stream.Null)
        {
            prevHash = await sdk.ComputePackagePublicApiHashAsync(nupkg, major, cancellationToken);
        }
        var currentHash = await sdk.ComputeCurrentPublicApiHashAsync(project, Configuration, cancellationToken);

        NuGetVersion next;
        if (!string.IsNullOrWhiteSpace(prevHash) && string.Equals(prevHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            next = new NuGetVersion(major, latest.Minor, latest.Patch + 1);
        }
        else
        {
            next = new NuGetVersion(major, latest.Minor + 1, 0);
        }

        Console.WriteLine(next.ToNormalizedString());
        return 0;
    }
}
