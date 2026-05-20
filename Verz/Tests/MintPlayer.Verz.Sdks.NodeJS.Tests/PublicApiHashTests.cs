using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdks.NodeJS;
using Xunit;

namespace MintPlayer.Verz.Sdks.NodeJS.Tests;

public class PublicApiHashTests
{
    private static NodeJsSdk Sdk() => new(NullLogger<NodeJsSdk>.Instance);

    private static DiscoveredProject DiscoveredFor(TempRepo repo, string relativeMemberDir) =>
        new()
        {
            PackageId = "test",
            ProjectDir = repo.PathOf(relativeMemberDir),
            ProjectFile = repo.PathOf(relativeMemberDir + "/package.json"),
            OwnerSdkId = "nodejs",
            FrameworkMajor = null,
        };

    private static async Task<string> Hash(NodeJsSdk sdk, DiscoveredProject project) =>
        await sdk.ComputePublicApiHashAsync(project, "Debug", default);

    [Fact]
    public async Task Identical_file_sets_hash_to_the_same_value()
    {
        using var repo = new TempRepo();
        repo.Write("lib/package.json", """{ "name": "lib", "version": "1.0.0" }""");
        repo.Write("lib/index.ts", "export const x = 1;");
        repo.Write("lib/util.ts", "export const y = 2;");

        var project = DiscoveredFor(repo, "lib");
        var sdk = Sdk();

        var h1 = await Hash(sdk, project);
        var h2 = await Hash(sdk, project);
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length); // SHA-256 hex
    }

    [Fact]
    public async Task Modifying_a_source_file_changes_the_hash()
    {
        using var repo = new TempRepo();
        repo.Write("lib/package.json", """{ "name": "lib", "version": "1.0.0" }""");
        repo.Write("lib/index.ts", "export const x = 1;");

        var project = DiscoveredFor(repo, "lib");
        var sdk = Sdk();

        var before = await Hash(sdk, project);

        repo.Write("lib/index.ts", "export const x = 2;");
        var after = await Hash(sdk, project);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task Adding_a_new_source_file_changes_the_hash()
    {
        using var repo = new TempRepo();
        repo.Write("lib/package.json", """{ "name": "lib", "version": "1.0.0" }""");
        repo.Write("lib/index.ts", "export const x = 1;");

        var project = DiscoveredFor(repo, "lib");
        var sdk = Sdk();

        var before = await Hash(sdk, project);

        repo.Write("lib/extra.ts", "export const z = 3;");
        var after = await Hash(sdk, project);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task node_modules_changes_do_not_affect_the_hash()
    {
        using var repo = new TempRepo();
        repo.Write("lib/package.json", """{ "name": "lib", "version": "1.0.0" }""");
        repo.Write("lib/index.ts", "export const x = 1;");

        var project = DiscoveredFor(repo, "lib");
        var sdk = Sdk();
        var before = await Hash(sdk, project);

        repo.Write("lib/node_modules/lodash/index.js", "module.exports = {};");
        repo.Write("lib/node_modules/foo/big.txt", new string('a', 4096));
        var after = await Hash(sdk, project);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task dist_and_build_output_dirs_are_excluded()
    {
        using var repo = new TempRepo();
        repo.Write("lib/package.json", """{ "name": "lib", "version": "1.0.0" }""");
        repo.Write("lib/index.ts", "export const x = 1;");

        var project = DiscoveredFor(repo, "lib");
        var sdk = Sdk();
        var before = await Hash(sdk, project);

        repo.Write("lib/dist/bundle.js", "compiled output");
        repo.Write("lib/build/whatever", "also output");
        var after = await Hash(sdk, project);

        Assert.Equal(before, after);
    }
}
