using System.Text;
using static MintPlayer.TokenReplacer.Tests.Integration.MsBuildRunner;

namespace MintPlayer.TokenReplacer.Tests.Integration;

/// <summary>
/// Builds real fixture projects that import the source props/targets directly,
/// with the task assembly taken from the test output (no packing involved).
/// </summary>
public class DirectImportTests
{
    private static string FixtureCsproj(string body, string? extraProperties = null) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
        	<PropertyGroup>
        		<TargetFramework>net10.0</TargetFramework>
        		<TokenReplacerTasksAssembly>{Slashed(TasksAssemblyPath)}</TokenReplacerTasksAssembly>
        		<TokenReplacerOwnVersion>0.0.0-test</TokenReplacerOwnVersion>
        {extraProperties}
        	</PropertyGroup>
        	<Import Project="{Slashed(PropsPath)}" />
        {body}
        	<Import Project="{Slashed(TargetsPath)}" />
        </Project>
        """;

    private static string WriteFixture(string body, string templateContent, string? extraProperties = null)
    {
        var dir = CreateTempWorkspace();
        File.WriteAllText(Path.Combine(dir, "fixture.csproj"), FixtureCsproj(body, extraProperties));
        File.WriteAllText(Path.Combine(dir, "template.txt"), templateContent);
        return dir;
    }

    [Fact]
    public void Replaces_Tokens_And_Copies_Output_To_Bin()
    {
        var dir = WriteFixture("""
            	<ItemGroup>
            		<TokenReplaceValue Include="version" Value="1.2.3" />
            		<TokenReplaceValue Include="greeting" Value="Hello" />
            		<TokenReplaceFile Include="template.txt" OutputFile="generated/result.txt" IncludeAs="None" CopyToOutputDirectory="PreserveNewest" />
            	</ItemGroup>
            """,
            "greeting=$greeting$ version=$version$ property=$(NotAToken)");
        try
        {
            AssertBuildSucceeded(RunDotnet(dir, "build -tl:off"));

            var generated = File.ReadAllText(Path.Combine(dir, "generated", "result.txt"));
            Assert.Equal("greeting=Hello version=1.2.3 property=$(NotAToken)", generated);

            var copied = Path.Combine(dir, "bin", "Debug", "net10.0", "generated", "result.txt");
            Assert.True(File.Exists(copied), $"Expected the generated file to be copied to '{copied}'.");
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }

    [Fact]
    public void Default_Output_Goes_To_IntermediateOutputPath()
    {
        var dir = WriteFixture("""
            	<ItemGroup>
            		<TokenReplaceValue Include="version" Value="7.7.7" />
            		<TokenReplaceFile Include="template.txt" />
            	</ItemGroup>
            """,
            "v=$version$");
        try
        {
            AssertBuildSucceeded(RunDotnet(dir, "build -tl:off"));

            var generated = Path.Combine(dir, "obj", "Debug", "net10.0", "tokenreplacer", "template.txt");
            Assert.True(File.Exists(generated), $"Expected default output at '{generated}'.");
            Assert.Equal("v=7.7.7", File.ReadAllText(generated));
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }

    [Fact]
    public void Second_Build_Skips_The_Replacement_Target()
    {
        var dir = WriteFixture("""
            	<ItemGroup>
            		<TokenReplaceValue Include="version" Value="1.0.0" />
            		<TokenReplaceFile Include="template.txt" OutputFile="generated/result.txt" />
            	</ItemGroup>
            """,
            "v=$version$");
        try
        {
            AssertBuildSucceeded(RunDotnet(dir, "build -tl:off"));
            var second = RunDotnet(dir, "build --no-restore -tl:off -v:d");
            AssertBuildSucceeded(second);

            Assert.Contains("Skipping target \"MintPlayerReplaceTokens\"", second.Output);
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }

    [Fact]
    public void Clean_Removes_The_Generated_File()
    {
        // Default OutputFile lands under obj/; MSBuild's IncrementalClean only tracks FileWrites
        // below the output/intermediate directories, so outputs elsewhere are not Clean's concern.
        var dir = WriteFixture("""
            	<ItemGroup>
            		<TokenReplaceValue Include="version" Value="1.0.0" />
            		<TokenReplaceFile Include="template.txt" />
            	</ItemGroup>
            """,
            "v=$version$");
        try
        {
            AssertBuildSucceeded(RunDotnet(dir, "build -tl:off"));
            var generated = Path.Combine(dir, "obj", "Debug", "net10.0", "tokenreplacer", "template.txt");
            Assert.True(File.Exists(generated));

            AssertBuildSucceeded(RunDotnet(dir, "clean -tl:off"));
            Assert.False(File.Exists(generated), "Expected 'dotnet clean' to remove the generated file (FileWrites).");
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }

    [Fact]
    public void Missing_Token_Warns_By_Default()
    {
        var dir = WriteFixture("""
            	<ItemGroup>
            		<TokenReplaceValue Include="known" Value="x" />
            		<TokenReplaceFile Include="template.txt" OutputFile="generated/result.txt" />
            	</ItemGroup>
            """,
            "$known$ $unknown$");
        try
        {
            var result = RunDotnet(dir, "build -tl:off");
            AssertBuildSucceeded(result);

            Assert.Contains("MPTR002", result.Output);
            Assert.Equal("x $unknown$", File.ReadAllText(Path.Combine(dir, "generated", "result.txt")));
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }

    [Fact]
    public void Missing_Token_With_Error_Policy_Fails_The_Build()
    {
        var dir = WriteFixture("""
            	<ItemGroup>
            		<TokenReplaceFile Include="template.txt" OutputFile="generated/result.txt" />
            	</ItemGroup>
            """,
            "$unknown$",
            extraProperties: "\t\t<TokenReplacerMissingTokenPolicy>Error</TokenReplacerMissingTokenPolicy>");
        try
        {
            var result = RunDotnet(dir, "build -tl:off");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("MPTR002", result.Output);
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }

    [Fact]
    public void Resolves_Referenced_Package_Version_From_Assets_File()
    {
        var dir = WriteFixture("""
            	<ItemGroup>
            		<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            		<TokenReplacePackageVersion Include="Newtonsoft.Json" TokenName="njVersion" />
            		<TokenReplaceFile Include="template.txt" OutputFile="generated/result.txt" />
            	</ItemGroup>
            """,
            "newtonsoft=$njVersion$");
        try
        {
            AssertBuildSucceeded(RunDotnet(dir, "build -tl:off"));

            Assert.Equal("newtonsoft=13.0.3", File.ReadAllText(Path.Combine(dir, "generated", "result.txt")));
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }

    [Fact]
    public void Unknown_Package_Version_Token_Fails_The_Build()
    {
        var dir = WriteFixture("""
            	<ItemGroup>
            		<TokenReplacePackageVersion Include="Absent.Package.That.Is.Not.Referenced" />
            		<TokenReplaceFile Include="template.txt" OutputFile="generated/result.txt" />
            	</ItemGroup>
            """,
            "irrelevant");
        try
        {
            var result = RunDotnet(dir, "build -tl:off");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("MPTR001", result.Output);
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }

    [Fact]
    public void Preserves_Utf8_Bom()
    {
        var dir = WriteFixture("""
            	<ItemGroup>
            		<TokenReplaceValue Include="version" Value="1.0.0" />
            		<TokenReplaceFile Include="template.txt" OutputFile="generated/result.txt" />
            	</ItemGroup>
            """,
            templateContent: "ignored");
        // Rewrite the template with an explicit BOM
        File.WriteAllText(Path.Combine(dir, "template.txt"), "v=$version$", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            AssertBuildSucceeded(RunDotnet(dir, "build -tl:off"));

            var bytes = File.ReadAllBytes(Path.Combine(dir, "generated", "result.txt"));
            Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "Expected the BOM to be preserved.");
            Assert.Equal("v=1.0.0", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }
        finally
        {
            TryDeleteWorkspace(dir);
        }
    }
}
