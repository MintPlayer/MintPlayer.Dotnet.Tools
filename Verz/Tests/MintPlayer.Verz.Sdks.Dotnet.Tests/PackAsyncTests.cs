using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdks.Dotnet;
using Xunit;

namespace MintPlayer.Verz.Sdks.Dotnet.Tests;

public sealed class PackAsyncTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "verz-pack-tests",
        Guid.NewGuid().ToString("N"));

    public PackAsyncTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task PackAsync_produces_nupkg_with_PublicApiHash_and_FrameworkMajor_in_nuspec()
    {
        var name = "PackFixture_" + Guid.NewGuid().ToString("N")[..8];
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>{name}</PackageId>
                <Version>1.2.3</Version>
                <Authors>verz-tests</Authors>
                <Description>fixture for PackAsync test</Description>
                <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "Lib.cs"), "public class Foo { public void A() {} }");

        var project = new DiscoveredProject
        {
            PackageId = name,
            ProjectDir = dir,
            ProjectFile = Path.Combine(dir, $"{name}.csproj"),
            OwnerSdkId = "dotnet",
            FrameworkMajor = 10,
        };

        var sdk = new DotnetSdk(NullLogger<DotnetSdk>.Instance);
        var artifacts = await sdk.PackAsync(project, "Release", default);

        Assert.NotEmpty(artifacts);
        var nupkg = artifacts.Single(a => a.Kind == ArtifactKinds.Nuget);
        Assert.True(File.Exists(nupkg.Path), $"expected nupkg at {nupkg.Path}");

        // Crack open the nupkg and inspect the nuspec.
        using var archive = ZipFile.OpenRead(nupkg.Path);
        var nuspecEntry = archive.Entries.First(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = nuspecEntry.Open();
        var doc = XDocument.Load(stream);

        var metadata = doc.Root!.Elements()
            .First(e => string.Equals(e.Name.LocalName, "metadata", StringComparison.OrdinalIgnoreCase));

        var hashElem = metadata.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "PublicApiHash", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(hashElem);
        Assert.False(string.IsNullOrWhiteSpace(hashElem!.Value), "PublicApiHash should be non-empty");
        Assert.Equal(64, hashElem.Value.Length); // SHA-256 hex string

        var fmElem = metadata.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "FrameworkMajor", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(fmElem);
        Assert.Equal("10", fmElem!.Value);

        // Clean up the temp pack output dir.
        try { Directory.Delete(Path.GetDirectoryName(nupkg.Path)!, recursive: true); } catch { }
    }

    [Fact]
    public async Task PackAsync_returns_nuget_kind_artifact()
    {
        var name = "Kind_" + Guid.NewGuid().ToString("N")[..8];
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>{name}</PackageId>
                <Version>0.1.0</Version>
                <Authors>verz-tests</Authors>
                <Description>kind check fixture</Description>
                <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "Lib.cs"), "public class Bar {}");

        var project = new DiscoveredProject
        {
            PackageId = name,
            ProjectDir = dir,
            ProjectFile = Path.Combine(dir, $"{name}.csproj"),
            OwnerSdkId = "dotnet",
            FrameworkMajor = 10,
        };

        var sdk = new DotnetSdk(NullLogger<DotnetSdk>.Instance);
        var artifacts = await sdk.PackAsync(project, "Release", default);

        Assert.All(artifacts, a => Assert.True(
            a.Kind == ArtifactKinds.Nuget || a.Kind == ArtifactKinds.NugetSymbols,
            $"unexpected kind: {a.Kind}"));
        Assert.Contains(artifacts, a => a.Kind == ArtifactKinds.Nuget);

        try { Directory.Delete(Path.GetDirectoryName(artifacts[0].Path)!, recursive: true); } catch { }
    }
}
