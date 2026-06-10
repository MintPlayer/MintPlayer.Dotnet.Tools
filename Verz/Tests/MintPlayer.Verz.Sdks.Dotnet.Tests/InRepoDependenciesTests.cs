using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdks.Dotnet;
using Xunit;

namespace MintPlayer.Verz.Sdks.Dotnet.Tests;

public sealed class InRepoDependenciesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "verz-sdks-dotnet-deps",
        Guid.NewGuid().ToString("N"));

    public InRepoDependenciesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private DiscoveredProject CreateCsproj(string name, string csprojBody)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), csprojBody);
        return new DiscoveredProject
        {
            PackageId = name,
            ProjectDir = dir,
            ProjectFile = Path.Combine(dir, $"{name}.csproj"),
            OwnerSdkId = "dotnet",
            FrameworkMajor = 10,
        };
    }

    private static IReadOnlyList<string> EnumerateDeps(
        DotnetSdk sdk, DiscoveredProject project,
        IReadOnlyDictionary<string, DiscoveredProject> repoIndex) =>
            sdk.EnumerateInRepoDependenciesAsync(project, repoIndex, default)
               .GetAwaiter().GetResult();

    [Fact]
    public void Project_reference_to_sibling_resolves_to_package_id()
    {
        var core = CreateCsproj("CoreLib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>CoreLib</PackageId>
              </PropertyGroup>
            </Project>
            """);
        var consumer = CreateCsproj("ConsumerLib", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>ConsumerLib</PackageId>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\CoreLib\CoreLib.csproj" />
              </ItemGroup>
            </Project>
            """);

        var index = new Dictionary<string, DiscoveredProject>
        {
            ["CoreLib"] = core,
            ["ConsumerLib"] = consumer,
        };

        var sdk = new DotnetSdk(NullLogger<DotnetSdk>.Instance);
        var deps = EnumerateDeps(sdk, consumer, index);

        Assert.Equal(new[] { "CoreLib" }, deps);
    }

    [Fact]
    public void Package_reference_to_in_repo_package_yields_an_edge()
    {
        var core = CreateCsproj("CoreLib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>CoreLib</PackageId>
              </PropertyGroup>
            </Project>
            """);
        var consumer = CreateCsproj("ConsumerLib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>ConsumerLib</PackageId>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="CoreLib" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var index = new Dictionary<string, DiscoveredProject>
        {
            ["CoreLib"] = core,
            ["ConsumerLib"] = consumer,
        };

        var sdk = new DotnetSdk(NullLogger<DotnetSdk>.Instance);
        var deps = EnumerateDeps(sdk, consumer, index);

        Assert.Equal(new[] { "CoreLib" }, deps);
    }

    [Fact]
    public void External_package_reference_does_not_yield_an_edge()
    {
        var solo = CreateCsproj("SoloLib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>SoloLib</PackageId>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);

        var index = new Dictionary<string, DiscoveredProject> { ["SoloLib"] = solo };
        var sdk = new DotnetSdk(NullLogger<DotnetSdk>.Instance);

        var deps = EnumerateDeps(sdk, solo, index);

        Assert.Empty(deps);
    }

    [Fact]
    public void ProjectReference_and_PackageReference_to_same_target_dedup()
    {
        // Some repos belt-and-suspenders this. Should still emit one edge.
        var core = CreateCsproj("CoreLib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>CoreLib</PackageId>
              </PropertyGroup>
            </Project>
            """);
        var consumer = CreateCsproj("ConsumerLib", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>ConsumerLib</PackageId>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\CoreLib\CoreLib.csproj" />
                <PackageReference Include="CoreLib" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var index = new Dictionary<string, DiscoveredProject>
        {
            ["CoreLib"] = core,
            ["ConsumerLib"] = consumer,
        };

        var sdk = new DotnetSdk(NullLogger<DotnetSdk>.Instance);
        var deps = EnumerateDeps(sdk, consumer, index);

        Assert.Equal(new[] { "CoreLib" }, deps);
    }
}
