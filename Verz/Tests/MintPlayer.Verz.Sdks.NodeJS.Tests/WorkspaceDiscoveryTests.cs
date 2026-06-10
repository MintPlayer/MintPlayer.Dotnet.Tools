using MintPlayer.Verz.Sdks.NodeJS;
using Xunit;

namespace MintPlayer.Verz.Sdks.NodeJS.Tests;

public class WorkspaceDiscoveryTests
{
    [Fact]
    public void npm_workspaces_array_resolves_glob_to_member_dirs()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """
            { "name": "root", "private": true, "workspaces": ["packages/*"] }
            """);
        repo.Write("packages/foo/package.json", """{ "name": "foo" }""");
        repo.Write("packages/bar/package.json", """{ "name": "bar" }""");
        repo.Write("apps/notamember/package.json", """{ "name": "notamember" }""");

        var members = WorkspaceDiscovery.ResolveMemberDirs(repo.Root);

        var memberNames = members.Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "bar", "foo" }, memberNames);
    }

    [Fact]
    public void yarn_classic_workspaces_object_form_works()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """
            { "name": "root", "private": true, "workspaces": { "packages": ["libs/*"] } }
            """);
        repo.Write("libs/a/package.json", """{ "name": "a" }""");
        repo.Write("libs/b/package.json", """{ "name": "b" }""");

        var members = WorkspaceDiscovery.ResolveMemberDirs(repo.Root);

        Assert.Equal(2, members.Count);
    }

    [Fact]
    public void pnpm_workspace_yaml_resolves_globs()
    {
        using var repo = new TempRepo();
        repo.Write("pnpm-workspace.yaml",
            "packages:\n  - 'packages/*'\n  - 'apps/*'\n");
        repo.Write("packages/foo/package.json", """{ "name": "foo" }""");
        repo.Write("apps/bar/package.json", """{ "name": "bar" }""");

        var members = WorkspaceDiscovery.ResolveMemberDirs(repo.Root);

        Assert.Equal(2, members.Count);
    }

    [Fact]
    public void single_package_repo_is_returned_as_one_member()
    {
        using var repo = new TempRepo();
        repo.Write("package.json", """{ "name": "solo", "version": "1.0.0" }""");

        var members = WorkspaceDiscovery.ResolveMemberDirs(repo.Root);

        Assert.Single(members);
        Assert.Equal(repo.Root, members[0]);
    }

    [Fact]
    public void empty_repo_returns_no_members()
    {
        using var repo = new TempRepo();
        var members = WorkspaceDiscovery.ResolveMemberDirs(repo.Root);
        Assert.Empty(members);
    }
}
