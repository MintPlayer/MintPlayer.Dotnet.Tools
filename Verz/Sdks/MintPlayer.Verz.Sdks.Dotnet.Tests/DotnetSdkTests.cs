using MintPlayer.Verz.Sdks.Dotnet;

namespace MintPlayer.Verz.Sdks.Dotnet.Tests;

/// <summary>
/// Covers the three XDocument-based members. The two hash members are deliberately left
/// alone: they need a real built assembly on disk (Assembly.LoadFrom) or a real .nupkg, which
/// is a build-fixture concern rather than a unit test.
/// </summary>
public sealed class DotnetSdkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"verz-sdk-{Guid.NewGuid():N}");
    private readonly DotnetSdk _sdk = new();

    public DotnetSdkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string WriteProject(string name, string body)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{body}</PropertyGroup></Project>");
        return path;
    }

    #region CanHandle

    [Theory]
    [InlineData("a.csproj", true)]
    [InlineData("a.CSPROJ", true)]
    [InlineData("path/to/a.csproj", true)]
    [InlineData("a.fsproj", false)]
    [InlineData("a.vbproj", false)]
    [InlineData("a.sln", false)]
    [InlineData("csproj", false)]
    [InlineData("", false)]
    public void CanHandle_AcceptsOnlyCsproj(string path, bool expected)
        => _sdk.CanHandle(path).Should().Be(expected);

    #endregion

    #region GetPackageIdAsync

    [Fact]
    public async Task GetPackageIdAsync_ReadsAnExplicitPackageId()
    {
        var path = WriteProject("Whatever.csproj", "<PackageId>My.Package</PackageId>");

        (await _sdk.GetPackageIdAsync(path, default)).Should().Be("My.Package");
    }

    [Fact]
    public async Task GetPackageIdAsync_TrimsWhitespace()
    {
        var path = WriteProject("Whatever.csproj", "<PackageId>  My.Package  </PackageId>");

        (await _sdk.GetPackageIdAsync(path, default)).Should().Be("My.Package");
    }

    [Fact]
    public async Task GetPackageIdAsync_FallsBackToTheFileName()
    {
        var path = WriteProject("Fallback.Name.csproj", "<TargetFramework>net10.0</TargetFramework>");

        (await _sdk.GetPackageIdAsync(path, default)).Should().Be("Fallback.Name");
    }

    [Fact]
    public async Task GetPackageIdAsync_FallsBackWhenPackageIdIsBlank()
    {
        var path = WriteProject("Blank.csproj", "<PackageId>   </PackageId>");

        (await _sdk.GetPackageIdAsync(path, default)).Should().Be("Blank");
    }

    [Fact]
    public async Task GetPackageIdAsync_OnAMissingFile_Throws()
    {
        var act = async () => await _sdk.GetPackageIdAsync(Path.Combine(_dir, "nope.csproj"), default);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    #endregion

    #region GetMajorVersionAsync

    [Theory]
    [InlineData("net8.0", 8)]
    [InlineData("net9.0", 9)]
    [InlineData("net10.0", 10)]
    public async Task GetMajorVersionAsync_ReadsASingleTargetFramework(string tfm, int expected)
    {
        var path = WriteProject("A.csproj", $"<TargetFramework>{tfm}</TargetFramework>");

        (await _sdk.GetMajorVersionAsync(path, default)).Should().Be(expected);
    }

    [Fact]
    public async Task GetMajorVersionAsync_PicksTheHighestOfSeveralTargetFrameworks()
    {
        var path = WriteProject("A.csproj", "<TargetFrameworks>net8.0;net10.0;net9.0</TargetFrameworks>");

        (await _sdk.GetMajorVersionAsync(path, default)).Should().Be(10);
    }

    [Fact]
    public async Task GetMajorVersionAsync_IgnoresNonNetTargetFrameworks()
    {
        var path = WriteProject("A.csproj", "<TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks>");

        (await _sdk.GetMajorVersionAsync(path, default)).Should().Be(8);
    }

    [Fact]
    public async Task GetMajorVersionAsync_TrimsWhitespaceBetweenTargetFrameworks()
    {
        var path = WriteProject("A.csproj", "<TargetFrameworks> net8.0 ; net10.0 </TargetFrameworks>");

        (await _sdk.GetMajorVersionAsync(path, default)).Should().Be(10);
    }

    [Fact]
    public async Task GetMajorVersionAsync_PrefersTargetFrameworkOverTargetFrameworks()
    {
        var path = WriteProject("A.csproj",
            "<TargetFramework>net9.0</TargetFramework><TargetFrameworks>net8.0;net10.0</TargetFrameworks>");

        (await _sdk.GetMajorVersionAsync(path, default)).Should().Be(9);
    }

    [Fact]
    public async Task GetMajorVersionAsync_WithNoTargetFramework_Throws()
    {
        var path = WriteProject("A.csproj", "<Nothing>here</Nothing>");

        var act = async () => await _sdk.GetMajorVersionAsync(path, default);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Cannot determine .NET TargetFramework*");
    }

    [Fact]
    public async Task GetMajorVersionAsync_WithOnlyNetstandard_Throws()
    {
        var path = WriteProject("A.csproj", "<TargetFramework>netstandard2.0</TargetFramework>");

        var act = async () => await _sdk.GetMajorVersionAsync(path, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Characterization, and a known limitation recorded in docs/PRD-TestCoverage.md rather
    /// than fixed. IsNetTfm accepts anything matching net&lt;digit&gt;, and ParseNetMajor reads
    /// every leading digit — so the .NET Framework moniker net472 parses as major version
    /// 472 and outranks net10.0. Whether .NET Framework should be supported at all is a
    /// product decision, not something a coverage pass should settle.
    /// </summary>
    [Fact]
    public async Task GetMajorVersionAsync_MisreadsNetFrameworkMonikers()
    {
        var path = WriteProject("A.csproj", "<TargetFrameworks>net472;net10.0</TargetFrameworks>");

        (await _sdk.GetMajorVersionAsync(path, default)).Should().Be(472);
    }

    [Fact]
    public async Task GetMajorVersionAsync_HandlesADocumentWithAnXmlNamespace()
    {
        var path = Path.Combine(_dir, "Ns.csproj");
        File.WriteAllText(path,
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        (await _sdk.GetMajorVersionAsync(path, default)).Should().Be(10);
    }

    #endregion

    #region ComputeCurrentPublicApiHashAsync guard

    [Fact]
    public async Task ComputeCurrentPublicApiHashAsync_WithoutABuildOutput_ThrowsAClearError()
    {
        var path = WriteProject("A.csproj", "<TargetFramework>net10.0</TargetFramework>");

        var act = async () => await _sdk.ComputeCurrentPublicApiHashAsync(path, "Release", default);

        (await act.Should().ThrowAsync<FileNotFoundException>())
            .WithMessage("*Build the project first*");
    }

    [Fact]
    public async Task ComputeCurrentPublicApiHashAsync_WithNoNetTargetFramework_Throws()
    {
        var path = WriteProject("A.csproj", "<TargetFramework>netstandard2.0</TargetFramework>");

        var act = async () => await _sdk.ComputeCurrentPublicApiHashAsync(path, "Release", default);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*No supported net TargetFramework*");
    }

    #endregion
}
