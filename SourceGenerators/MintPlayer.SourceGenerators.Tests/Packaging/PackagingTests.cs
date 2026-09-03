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
    [InlineData("analyzers/dotnet/roslyn4.0/cs/MintPlayer.SourceGenerators.dll")]
    [InlineData("analyzers/dotnet/roslyn4.9/cs/MintPlayer.SourceGenerators.dll")]
    public void TheGeneratorShipsInEveryRoslynAnalyzerFolder(string expectedPath)
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
    [InlineData("analyzers/dotnet/roslyn4.0/cs/MintPlayer.SourceGenerators.Tools.dll")]
    [InlineData("analyzers/dotnet/roslyn4.9/cs/MintPlayer.SourceGenerators.Tools.dll")]
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

        entries.Should().Contain("analyzers/dotnet/cs/MintPlayer.Assertions.SourceGenerator.dll");
        entries.Should().Contain("analyzers/dotnet/cs/MintPlayer.SourceGenerators.Tools.dll");
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
    /// Comparing the two payloads rather than asserting a fixed list, so a future addition to
    /// either configuration has to be made to both.
    ///
    /// Both sides are packed in ISOLATION. Taking the Release side from the shared feed compares a
    /// pack made after a full solution build against one made after a single project build, which
    /// is a difference in leftover <c>bin</c> contents rather than in packaging logic.
    /// </remarks>
    [Theory]
    [InlineData(GeneratorPackage, "SourceGenerators/SourceGenerators/MintPlayer.SourceGenerators/MintPlayer.SourceGenerators.csproj")]
    [InlineData(AssertionsPackage, "Assertions/MintPlayer.Assertions/MintPlayer.Assertions.csproj")]
    public void TheAnalyzerPayloadIsTheSameInDebugAndRelease(string packageId, string projectRelativePath)
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
            		<TargetFramework>net10.0</TargetFramework>
            		<Nullable>enable</Nullable>
            		<LangVersion>14</LangVersion>
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
            		<TargetFramework>net10.0</TargetFramework>
            		<Nullable>enable</Nullable>
            		<LangVersion>14</LangVersion>
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

    #endregion
}
