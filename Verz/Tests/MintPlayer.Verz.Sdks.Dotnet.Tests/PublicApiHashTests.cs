using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdks.Dotnet;
using Xunit;

namespace MintPlayer.Verz.Sdks.Dotnet.Tests;

public sealed class PublicApiHashTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "verz-sdks-dotnet-tests",
        Guid.NewGuid().ToString("N"));

    public PublicApiHashTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* locked test outputs are tolerable */ }
    }

    [Fact]
    public void Same_source_built_twice_hashes_identically()
    {
        var project = CreateLib("public class Foo { public void A() {} }");
        Build(project);
        var hash1 = Hash(project);

        Build(project);
        var hash2 = Hash(project);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Adding_a_public_method_changes_the_hash()
    {
        var project = CreateLib("public class Foo { public void A() {} }");
        Build(project);
        var before = Hash(project);

        WriteLibSource(project, "public class Foo { public void A() {} public void B() {} }");
        Build(project);
        var after = Hash(project);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Adding_a_private_field_does_not_change_the_hash()
    {
        var project = CreateLib("public class Foo { public void A() {} }");
        Build(project);
        var before = Hash(project);

        WriteLibSource(project, "public class Foo { private int _x; public void A() { _x = 1; } }");
        Build(project);
        var after = Hash(project);

        Assert.Equal(before, after);
    }

    private DiscoveredProject CreateLib(string source)
    {
        var name = "FooLib_" + Guid.NewGuid().ToString("N")[..8];
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>{name}</PackageId>
                <Version>0.0.0-placeholder</Version>
                <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
              </PropertyGroup>
            </Project>
            """);

        WriteLibSource(dir, name, source);

        return new DiscoveredProject
        {
            PackageId = name,
            ProjectDir = dir,
            ProjectFile = Path.Combine(dir, $"{name}.csproj"),
            OwnerSdkId = "dotnet",
            FrameworkMajor = 10,
        };
    }

    private static void WriteLibSource(DiscoveredProject project, string source) =>
        WriteLibSource(project.ProjectDir, project.PackageId, source);

    private static void WriteLibSource(string dir, string name, string source) =>
        File.WriteAllText(Path.Combine(dir, "Lib.cs"), source);

    private static void Build(DiscoveredProject project)
    {
        var psi = new ProcessStartInfo("dotnet", $"build \"{project.ProjectFile}\" -c Debug --nologo -v quiet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"dotnet build failed:\nstdout:\n{stdout}\nstderr:\n{stderr}");
    }

    private static string Hash(DiscoveredProject project)
    {
        var sdk = new DotnetSdk(NullLogger<DotnetSdk>.Instance);
        return sdk.ComputePublicApiHashAsync(project, "Debug", default).GetAwaiter().GetResult();
    }
}
