using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Configuration;
using MintPlayer.Verz.Helpers;
using MintPlayer.Verz.Hosting;
using NuGet.Versioning;
using Xunit;

namespace MintPlayer.Verz.Tests;

public sealed class VersionPlannerTests : IDisposable
{
    private readonly string _repo;

    public VersionPlannerTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "verz-planner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repo);
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "test@test.test");
        Git("config", "user.name", "Test");
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }

    private void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException(
            $"git {string.Join(' ', args)} failed: {proc.StandardError.ReadToEnd()}");
    }

    private string CreateProject(string name, int frameworkMajor)
    {
        var dir = Path.Combine(_repo, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"),
            $"<Project><PropertyGroup><TargetFramework>net{frameworkMajor}.0</TargetFramework><PackageId>{name}</PackageId></PropertyGroup></Project>");
        return dir;
    }

    private void Commit(string message) { Git("add", "-A"); Git("commit", "-qm", message); }
    private void Tag(string tag) => Git("tag", tag);

    private ProjectNode Node(string id, int? fx, params string[] deps) => new()
    {
        PackageId = id,
        ProjectDir = Path.Combine(_repo, id),
        ProjectFile = Path.Combine(_repo, id, $"{id}.csproj"),
        OwnerSdkId = "fake",
        FrameworkMajor = fx,
        Dependencies = deps,
    };

    private VersionPlanner CreatePlanner() =>
        new(new GitClient(NullLogger<GitClient>.Instance), NullLogger<VersionPlanner>.Instance);

    [Fact]
    public async Task First_release_for_untagged_lib_uses_framework_major()
    {
        CreateProject("Foo", 10);
        Commit("initial");

        var graph = new ProjectGraph(new Dictionary<string, ProjectNode>
        {
            ["Foo"] = Node("Foo", fx: 10),
        });

        var plans = await CreatePlanner().PlanAsync(
            graph,
            registries: Array.Empty<RegistryWithPlugin>(),
            sdksById: new Dictionary<string, IDevelopmentSdk>(),
            repoRoot: _repo,
            configuration: "Debug",
            cancellationToken: default);

        Assert.Single(plans);
        Assert.Equal(BumpLevel.Initial, plans["Foo"].BumpLevel);
        Assert.Equal("10.0.0", plans["Foo"].NewVersion.ToNormalizedString());
    }

    [Fact]
    public async Task Unchanged_lib_with_prior_tag_is_skipped()
    {
        CreateProject("Foo", 10);
        Commit("initial");
        Tag("Foo/v10.0.0");

        var graph = new ProjectGraph(new Dictionary<string, ProjectNode>
        {
            ["Foo"] = Node("Foo", fx: 10),
        });

        var plans = await CreatePlanner().PlanAsync(
            graph,
            registries: Array.Empty<RegistryWithPlugin>(),
            sdksById: new Dictionary<string, IDevelopmentSdk>(),
            repoRoot: _repo,
            configuration: "Debug",
            cancellationToken: default);

        Assert.Empty(plans); // no source change since tag, no deps
    }

    [Fact]
    public async Task Cross_major_framework_bump_forces_major()
    {
        CreateProject("Foo", 9);
        Commit("v9");
        Tag("Foo/v9.4.2");

        // Now lift the framework to 10 with a source change.
        File.WriteAllText(Path.Combine(_repo, "Foo", "Foo.csproj"),
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>Foo</PackageId></PropertyGroup></Project>");
        Commit("bump to net10");

        var graph = new ProjectGraph(new Dictionary<string, ProjectNode>
        {
            ["Foo"] = Node("Foo", fx: 10),
        });

        // Mock registry that has the prior package recorded with fx=9.
        var registry = new FakeRegistry
        {
            { ("Foo", "9.4.2"), new PriorPackageInfo { FrameworkMajor = 9, PublicApiHash = "abc" } },
        };
        var rwp = new[] { new RegistryWithPlugin(new RegistryEntry { Id = "fake", Url = "fake://" }, registry) };

        var plans = await CreatePlanner().PlanAsync(
            graph, rwp,
            sdksById: new Dictionary<string, IDevelopmentSdk>(),
            repoRoot: _repo, configuration: "Debug", cancellationToken: default);

        Assert.Single(plans);
        Assert.Equal(BumpLevel.Major, plans["Foo"].BumpLevel);
        Assert.Equal("10.0.0", plans["Foo"].NewVersion.ToNormalizedString());
    }

    [Fact]
    public async Task Public_api_change_yields_minor_bump()
    {
        CreateProject("Foo", 10);
        Commit("v10");
        Tag("Foo/v10.0.0");

        // Change source so git diff sees something.
        File.WriteAllText(Path.Combine(_repo, "Foo", "Lib.cs"), "public class Foo {}");
        Commit("add public type");

        var graph = new ProjectGraph(new Dictionary<string, ProjectNode>
        {
            ["Foo"] = Node("Foo", fx: 10),
        });

        // Prior hash differs from what our fake SDK returns -> MINOR
        var registry = new FakeRegistry
        {
            { ("Foo", "10.0.0"), new PriorPackageInfo { FrameworkMajor = 10, PublicApiHash = "PRIOR" } },
        };
        var sdks = new Dictionary<string, IDevelopmentSdk>
        {
            ["fake"] = new FakeSdk("CURRENT"),
        };
        var rwp = new[] { new RegistryWithPlugin(new RegistryEntry { Id = "fake", Url = "fake://" }, registry) };

        var plans = await CreatePlanner().PlanAsync(graph, rwp, sdks, _repo, "Debug", default);

        Assert.Equal(BumpLevel.Minor, plans["Foo"].BumpLevel);
        Assert.Equal("10.1.0", plans["Foo"].NewVersion.ToNormalizedString());
    }

    [Fact]
    public async Task Source_change_with_same_hash_yields_patch_bump()
    {
        CreateProject("Foo", 10);
        Commit("v10");
        Tag("Foo/v10.0.0");

        File.WriteAllText(Path.Combine(_repo, "Foo", "internal_refactor.cs"), "// trivial");
        Commit("refactor");

        var graph = new ProjectGraph(new Dictionary<string, ProjectNode>
        {
            ["Foo"] = Node("Foo", fx: 10),
        });

        var registry = new FakeRegistry
        {
            { ("Foo", "10.0.0"), new PriorPackageInfo { FrameworkMajor = 10, PublicApiHash = "SAME" } },
        };
        var sdks = new Dictionary<string, IDevelopmentSdk> { ["fake"] = new FakeSdk("SAME") };
        var rwp = new[] { new RegistryWithPlugin(new RegistryEntry { Id = "fake", Url = "fake://" }, registry) };

        var plans = await CreatePlanner().PlanAsync(graph, rwp, sdks, _repo, "Debug", default);

        Assert.Equal(BumpLevel.Patch, plans["Foo"].BumpLevel);
        Assert.Equal("10.0.1", plans["Foo"].NewVersion.ToNormalizedString());
    }

    [Fact]
    public async Task Transitive_minor_dep_with_unchanged_consumer_demotes_to_patch()
    {
        // Dep changed minor; consumer source unchanged. Consumer should bump PATCH (not MINOR).
        CreateProject("Dep", 10);
        CreateProject("Consumer", 10);
        Commit("v10");
        Tag("Dep/v10.0.0");
        Tag("Consumer/v10.0.0");

        // Source change ONLY in Dep — Consumer's directory has no diff.
        File.WriteAllText(Path.Combine(_repo, "Dep", "added.cs"), "public class Added {}");
        Commit("dep API change");

        var graph = new ProjectGraph(new Dictionary<string, ProjectNode>
        {
            ["Dep"] = Node("Dep", fx: 10),
            ["Consumer"] = Node("Consumer", fx: 10, "Dep"),
        });

        var registry = new FakeRegistry
        {
            { ("Dep", "10.0.0"), new PriorPackageInfo { FrameworkMajor = 10, PublicApiHash = "OLD" } },
            { ("Consumer", "10.0.0"), new PriorPackageInfo { FrameworkMajor = 10, PublicApiHash = "CONSUMER" } },
        };
        var sdks = new Dictionary<string, IDevelopmentSdk>
        {
            ["fake"] = new FakeSdk(hashFor: id => id == "Dep" ? "NEW" : "CONSUMER"),
        };
        var rwp = new[] { new RegistryWithPlugin(new RegistryEntry { Id = "fake", Url = "fake://" }, registry) };

        var plans = await CreatePlanner().PlanAsync(graph, rwp, sdks, _repo, "Debug", default);

        Assert.Equal(BumpLevel.Minor, plans["Dep"].BumpLevel);
        Assert.Equal("10.1.0", plans["Dep"].NewVersion.ToNormalizedString());
        Assert.Equal(BumpLevel.Patch, plans["Consumer"].BumpLevel); // demoted from MINOR
        Assert.Equal("10.0.1", plans["Consumer"].NewVersion.ToNormalizedString());
    }

    [Fact]
    public async Task Cold_start_when_no_registry_has_the_prior_package()
    {
        CreateProject("Foo", 10);
        Commit("v10");
        Tag("Foo/v10.0.0");

        // Touch source so we don't hit the SKIP path.
        File.WriteAllText(Path.Combine(_repo, "Foo", "Foo.cs"), "// x");
        Commit("touch");

        var graph = new ProjectGraph(new Dictionary<string, ProjectNode>
        {
            ["Foo"] = Node("Foo", fx: 10),
        });

        // Registry returns null for all lookups.
        var rwp = new[] { new RegistryWithPlugin(new RegistryEntry { Id = "fake", Url = "fake://" }, new FakeRegistry()) };

        var ex = await Assert.ThrowsAsync<ColdStartException>(() =>
            CreatePlanner().PlanAsync(graph, rwp, new Dictionary<string, IDevelopmentSdk>(), _repo, "Debug", default));
        Assert.Equal(5, ex.ExitCode);
    }

    // ---- Fakes -----------------------------------------------------------

    private sealed class FakeRegistry : IPackageRegistry,
        System.Collections.Generic.IEnumerable<KeyValuePair<(string, string), PriorPackageInfo>>
    {
        private readonly Dictionary<(string, string), PriorPackageInfo> _store = new();
        public void Add((string id, string version) key, PriorPackageInfo value) => _store[key] = value;
        public string Kind => "fake";
        public IReadOnlyList<string> AcceptedKinds => Array.Empty<string>();
        public bool CanHandle(string registryUrl) => true;
        public Task<PriorPackageInfo?> LookupAsync(string url, string id, NuGetVersion v, CancellationToken ct) =>
            Task.FromResult(_store.TryGetValue((id, v.ToNormalizedString()), out var info) ? info : null);
        public Task PushAsync(string url, Artifact a, CancellationToken ct) => Task.CompletedTask;
        public System.Collections.Generic.IEnumerator<KeyValuePair<(string, string), PriorPackageInfo>> GetEnumerator() => _store.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FakeSdk : IDevelopmentSdk
    {
        private readonly Func<string, string> _hashFor;
        public FakeSdk(string fixedHash) => _hashFor = _ => fixedHash;
        public FakeSdk(Func<string, string> hashFor) => _hashFor = hashFor;
        public string Id => "fake";
        public Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(string r, CancellationToken c) =>
            Task.FromResult<IReadOnlyList<DiscoveredProject>>(Array.Empty<DiscoveredProject>());
        public Task<IReadOnlyList<string>> EnumerateInRepoDependenciesAsync(
            DiscoveredProject p, IReadOnlyDictionary<string, DiscoveredProject> i, CancellationToken c) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string> ComputePublicApiHashAsync(DiscoveredProject p, string cfg, CancellationToken c) =>
            Task.FromResult(_hashFor(p.PackageId));
        public Task StampVersionAsync(DiscoveredProject p, string v, CancellationToken c) => Task.CompletedTask;
        public Task<IReadOnlyList<Artifact>> PackAsync(DiscoveredProject p, string cfg, CancellationToken c) =>
            Task.FromResult<IReadOnlyList<Artifact>>(Array.Empty<Artifact>());
    }
}
