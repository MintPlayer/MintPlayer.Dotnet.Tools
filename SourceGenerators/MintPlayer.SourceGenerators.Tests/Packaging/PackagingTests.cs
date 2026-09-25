using System.IO.Compression;
using System.Reflection;

namespace MintPlayer.SourceGenerators.Tests.Packaging;

/// <summary>
/// What the packed generator packages actually contain, and whether a real consumer build picks
/// them up.
/// </summary>
/// <remarks>
/// R5.2 of docs/PRD-TestCoverage-Phase2.md. This is the one failure mode the rest of the suite
/// cannot see: every other generator test loads the assembly directly and never goes near NuGet,
/// so a package that puts the DLL in the wrong folder, drops a runtime dependency, or fails to
/// wire up its props/targets ships green.
///
/// Marked E2E because each test in the class shares one pack of five real projects — around a
/// minute — and the consumer test runs a full restore and build on top.
/// </remarks>
[Trait("Category", "E2E")]
public class PackagingTests(PackedFeed feed) : IClassFixture<PackedFeed>
{
    private const string GeneratorPackage = "MintPlayer.SourceGenerators";
    private const string AssertionsPackage = "MintPlayer.Assertions";

    #region Layout

    /// <summary>
    /// Roslyn only loads analyzers from <c>analyzers/dotnet/&lt;roslynN.N&gt;/cs</c>. A DLL a
    /// folder away is shipped, restored, and never run — the generator simply produces nothing,
    /// with no error anywhere.
    /// </summary>
    [Theory]
    [InlineData("analyzers/dotnet/roslyn5.9/cs/MintPlayer.SourceGenerators.dll")]
    public void TheGeneratorShipsInTheRoslynAnalyzerFolder(string expectedPath)
        => feed.EntriesOf(GeneratorPackage).Should().Contain(expectedPath);

    /// <summary>
    /// The generator's own runtime dependency has to sit beside it in each analyzer folder.
    /// </summary>
    /// <remarks>
    /// Without MintPlayer.SourceGenerators.Tools.dll beside it the generator cannot resolve its own
    /// base class, and the consumer sees CS8032 — "An instance of analyzer cannot be created" — or,
    /// more often, nothing at all.
    /// </remarks>
    [Theory]
    [InlineData("analyzers/dotnet/roslyn5.9/cs/MintPlayer.SourceGenerators.Tools.dll")]
    public void TheGeneratorsRuntimeDependencyShipsBesideIt(string expectedPath)
        => feed.EntriesOf(GeneratorPackage).Should().Contain(expectedPath);

    [Fact]
    public void TheGeneratorPackageShipsItsBuildProps()
    {
        var entries = feed.EntriesOf(GeneratorPackage);

        entries.Should().Contain("build/MintPlayer.SourceGenerators.props");
        entries.Should().Contain("build/MintPlayer.SourceGenerators.targets");
    }

    /// <summary>
    /// A generator package must not carry compiled output in <c>lib/</c>: it is a build-time
    /// component, and a lib/ assembly would land in every consumer's output directory.
    /// </summary>
    [Fact]
    public void TheGeneratorPackageShipsNoLibAssembly()
        => feed.EntriesOf(GeneratorPackage)
            .Should().NotContain(e => e.StartsWith("lib/", StringComparison.Ordinal));

    /// <summary>
    /// Roslyn is supplied by the compiler host. Shipping our own copy inside the analyzer folder
    /// gets it loaded alongside the host's, and the resulting type identity mismatch breaks the
    /// generator in ways that are very hard to diagnose from the consumer's side.
    /// </summary>
    [Fact]
    public void TheGeneratorPackageDoesNotShipRoslyn()
    {
        var entries = feed.EntriesOf(GeneratorPackage);

        entries.Should().NotContain(e => e.Contains("Microsoft.CodeAnalysis", StringComparison.Ordinal));
        entries.Should().NotContain(e => e.Contains("Microsoft.CodeAnalysis.CSharp", StringComparison.Ordinal));
    }

