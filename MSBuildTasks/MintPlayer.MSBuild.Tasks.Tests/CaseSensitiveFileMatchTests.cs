using MintPlayer.MSBuild.Tasks;
using MintPlayer.Testing.MSBuild;

namespace MintPlayer.MSBuild.Tasks.Tests;

/// <summary>
/// The task's whole purpose is to detect a filename whose case does not match, which Windows
/// normally hides. Both platforms end up with the same answer by different routes: on Linux
/// EnumerateFiles simply does not return the mismatched file, while on Windows it does and the
/// Ordinal comparison rejects it. So these tests are meaningful on both.
/// </summary>
public sealed class CaseSensitiveFileMatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"csfm-{Guid.NewGuid():N}");

    public CaseSensitiveFileMatchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "content");
        return path;
    }

    private (CaseSensitiveFileMatch Task, FakeBuildEngine Engine) Create(string candidate)
    {
        var engine = new FakeBuildEngine();
        var task = new CaseSensitiveFileMatch
        {
            BuildEngine = engine,
            Directory = _dir,
            Candidate = candidate,
        };
        return (task, engine);
    }

    [Fact]
    public void AnExactCaseMatch_IsFound()
    {
        Touch("Program.cs");
        var (task, _) = Create("Program.cs");

        task.Execute().Should().BeTrue();

        task.Exists.Should().BeTrue();
        task.MatchedFiles.Should().ContainSingle();
    }

    [Fact]
    public void AMismatchedCase_IsNotFound()
    {
        Touch("program.cs");
        var (task, _) = Create("Program.cs");

        task.Execute().Should().BeTrue();

        task.Exists.Should().BeFalse();
        task.MatchedFiles.Should().BeEmpty();
    }

    [Fact]
    public void AnAbsentFile_IsNotFound()
    {
        var (task, _) = Create("Missing.cs");

        task.Execute().Should().BeTrue();

        task.Exists.Should().BeFalse();
        task.MatchedFiles.Should().BeEmpty();
    }

    [Fact]
    public void TheMatchedItemCarriesItsMetadata()
    {
        Touch("Widget.cs");
        var (task, _) = Create("Widget.cs");

        task.Execute();

        var item = task.MatchedFiles.Should().ContainSingle().Which;
        item.GetMetadata("BaseName").Should().Be("Widget.cs");
        item.GetMetadata("Exists").Should().Be("true");
        item.ItemSpec.Should().EndWith("Widget.cs");
    }

    [Fact]
    public void ACandidateGivenAsAFullPath_MatchesOnItsFileName()
    {
        Touch("Deep.cs");
        var (task, _) = Create(Path.Combine("some", "other", "place", "Deep.cs"));

        task.Execute().Should().BeTrue();

        task.Exists.Should().BeTrue();
    }

    [Fact]
    public void OtherFilesInTheDirectoryAreIgnored()
    {
        Touch("A.cs");
        Touch("B.cs");
        var (task, _) = Create("A.cs");

        task.Execute();

        task.MatchedFiles.Should().ContainSingle()
            .Which.ItemSpec.Should().EndWith("A.cs");
    }

    [Fact]
    public void TheTaskLogsNothingOnEitherPath()
    {
        Touch("Quiet.cs");
        var (task, engine) = Create("Quiet.cs");

        task.Execute();

        // No Log usage at all, which is why the task runs on a bare instance. Pinned so a
        // future Log call does not silently start requiring more engine setup.
        engine.Errors.Should().BeEmpty();
        engine.Warnings.Should().BeEmpty();
        engine.Messages.Should().BeEmpty();
    }

    [Fact]
    public void AMissingDirectory_ThrowsRatherThanReportingFalse()
    {
        var engine = new FakeBuildEngine();
        var task = new CaseSensitiveFileMatch
        {
            BuildEngine = engine,
            Directory = Path.Combine(_dir, "does-not-exist"),
            Candidate = "A.cs",
        };

        // Characterization: Execute has no try/catch, so the exception escapes to MSBuild
        // rather than becoming a logged error.
        var act = () => task.Execute();

        act.Should().Throw<DirectoryNotFoundException>();
    }
}
