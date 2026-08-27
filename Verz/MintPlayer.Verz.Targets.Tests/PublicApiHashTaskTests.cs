using System.Xml.Linq;
using MintPlayer.Testing.MSBuild;

namespace MintPlayer.Verz.Targets.Tests;

public sealed class GeneratePublicApiHashTaskTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"verz-targets-{Guid.NewGuid():N}");

    public GeneratePublicApiHashTaskTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AMissingAssembly_IsALoggedErrorRatherThanAThrow()
    {
        var engine = new FakeBuildEngine();
        var task = new GeneratePublicApiHashTask
        {
            BuildEngine = engine,
            AssemblyPath = Path.Combine(_dir, "nope.dll"),
        };

        task.Execute().Should().BeFalse();

        engine.Errors.Should().ContainSingle();
        engine.ErrorText.Should().Contain("Assembly not found");
    }

    /// <summary>
    /// Regression for D4 in docs/PRD-TestCoverage.md. The task called
    /// <c>Assembly.Load(AssemblyPath)</c>, and <c>Assembly.Load(string)</c> takes an assembly
    /// NAME, not a path — so for every real input it threw and the task reported failure. It
    /// could never have succeeded. Loading this test assembly's own DLL by path is the
    /// smallest input that proves the happy path now works.
    /// </summary>
    [Fact]
    public void AValidAssemblyPath_ProducesAHash()
    {
        var engine = new FakeBuildEngine();
        var path = typeof(GeneratePublicApiHashTaskTests).Assembly.Location;

        var task = new GeneratePublicApiHashTask
        {
            BuildEngine = engine,
            AssemblyPath = path,
        };

        task.Execute().Should().BeTrue();

        engine.Errors.Should().BeEmpty();
        task.PublicApiHash.Should().NotBeNullOrWhiteSpace();
        // SHA256 as hex.
        task.PublicApiHash.Should().HaveLength(64);
        task.PublicApiHash.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void TheHashIsStableForTheSameAssembly()
    {
        var path = typeof(GeneratePublicApiHashTaskTests).Assembly.Location;

        string Run()
        {
            var task = new GeneratePublicApiHashTask
            {
                BuildEngine = new FakeBuildEngine(),
                AssemblyPath = path,
            };
            task.Execute().Should().BeTrue();
            return task.PublicApiHash;
        }

        Run().Should().Be(Run());
    }

    [Fact]
    public void ADifferentAssembly_HashesDifferently()
    {
        string HashOf(string path)
        {
            var task = new GeneratePublicApiHashTask
            {
                BuildEngine = new FakeBuildEngine(),
                AssemblyPath = path,
            };
            task.Execute().Should().BeTrue();
            return task.PublicApiHash;
        }

        var mine = HashOf(typeof(GeneratePublicApiHashTaskTests).Assembly.Location);
        var theirs = HashOf(typeof(GeneratePublicApiHashTask).Assembly.Location);

        theirs.Should().NotBe(mine);
    }

    [Fact]
    public void TheSuccessPathLogsTheHash()
    {
        var engine = new FakeBuildEngine();
        var task = new GeneratePublicApiHashTask
        {
            BuildEngine = engine,
            AssemblyPath = typeof(GeneratePublicApiHashTaskTests).Assembly.Location,
        };

        task.Execute();

        engine.Messages.Should().Contain(m => m.Message!.Contains("Generated SHA256 hash"));
    }
}

public sealed class InjectPublicApiHashTaskTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"verz-nuspec-{Guid.NewGuid():N}");

    public InjectPublicApiHashTaskTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string WriteNuspec(string body)
    {
        var path = Path.Combine(_dir, "package.nuspec");
        File.WriteAllText(path, body);
        return path;
    }

    private const string WithMetadata = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>My.Package</id>
            <version>1.0.0</version>
          </metadata>
        </package>
        """;

    private static string? HashIn(string path)
    {
        var doc = XDocument.Load(path);
        var ns = doc.Root!.Name.Namespace;
        return doc.Root.Element(ns + "metadata")?.Element(ns + "PublicApiHash")?.Value;
    }

    [Fact]
    public void ItAddsTheHashElement()
    {
        var path = WriteNuspec(WithMetadata);
        var task = new InjectPublicApiHashTask
        {
            BuildEngine = new FakeBuildEngine(),
            NuspecPath = path,
            PublicApiHash = "ABC123",
        };

        task.Execute().Should().BeTrue();

        HashIn(path).Should().Be("ABC123");
    }

    [Fact]
    public void ItOverwritesAnExistingHashElement()
    {
        var path = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>My.Package</id>
                <PublicApiHash>OLD</PublicApiHash>
              </metadata>
            </package>
            """);

        var task = new InjectPublicApiHashTask
        {
            BuildEngine = new FakeBuildEngine(),
            NuspecPath = path,
            PublicApiHash = "NEW",
        };

        task.Execute().Should().BeTrue();

        HashIn(path).Should().Be("NEW");
    }

    [Fact]
    public void ItPreservesTheRestOfTheNuspec()
    {
        var path = WriteNuspec(WithMetadata);
        var task = new InjectPublicApiHashTask
        {
            BuildEngine = new FakeBuildEngine(),
            NuspecPath = path,
            PublicApiHash = "ABC",
        };

        task.Execute();

        var doc = XDocument.Load(path);
        var ns = doc.Root!.Name.Namespace;
        doc.Root.Element(ns + "metadata")!.Element(ns + "id")!.Value.Should().Be("My.Package");
        doc.Root.Element(ns + "metadata")!.Element(ns + "version")!.Value.Should().Be("1.0.0");
    }

    #region Deliberately non-fatal paths

    /// <summary>
    /// The task returns true from every failure path, on purpose: its own comment says "do
    /// not break pack". These tests pin that so nobody "fixes" it into a build breaker
    /// without deciding to — but they also assert something is LOGGED each time, so a failure
    /// is at least visible.
    /// </summary>
    [Fact]
    public void AMissingNuspec_IsANoOpNotAFailure()
    {
        var engine = new FakeBuildEngine();
        var task = new InjectPublicApiHashTask
        {
            BuildEngine = engine,
            NuspecPath = Path.Combine(_dir, "absent.nuspec"),
            PublicApiHash = "ABC",
        };

        task.Execute().Should().BeTrue();

        engine.Errors.Should().BeEmpty();
        engine.Messages.Should().Contain(m => m.Message!.Contains("Nuspec not found"));
    }

    [Fact]
    public void AnEmptyNuspecPath_IsANoOpNotAFailure()
    {
        var task = new InjectPublicApiHashTask
        {
            BuildEngine = new FakeBuildEngine(),
            NuspecPath = string.Empty,
            PublicApiHash = "ABC",
        };

        task.Execute().Should().BeTrue();
    }

    [Fact]
    public void ANuspecWithoutMetadata_WarnsButSucceeds()
    {
        var path = WriteNuspec("""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
            </package>
            """);

        var engine = new FakeBuildEngine();
        var task = new InjectPublicApiHashTask
        {
            BuildEngine = engine,
            NuspecPath = path,
            PublicApiHash = "ABC",
        };

        task.Execute().Should().BeTrue();

        engine.Warnings.Should().ContainSingle();
        engine.WarningText.Should().Contain("missing <metadata>");
    }

    [Fact]
    public void MalformedXml_WarnsButSucceeds()
    {
        var path = WriteNuspec("this is not xml <<<");

        var engine = new FakeBuildEngine();
        var task = new InjectPublicApiHashTask
        {
            BuildEngine = engine,
            NuspecPath = path,
            PublicApiHash = "ABC",
        };

        task.Execute().Should().BeTrue();

        engine.Errors.Should().BeEmpty();
        engine.Warnings.Should().ContainSingle();
        engine.WarningText.Should().Contain("Failed to inject PublicApiHash");
    }

    #endregion
}