    /// <summary>
    /// MintPlayer.Assertions hand-rolls its analyzer packaging with <c>None Include</c> items
    /// rather than using the shared sourcegenerator.targets, so it can drift from the other
    /// packages independently — and it is the one whose analyzers reach every consumer of the
    /// assertion library.
    /// </summary>
    [Fact]
    public void TheAssertionsPackageShipsItsAnalyzers()
    {
        var entries = feed.EntriesOf(AssertionsPackage);

        entries.Should().Contain("analyzers/dotnet/roslyn5.9/cs/MintPlayer.Assertions.SourceGenerator.dll");
        entries.Should().Contain("analyzers/dotnet/roslyn5.9/cs/MintPlayer.SourceGenerators.Tools.dll");

        // The generator's own models are [GenerateEquality] types. Beside the generator rather
        // than in analyzers/dotnet/cs, so the unversioned folder below stays empty.
        entries.Should().Contain("analyzers/dotnet/roslyn5.9/cs/MintPlayer.ValueComparerGenerator.Attributes.dll");

        // The unversioned folder loads under EVERY Roslyn. With analyzers built against 5.9 that
        // would crash an older host with CS8032 instead of letting it skip them.
        entries.Where(e => e.StartsWith("analyzers/dotnet/cs/", StringComparison.Ordinal)).Should().BeEmpty();
    }

    /// <summary>
    /// The analyzer payload must not depend on the build configuration.
    /// </summary>
    /// <remarks>
    /// It used to. The Tools DLLs were packed under
    /// <c>Condition="'$(Configuration)' == 'Release'"</c>, so a Debug pack produced a package whose
    /// generator could not resolve its own base class — restores cleanly, reports nothing, never
    /// runs. CI packs Release, so nothing broken shipped; a developer's plain <c>dotnet pack</c>
    /// defaults to Debug and did produce one.
    ///
    /// Both sides are packed in ISOLATION, because the shared feed's Release pack is made after
    /// the test run's own <c>dotnet build</c> has populated every <c>bin/Release</c> in the
    /// solution — comparing that against a single-project Debug pack measures leftover build state
    /// rather than packaging logic.
    ///
    /// This one names the load-bearing entries explicitly, so a failure says which assembly is
    /// missing and why that matters. <see cref="TheAnalyzerPayloadIsIdenticalInDebugAndRelease"/>
    /// covers drift in the rest of the payload.
    /// </remarks>
    [Theory]
    [InlineData(GeneratorPackage, "SourceGenerators/SourceGenerators/MintPlayer.SourceGenerators/MintPlayer.SourceGenerators.csproj",
        "analyzers/dotnet/roslyn5.9/cs/MintPlayer.SourceGenerators.dll",
        "analyzers/dotnet/roslyn5.9/cs/MintPlayer.SourceGenerators.Tools.dll",
        "analyzers/dotnet/roslyn5.9/cs/MintPlayer.SourceGenerators.dll",
        // [GenerateEquality] decorates this generator's own model types, so Roslyn cannot load
        // the generator without it. Dropping this entry does not degrade the package, it disables
        // it — which an earlier revision of this file did.
        "analyzers/dotnet/cs/MintPlayer.ValueComparerGenerator.Attributes.dll")]
    [InlineData(AssertionsPackage, "Assertions/MintPlayer.Assertions/MintPlayer.Assertions.csproj",
        "analyzers/dotnet/roslyn5.9/cs/MintPlayer.Assertions.SourceGenerator.dll",
        "analyzers/dotnet/roslyn5.9/cs/MintPlayer.SourceGenerators.Tools.dll",
        // Same reason as for MintPlayer.SourceGenerators above: its models use [GenerateEquality].
        "analyzers/dotnet/roslyn5.9/cs/MintPlayer.ValueComparerGenerator.Attributes.dll")]
    public void TheAnalyzerPayloadCarriesItsEssentialsInBothConfigurations(
        string packageId, string projectRelativePath, params string[] required)
    {
        foreach (var configuration in (string[])["Release", "Debug"])
        {
            var entries = feed.IsolatedAnalyzerEntriesOf(packageId, projectRelativePath, configuration);

            entries.Should().NotBeEmpty($"the {configuration} pack must carry an analyzer payload at all");

            foreach (var path in required)
            {
                entries.Should().Contain(path,
                    $"a {configuration} pack without '{path}' installs cleanly and does nothing — " +
                    $"the generator cannot load, and the consumer gets no error to search for");
            }
        }
    }

