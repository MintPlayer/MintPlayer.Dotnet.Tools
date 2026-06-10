using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdks.NodeJS;
using Xunit;

namespace MintPlayer.Verz.Sdks.NodeJS.Tests;

public class NodeJsSdkTests
{
    private static NodeJsSdk Sdk() => new(NullLogger<NodeJsSdk>.Instance);

    [Fact]
    public async Task Discover_skips_private_packages()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root", "private": true, "workspaces": ["packages/*"] }""");
        repo.Write("packages/public/package.json", """{ "name": "public-lib", "version": "1.0.0" }""");
        repo.Write("packages/private/package.json", """{ "name": "private-lib", "private": true, "version": "1.0.0" }""");

        var projects = await Sdk().DiscoverAsync(repo.Root, default);

        Assert.Single(projects);
        Assert.Equal("public-lib", projects[0].PackageId);
    }

    [Fact]
    public async Task Discover_skips_packages_with_no_name()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root", "private": true, "workspaces": ["packages/*"] }""");
        repo.Write("packages/named/package.json", """{ "name": "named" }""");
        repo.Write("packages/anon/package.json", """{ "version": "1.0.0" }""");

        var projects = await Sdk().DiscoverAsync(repo.Root, default);

        Assert.Single(projects);
        Assert.Equal("named", projects[0].PackageId);
    }

    [Fact]
    public async Task Discover_reports_framework_major()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root", "private": true, "workspaces": ["packages/*"] }""");
        repo.Write("packages/ng-lib/package.json", """
            { "name": "ng-lib", "version": "1.0.0",
              "dependencies": { "@angular/core": "^17.2.1" } }
            """);

        var projects = await Sdk().DiscoverAsync(repo.Root, default);

        Assert.Single(projects);
        Assert.Equal(17, projects[0].FrameworkMajor);
    }

    [Fact]
    public async Task StampVersion_updates_package_json_version_field()
    {
        using var repo = new TempRepo();
        repo.Write("packages/lib/package.json", """
            { "name": "lib", "version": "0.0.0-placeholder", "description": "kept" }
            """);

        var project = new DiscoveredProject
        {
            PackageId = "lib",
            ProjectDir = repo.PathOf("packages/lib"),
            ProjectFile = repo.PathOf("packages/lib/package.json"),
            OwnerSdkId = "nodejs",
            FrameworkMajor = null,
        };

        await Sdk().StampVersionAsync(project, "1.2.3", default);

        var reread = new PackageJsonReader(project.ProjectFile);
        Assert.Equal("1.2.3", reread.Version);
        Assert.Equal("lib", reread.Name);  // other fields preserved
    }

    [Fact]
    public async Task EnumerateInRepoDependencies_resolves_workspace_deps()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root", "private": true, "workspaces": ["packages/*"] }""");
        repo.Write("packages/core/package.json", """{ "name": "@scope/core", "version": "1.0.0" }""");
        repo.Write("packages/app/package.json", """
            { "name": "@scope/app", "version": "1.0.0",
              "dependencies": { "@scope/core": "^1.0.0", "lodash": "^4.0.0" } }
            """);

        var sdk = Sdk();
        var projects = await sdk.DiscoverAsync(repo.Root, default);
        var index = projects.ToDictionary(p => p.PackageId);

        var app = projects.First(p => p.PackageId == "@scope/app");
        var deps = await sdk.EnumerateInRepoDependenciesAsync(app, index, default);

        Assert.Equal(new[] { "@scope/core" }, deps);
    }

    [Fact]
    public async Task EnumerateInRepoDependencies_includes_dev_and_peer()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "root", "private": true, "workspaces": ["packages/*"] }""");
        repo.Write("packages/a/package.json", """{ "name": "a", "version": "1.0.0" }""");
        repo.Write("packages/b/package.json", """{ "name": "b", "version": "1.0.0" }""");
        repo.Write("packages/c/package.json", """
            { "name": "c", "version": "1.0.0",
              "devDependencies": { "a": "^1.0.0" },
              "peerDependencies": { "b": "^1.0.0" } }
            """);

        var sdk = Sdk();
        var projects = await sdk.DiscoverAsync(repo.Root, default);
        var index = projects.ToDictionary(p => p.PackageId);

        var c = projects.First(p => p.PackageId == "c");
        var deps = await sdk.EnumerateInRepoDependenciesAsync(c, index, default);

        Assert.Equal(new[] { "a", "b" }, deps.OrderBy(x => x).ToArray());
    }
}
