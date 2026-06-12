using static MintPlayer.TokenReplacer.Tests.Integration.MsBuildRunner;

namespace MintPlayer.TokenReplacer.Tests.Integration;

/// <summary>
/// Full TwoSky-style end-to-end scenario:
/// 1. pack MintPlayer.TokenReplacer.Targets into a temp local feed,
/// 2. pack a sample web-component package that re-ships it (own .targets deriving its own version),
/// 3. build a consumer that references only the sample package,
/// and assert the consumer's output contains the sample package's exact version — proving the
/// buildTransitive flow and the package-folder version derivation against real NuGet layout.
/// </summary>
public class PackAndConsumeTests
{
    private const string TokenReplacerVersion = "9.9.9";
    private const string SampleVersion = "2.5.7";
    private const string SamplePackageId = "MintPlayer.TokenReplacer.SampleWebComponent";

    [Fact]
    [Trait("Category", "E2E")]
    public void Consumer_Gets_Asset_Stamped_With_The_Sample_Packages_Own_Version()
    {
        var root = CreateTempWorkspace();
        try
        {
            var feed = Path.Combine(root, "feed");
            var packagesDir = Path.Combine(root, "gpf"); // isolated global packages folder
            Directory.CreateDirectory(feed);
            var env = new Dictionary<string, string> { ["NUGET_PACKAGES"] = packagesDir };

            var nugetConfig = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                	<packageSources>
                		<clear />
                		<add key="local" value="{Slashed(feed)}" />
                		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                	</packageSources>
                </configuration>
                """;

            // 1. Pack the real Targets project into the local feed
            var packTargets = RunDotnet(TokenReplacerProjectDir,
                $"pack MintPlayer.TokenReplacer.Targets.csproj -o \"{feed}\" -p:Version={TokenReplacerVersion} -tl:off");
            AssertBuildSucceeded(packTargets);

            // 2. The sample package: content template + thin buildTransitive targets per the documented recipe
            var sampleDir = Path.Combine(root, "sample");
            Directory.CreateDirectory(Path.Combine(sampleDir, "buildTransitive"));
            Directory.CreateDirectory(Path.Combine(sampleDir, "content"));
            File.WriteAllText(Path.Combine(sampleDir, "nuget.config"), nugetConfig);
            File.WriteAllText(Path.Combine(sampleDir, "content", "web-loader.template.js"),
                """export const loaderUrl = "https://cdn.example.com/sample-web-component@$version$/loader.js";""");
            File.WriteAllText(Path.Combine(sampleDir, "buildTransitive", $"{SamplePackageId}.targets"), """
                <Project>
                	<PropertyGroup>
                		<!-- <packagesRoot>/<id>/<version>/buildTransitive/ → "<version>" -->
                		<_SampleWebComponentVersion>$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName($([System.IO.Path]::GetDirectoryName($(MSBuildThisFileDirectory)))))))</_SampleWebComponentVersion>
                	</PropertyGroup>
                	<ItemGroup>
                		<TokenReplaceValue Include="version" Value="$(_SampleWebComponentVersion)" />
                		<TokenReplaceFile Include="$(MSBuildThisFileDirectory)..\content\web-loader.template.js"
                		                  OutputFile="$(IntermediateOutputPath)tokenreplacer\web-loader.js"
                		                  IncludeAs="None" CopyToOutputDirectory="PreserveNewest" Link="web-loader.js" />
                	</ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(sampleDir, "sample.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                	<PropertyGroup>
                		<TargetFramework>netstandard2.0</TargetFramework>
                		<PackageId>{SamplePackageId}</PackageId>
                		<IncludeBuildOutput>false</IncludeBuildOutput>
                		<NoWarn>$(NoWarn);NU5128</NoWarn>
                	</PropertyGroup>
                	<ItemGroup>
                		<!-- PrivateAssets=none: the TokenReplacer dependency MUST flow to this package's consumers -->
                		<PackageReference Include="MintPlayer.TokenReplacer.Targets" Version="{TokenReplacerVersion}" PrivateAssets="none" />
                	</ItemGroup>
                	<ItemGroup>
                		<None Include="buildTransitive\{SamplePackageId}.targets" Pack="true" PackagePath="buildTransitive/" />
                		<None Include="content\web-loader.template.js" Pack="true" PackagePath="content/" />
                	</ItemGroup>
                </Project>
                """);
            var packSample = RunDotnet(sampleDir, $"pack -o \"{feed}\" -p:Version={SampleVersion} -tl:off", env);
            AssertBuildSucceeded(packSample);

            // 3. The consumer references ONLY the sample package
            var consumerDir = Path.Combine(root, "consumer");
            Directory.CreateDirectory(consumerDir);
            File.WriteAllText(Path.Combine(consumerDir, "nuget.config"), nugetConfig);
            File.WriteAllText(Path.Combine(consumerDir, "consumer.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                	<PropertyGroup>
                		<TargetFramework>net10.0</TargetFramework>
                	</PropertyGroup>
                	<ItemGroup>
                		<PackageReference Include="{SamplePackageId}" Version="{SampleVersion}" />
                	</ItemGroup>
                </Project>
                """);
            var build = RunDotnet(consumerDir, "build -tl:off", env);
            AssertBuildSucceeded(build);

            var copied = Path.Combine(consumerDir, "bin", "Debug", "net10.0", "web-loader.js");
            Assert.True(File.Exists(copied), $"Expected the stamped asset at '{copied}'. Build output:{Environment.NewLine}{build.Output}");
            Assert.Equal(
                $"""export const loaderUrl = "https://cdn.example.com/sample-web-component@{SampleVersion}/loader.js";""",
                File.ReadAllText(copied));
        }
        finally
        {
            TryDeleteWorkspace(root);
        }
    }
}