    /// <summary>
    /// The two configurations must produce byte-identical analyzer payloads.
    /// </summary>
    /// <remarks>
    /// Set equality, not a subset check. It catches the case the named-essentials test cannot:
    /// something being ADDED to one configuration and not the other, which is how a package ends
    /// up carrying an assembly nobody decided to ship.
    ///
    /// This is only a meaningful assertion because the payload is now deterministic — every entry
    /// is named explicitly and sourced from the producing project's own output directory. While
    /// the items were <c>*.dll</c> globs over sibling bin folders they picked up whatever an
    /// earlier build had left behind, and this test failed on residue rather than on defects.
    /// </remarks>
    [Theory]
    [InlineData(GeneratorPackage, "SourceGenerators/SourceGenerators/MintPlayer.SourceGenerators/MintPlayer.SourceGenerators.csproj")]
    [InlineData(AssertionsPackage, "Assertions/MintPlayer.Assertions/MintPlayer.Assertions.csproj")]
    public void TheAnalyzerPayloadIsIdenticalInDebugAndRelease(string packageId, string projectRelativePath)
    {
        var release = feed.IsolatedAnalyzerEntriesOf(packageId, projectRelativePath, "Release");
        var debug = feed.IsolatedAnalyzerEntriesOf(packageId, projectRelativePath, "Debug");

        release.Should().NotBeEmpty("the Release pack must carry an analyzer payload at all");
        debug.Should().BeEquivalentTo(release);
    }

    [Fact]
    public void TheAssertionsPackageStillShipsItsLibrary()
        => feed.EntriesOf(AssertionsPackage)
            .Should().Contain(e => e.StartsWith("lib/", StringComparison.Ordinal)
                                && e.EndsWith("MintPlayer.Assertions.dll", StringComparison.Ordinal));

    #endregion

    #region End-to-end consumption

