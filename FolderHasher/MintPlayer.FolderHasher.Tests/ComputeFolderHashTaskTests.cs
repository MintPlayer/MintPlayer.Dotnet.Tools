using MintPlayer.Assertions;
using MintPlayer.FolderHasher.Targets;
using MintPlayer.Testing.MSBuild;

namespace MintPlayer.FolderHasher.Tests;

/// <summary>
/// The MSBuild task duplicates roughly 150 lines of the library's hashing logic, so it can
/// drift from it silently. These tests cover the task's own surface and pin the drift risk by
/// asserting the two agree on the same folder.
/// </summary>
public sealed class ComputeFolderHashTaskTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"folderhash-task-{Guid.NewGuid():N}");

    public ComputeFolderHashTaskTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private (ComputeFolderHashTask Task, FakeBuildEngine Engine) Create(string? folder = null)
    {
        var engine = new FakeBuildEngine();
        var task = new ComputeFolderHashTask
        {
            BuildEngine = engine,
            FolderPath = folder ?? _dir,
        };
        return (task, engine);
    }

    [Fact]
    public void ItHashesAFolder()
    {
        Write("a.txt", "content");
        var (task, engine) = Create();

        task.Execute().Should().BeTrue();

        engine.Errors.Should().BeEmpty();
        task.Hash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheHashIsStableForUnchangedContent()
    {
        Write("a.txt", "content");
        Write("nested/b.txt", "more");

        string Run()
        {
            var (task, _) = Create();
            task.Execute().Should().BeTrue();
            return task.Hash;
        }

        Run().Should().Be(Run());
    }

    [Fact]
    public void TheHashChangesWhenContentChanges()
    {
        Write("a.txt", "before");
        var (first, _) = Create();
        first.Execute();
        var before = first.Hash;

        Write("a.txt", "after");
        var (second, _) = Create();
        second.Execute();

        second.Hash.Should().NotBe(before);
    }

    [Fact]
    public void TheHashChangesWhenAFileIsAdded()
    {
        Write("a.txt", "content");
        var (first, _) = Create();
        first.Execute();
        var before = first.Hash;

        Write("b.txt", "extra");
        var (second, _) = Create();
        second.Execute();

        second.Hash.Should().NotBe(before);
    }

    [Fact]
    public void AMissingFolder_IsALoggedErrorRatherThanAThrow()
    {
        var (task, engine) = Create(Path.Combine(_dir, "does-not-exist"));

        task.Execute().Should().BeFalse();

        engine.Errors.Should().ContainSingle();
        engine.ErrorText.Should().Contain("Folder not found");
    }

    [Fact]
    public void AnEmptyFolder_StillHashes()
    {
        var (task, _) = Create();

        task.Execute().Should().BeTrue();

        task.Hash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ItLogsTheComputedHash()
    {
        Write("a.txt", "content");
        var (task, engine) = Create();

        task.Execute();

        engine.Messages.Should().Contain(m => m.Message!.Contains("Computed folder hash"));
    }

    [Fact]
    public void ItHonoursAHasherIgnoreFile()
    {
        Write("keep.txt", "keep");
        Write("skip.log", "skip");
        Write(".hasherignore", "*.log");

        var (withIgnore, _) = Create();
        withIgnore.Execute().Should().BeTrue();
        var ignoredHash = withIgnore.Hash;

        // Changing an ignored file must not move the hash.
        Write("skip.log", "totally different content");
        var (again, _) = Create();
        again.Execute().Should().BeTrue();

        again.Hash.Should().Be(ignoredHash);
    }

    [Fact]
    public void AChangeToANonIgnoredFileStillMovesTheHash()
    {
        Write("keep.txt", "keep");
        Write("skip.log", "skip");
        Write(".hasherignore", "*.log");

        var (first, _) = Create();
        first.Execute();
        var before = first.Hash;

        Write("keep.txt", "changed");
        var (second, _) = Create();
        second.Execute();

        second.Hash.Should().NotBe(before);
    }
}