    /// <summary>
    /// The whole point: restore the packed generator from a feed and build a project whose code
    /// cannot compile unless the generator ran.
    /// </summary>
    /// <remarks>
    /// The consumer calls <c>new Service(provider)</c>, and the only declaration of that
    /// constructor is the one InjectSourceGenerator emits. So a successful build IS the assertion
    /// — if the analyzer folder is wrong, the runtime dependency is missing, or the props/targets
    /// fail to wire the generator in, this fails with CS1729 rather than passing quietly.
    /// </remarks>
    [Fact]
    public void AConsumerRestoringThePackageGetsGeneratedCode()
    {
        var consumer = Path.Combine(feed.Root, "consumer");
        Directory.CreateDirectory(consumer);

        File.WriteAllText(Path.Combine(consumer, "nuget.config"), feed.NuGetConfigXml);

        File.WriteAllText(Path.Combine(consumer, "consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
            	<PropertyGroup>
            		<TargetFramework>net11.0</TargetFramework>
            		<Nullable>enable</Nullable>
            		<LangVersion>15</LangVersion>
            		<!-- So the assertion below can look at what was actually emitted. -->
            		<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
            	</PropertyGroup>
            	<ItemGroup>
            		<PackageReference Include="{GeneratorPackage}" Version="{PackedFeed.Version}" />
            	</ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(consumer, "Service.cs"), """
            using System;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Consumer;

            public partial class Service
            {
                [Inject] private readonly IServiceProvider _provider;

                public IServiceProvider Provider => _provider;
            }

            public static class Entry
            {
                // Only the generated constructor declares this signature. No generator, no build.
                public static Service Build(IServiceProvider provider) => new Service(provider);
            }
            """);

        var build = PackedFeed.Run(consumer, "build -c Release -tl:off");

        build.ExitCode.Should().Be(0,
            $"a consumer of the packed generator should compile.{Environment.NewLine}" +
            $"{build.Output}{Environment.NewLine}Pack log:{Environment.NewLine}{feed.PackLog}");
    }

    /// <summary>
    /// The generated file lands on disk under the expected generator-named folder, which is what
    /// consumers actually look at when something goes wrong.
    /// </summary>
    [Fact]
    public void TheGeneratedFileIsWrittenWhereConsumersExpectIt()
    {
        var consumer = Path.Combine(feed.Root, "consumeremit");
        Directory.CreateDirectory(consumer);

        File.WriteAllText(Path.Combine(consumer, "nuget.config"), feed.NuGetConfigXml);

        // CompilerGeneratedFilesOutputPath is deliberately left at its default, under obj/. Pointing
        // it at a folder inside the project directory puts the emitted files in the SDK's default
        // **/*.cs glob, so they are compiled a second time as ordinary source and every generated
        // member collides with itself (CS0102).
        File.WriteAllText(Path.Combine(consumer, "consumeremit.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
            	<PropertyGroup>
            		<TargetFramework>net11.0</TargetFramework>
            		<Nullable>enable</Nullable>
            		<LangVersion>15</LangVersion>
            		<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
            	</PropertyGroup>
            	<ItemGroup>
            		<PackageReference Include="{GeneratorPackage}" Version="{PackedFeed.Version}" />
            	</ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(consumer, "Service.cs"), """
            using System;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Consumer;

            public partial class Service
            {
                [Inject] private readonly IServiceProvider _provider;
            }
            """);

        var build = PackedFeed.Run(consumer, "build -c Release -tl:off");
        build.ExitCode.Should().Be(0, build.Output);

        var generatedRoot = Path.Combine(consumer, "obj");
        var emitted = Directory.GetFiles(generatedRoot, "*.g.cs", SearchOption.AllDirectories);

        emitted.Should().NotBeEmpty(
            $"EmitCompilerGeneratedFiles should have written the generator's output under obj/." +
            $"{Environment.NewLine}{build.Output}");

        // The folder is named after the generator that produced the file, which is what a consumer
        // navigates to when they want to see what was generated.
        emitted.Should().Contain(f => f.Contains("MintPlayer.SourceGenerators", StringComparison.Ordinal));

        var allText = string.Join(Environment.NewLine, emitted.Select(File.ReadAllText));
        allText.Should().Contain("Service", "the emitted constructor belongs to the decorated class");
    }

    /// <summary>
    /// A generator built on this package, referenced by an ordinary <c>ProjectReference</c> (a test project running it
    /// in-process does this), must build. With <c>IncludeRuntimeDependency="true"</c> on the attributes dll the
    /// package's targets add to <c>TargetPathWithTargetPlatformMoniker</c>, the referencing project gets two runtime
    /// assemblies from one project, and <c>GenerateDepsFile</c> fails with MSB4018 "An item with the same key has
    /// already been added" (#187).
    /// </summary>
    /// <remarks>
    /// A fresh directory, so no <c>deps.json</c> from an earlier build can hide the failure; a leftover one did in the
    /// spike.
    /// </remarks>
    [Fact]
    public void APlainProjectReferenceToAGeneratorBuiltOnThePackageBuilds()
    {
        var root = Path.Combine(feed.Root, "plainreference");
        var generator = Path.Combine(root, "gen");
        var host = Path.Combine(root, "host");
        Directory.CreateDirectory(generator);
        Directory.CreateDirectory(host);

        File.WriteAllText(Path.Combine(root, "nuget.config"), feed.NuGetConfigXml);

        File.WriteAllText(Path.Combine(generator, "gen.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
            	<PropertyGroup>
            		<TargetFramework>netstandard2.0</TargetFramework>
            		<LangVersion>latest</LangVersion>
            		<IsRoslynComponent>true</IsRoslynComponent>
            		<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
            	</PropertyGroup>
            	<ItemGroup>
            		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" PrivateAssets="all" />
            		<PackageReference Include="{GeneratorPackage}" Version="{PackedFeed.Version}" PrivateAssets="all" />
            		<PackageReference Include="MintPlayer.SourceGenerators.Attributes" Version="{PackedFeed.Version}" PrivateAssets="all" GeneratePathProperty="true" />
            	</ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(generator, "Marker.cs"), """
            namespace Gen;

            public static class Marker
            {
                public const string Name = "gen";
            }
            """);

        File.WriteAllText(Path.Combine(host, "host.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
            	<PropertyGroup>
            		<OutputType>Exe</OutputType>
            		<TargetFramework>net11.0</TargetFramework>
            	</PropertyGroup>
            	<ItemGroup>
            		<ProjectReference Include="../gen/gen.csproj" />
            	</ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(host, "Program.cs"), "System.Console.WriteLine(Gen.Marker.Name);");

        var build = PackedFeed.Run(host, "build -c Release -tl:off");

        build.Output.Should().NotContain("MSB4018", "GenerateDepsFile must not see two runtime assemblies from one project");
        build.ExitCode.Should().Be(0, build.Output);
    }

    #endregion

    #region Versions

    /// <summary>
    /// The feed's packs must not overwrite the repository's own build output. They used to pack in place with
    /// <c>-p:Version=99.9.9-packtest</c>, rebuilding every project's <c>bin/Release</c> with that version. CI's
    /// <c>dotnet pack --no-build</c> after the test run then shipped those dlls, so every release from 10.20.2 to
    /// 12.0.1 carried AssemblyVersion 99.9.9.0 (#187).
    /// </summary>
    [Fact]
    public void TheFeedLeavesTheRepositoryBuildOutputAlone()
    {
        var restamped = PackedFeed.ProjectsToPack
            .SelectMany(project =>
            {
                var bin = Path.Combine(PackedFeed.RepoRoot, Path.GetDirectoryName(project)!, "bin");
                var dll = Path.GetFileNameWithoutExtension(project) + ".dll";
                return Directory.Exists(bin) ? Directory.GetFiles(bin, dll, SearchOption.AllDirectories) : [];
            })
            .Where(dll => AssemblyName.GetAssemblyName(dll).Version == PackedAssemblyVersion)
            .ToList();

        restamped.Should().BeEmpty(
            $"packing the feed must not rebuild the repository's output with the test version:{Environment.NewLine}"
            + string.Join(Environment.NewLine, restamped));
    }

    /// <summary>
    /// The version passed to the pack reaches the assemblies inside the package, so the release's own version check
    /// (<c>eng/Assert-PackageVersions.ps1</c>) compares something that moves with the package version.
    /// </summary>
    [Theory]
    [InlineData("MintPlayer.SourceGenerators.Attributes", "lib/netstandard2.0/MintPlayer.SourceGenerators.Attributes.dll")]
    [InlineData(GeneratorPackage, "analyzers/dotnet/roslyn5.9/cs/MintPlayer.SourceGenerators.dll")]
    public void ThePackedAssembliesCarryThePackageVersion(string packageId, string entryPath)
    {
        var nupkg = Path.Combine(feed.Feed, $"{packageId}.{PackedFeed.Version}.nupkg");
        var extracted = Path.Combine(feed.Root, "versions", packageId, Path.GetFileName(entryPath));
        Directory.CreateDirectory(Path.GetDirectoryName(extracted)!);

        using (var archive = ZipFile.OpenRead(nupkg))
        {
            var entry = archive.GetEntry(entryPath);
            entry.Should().NotBeNull($"{packageId} must contain {entryPath}");
            entry!.ExtractToFile(extracted, overwrite: true);
        }

        AssemblyName.GetAssemblyName(extracted).Version.Should().Be(PackedAssemblyVersion);
    }

    private static readonly Version PackedAssemblyVersion = new(PackedFeed.Version.Split('-')[0] + ".0");

    #endregion
}
