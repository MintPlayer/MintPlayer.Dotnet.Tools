using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// <see cref="CliCommandSourceGenerator"/> across type kinds and the shapes it cannot generate for (PRD
/// TypeKindRobustness, M5: C4, K4, K5, W3, W12, W13, P12–P16). Every unsupported shape reports a MINTCLI
/// diagnostic and generates nothing for that command, instead of emitting code that does not compile.
/// </summary>
public class CliCommandTypeKindTests
{
    private static GeneratorRun Run(params string[] sources)
        => GeneratorHarness.Run("CliCommandSourceGenerator", sources, generatorAssemblyName: "MintPlayer.CliGenerator");

    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using MintPlayer.CliGenerator.Attributes;
        using MintPlayer.SourceGenerators.Attributes;

        """;

    private const string Execute = "public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);";

    private static string[] DiagnosticIds(GeneratorRun run)
        => run.Diagnostics.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    /// <summary>The compiler's errors: <c>run.Errors</c> without the generator's own MINTCLI diagnostics.</summary>
    private static Diagnostic[] CompilerErrors(GeneratorRun run)
        => run.Errors.Where(e => !e.Id.StartsWith("MINTCLI", StringComparison.Ordinal)).ToArray();

    public static TheoryData<string, string> CommandKindsAndContainers()
    {
        var data = new TheoryData<string, string>();
        foreach (var kind in new[] { "class", "record class", "record" })
            foreach (var container in new[] { "", "class", "record class", "record", "struct", "record struct", "interface" })
                data.Add(kind, container);
        return data;
    }

    /// <summary>
    /// K4 and K5. A root command of each class kind, with a nested subcommand of the same kind, at top level and
    /// nested in each of the five kinds and in an interface. The headers use the user's keyword (a reference
    /// record is always re-declared as <c>record</c>), and a call through the generated extensions compiles.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommandKindsAndContainers))]
    public void EveryClassKind_GeneratesInEveryContainer(string kind, string container)
    {
        var root = $$"""
            [CliRootCommand(Name = "demo")]
            public partial {{kind}} Tool : ICliCommand
            {
                [CliOption("--verbose"), NoInterfaceMember] public bool Verbose { get; set; }
                {{Execute}}

                [CliCommand("sub")]
                public partial {{kind}} Sub : ICliCommand
                {
                    {{Execute}}
                }
            }
            """;
        var body = container == "" ? root : $$"""
            public partial {{container}} Outer
            {
            {{root}}
            }
            """;

        var run = Run(Usings + $$"""
            namespace Demo
            {
            {{body}}

                internal static class Use
                {
                    internal static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                        => global::TestRoot.ToolCliExtensions.AddTool(services);
                }
            }
            """);

        run.Diagnostics.Should().BeEmpty();
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
        var keyword = kind == "class" ? "class" : "record";
        run.AllSources.Should().Contain($"partial {keyword} Tool");
        run.AllSources.Should().Contain($"partial {keyword} Sub");
        if (container != "")
        {
            var containerKeyword = container == "record class" ? "record" : container;
            run.AllSources.Should().Contain($"partial {containerKeyword} Outer");
        }
        run.AllSources.Should().NotContain("public partial", "the header never repeats the accessibility");
    }

    /// <summary>C4: <c>[CliOption(null)]</c> passes a null params array. It crashed the generator (CS8785).</summary>
    [Fact]
    public void C4_OptionWithNullAliases_FallsBackToThePropertyName()
    {
        var run = Run(Usings + $$"""
            namespace Demo;

            [CliRootCommand(Name = "demo")]
            public partial class RootCommand : ICliCommand
            {
                [CliOption(null), NoInterfaceMember] public string? Name { get; set; }
                {{Execute}}
            }
            """);

        run.Diagnostics.Should().BeEmpty();
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("\"--name\"");
    }

    /// <summary>W3: a <c>file</c> command re-declared in the generated file is a different type (it gave CS1061).</summary>
    [Fact]
    public void W3_FileLocalCommand_ReportsMintCli006()
    {
        var run = Run(Usings + $$"""
            namespace Demo;

            [CliRootCommand(Name = "demo")]
            file partial class RootCommand : ICliCommand
            {
                {{Execute}}
            }
            """);

        DiagnosticIds(run).Should().Equal("MINTCLI006");
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().BeEmpty();
    }

    /// <summary>W12: a command nested in a non-partial type gave CS0260 on the user's type.</summary>
    [Fact]
    public void W12_CommandInNonPartialType_ReportsMintCli005()
    {
        var run = Run(Usings + $$"""
            namespace Demo;

            public class Outer
            {
                [CliRootCommand(Name = "demo")]
                public partial class RootCommand : ICliCommand
                {
                    {{Execute}}
                }
            }
            """);

        DiagnosticIds(run).Should().Equal("MINTCLI005");
        run.Diagnostics[0].GetMessage().Should().Contain("'Outer'");
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
    }

    /// <summary>W13: two roots named <c>Tool</c> in different namespaces both declared <c>ToolCliExtensions</c> (CS0101).</summary>
    [Fact]
    public void W13_TwoRootsWithTheSameName_ReportMintCli009AndGenerateNoExtensions()
    {
        var run = Run(
            Usings + $$"""
                namespace A;

                [CliRootCommand(Name = "a")]
                public partial class Tool : ICliCommand
                {
                    {{Execute}}
                }
                """,
            Usings + $$"""
                namespace B;

                [CliRootCommand(Name = "b")]
                public partial class Tool : ICliCommand
                {
                    {{Execute}}
                }
                """);

        DiagnosticIds(run).Should().Equal("MINTCLI009", "MINTCLI009");
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("ToolCliExtensions");
        run.AllSources.Should().Contain("BuildCliRootCommand", "the command partials themselves are still generated");
    }

    /// <summary>P13: a <c>private protected</c> subcommand was re-declared <c>protected internal</c> (CS0262).</summary>
    [Fact]
    public void P13_PrivateProtectedSubcommand_KeepsItsAccessibility()
    {
        var run = Run(Usings + $$"""
            namespace Demo;

            [CliRootCommand(Name = "demo")]
            public partial class RootCommand : ICliCommand
            {
                {{Execute}}

                [CliCommand("sub")]
                private protected partial class Sub : ICliCommand
                {
                    {{Execute}}
                }
            }
            """);

        run.Diagnostics.Should().BeEmpty();
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("partial class Sub");
        run.AllSources.Should().NotContain("protected internal");
    }

    /// <summary>P14: a private nested root made the public extensions class call an inaccessible type (CS0122).</summary>
    [Fact]
    public void P14_PrivateNestedRoot_ReportsMintCli007()
    {
        var run = Run(Usings + $$"""
            namespace Demo;

            public partial class Outer
            {
                [CliRootCommand(Name = "demo")]
                private partial class RootCommand : ICliCommand
                {
                    {{Execute}}
                }
            }
            """);

        DiagnosticIds(run).Should().Equal("MINTCLI007");
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
    }

    /// <summary>
    /// The same family as P14 for <c>[CliParentCommand]</c>: the parent registers and builds the subcommand, so a
    /// subcommand private to another type cannot be called from it.
    /// </summary>
    [Fact]
    public void P14_SubcommandNotAccessibleFromItsParent_ReportsMintCli008()
    {
        var run = Run(Usings + $$"""
            namespace Demo;

            [CliRootCommand(Name = "demo")]
            public partial class RootCommand : ICliCommand
            {
                {{Execute}}
            }

            public partial class Elsewhere
            {
                [CliCommand("sub")]
                [CliParentCommand(typeof(RootCommand))]
                private partial class Sub : ICliCommand
                {
                    {{Execute}}
                }
            }
            """);

        DiagnosticIds(run).Should().Equal("MINTCLI008");
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
    }

    /// <summary>P15: a non-partial, generic or static command generated nothing and said nothing.</summary>
    [Theory]
    [InlineData("public class RootCommand : ICliCommand", "MINTCLI002")]
    [InlineData("public partial class RootCommand<T> : ICliCommand", "MINTCLI003")]
    [InlineData("public static partial class RootCommand", "MINTCLI004")]
    public void P15_UnsupportedCommandShape_ReportsADiagnostic(string declaration, string expectedId)
    {
        var members = declaration.Contains("static") ? "" : Execute;
        var run = Run(Usings + $$"""
            namespace Demo;

            [CliRootCommand(Name = "demo")]
            {{declaration}}
            {
                {{members}}
            }
            """);

        DiagnosticIds(run).Should().Equal(expectedId);
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().BeEmpty();
    }

    /// <summary>P16: the handler passed <c>CancellationToken.None</c>, so Ctrl+C never reached <c>Execute</c>.</summary>
    [Fact]
    public async Task P16_TheHandlerForwardsTheInvocationCancellationToken()
    {
        var run = Run(Usings + $$"""
            namespace Demo;

            [CliRootCommand(Name = "demo")]
            public partial class RootCommand : ICliCommand
            {
                public static CancellationToken Received;

                public Task<int> Execute(CancellationToken cancellationToken)
                {
                    Received = cancellationToken;
                    return Task.FromResult(7);
                }
            }
            """);

        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("SetAction(async (parseResult, cancellationToken) =>");
        run.AllSources.Should().Contain("handler.Execute(cancellationToken)");
        run.AllSources.Should().NotContain("CancellationToken.None");

        // Behaviour: invoke the built command with a token and check that Execute received that token.
        var assembly = run.Emit();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        assembly.GetGeneratedType("TestRoot.RootCommandCliExtensions").GetMethod("AddRootCommand")!.Invoke(null, [services]);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var command = (System.CommandLine.RootCommand)assembly.GetGeneratedType("TestRoot.RootCommandCliExtensions")
            .GetMethod("BuildRootCommand")!.Invoke(null, [provider])!;

        using var cts = new CancellationTokenSource();
        var exitCode = await command.Parse([]).InvokeAsync(cancellationToken: cts.Token);

        exitCode.Should().Be(7);
        var received = (CancellationToken)assembly.GetGeneratedType("Demo.RootCommand").GetField("Received")!.GetValue(null)!;
        received.CanBeCanceled.Should().BeTrue("Execute must receive the invocation's token, not CancellationToken.None");
    }

    /// <summary>P12: attributes are matched by metadata name, so a same-named attribute in another namespace is ignored.</summary>
    [Fact]
    public void P12_ASameNamedAttributeInAnotherNamespace_IsIgnored()
    {
        var run = Run("""
            namespace Other
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class CliRootCommandAttribute : System.Attribute { }
            }

            namespace Demo
            {
                [Other.CliRootCommand]
                public partial class RootCommand { }
            }
            """);

        run.Diagnostics.Should().BeEmpty();
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().BeEmpty();
    }

    /// <summary>
    /// The #193 case: unrelated partial code of every kind next to one valid command. The regex generator does not
    /// run in this harness, so its partial methods' CS8795 is expected and excluded.
    /// </summary>
    [Fact]
    public void UnrelatedPartialCode_DoesNotDisturbTheGenerator()
    {
        var run = Run(GeneratedCodeChecks.UnrelatedPartials, Usings + $$"""
            namespace Demo;

            [CliRootCommand(Name = "demo")]
            public partial class RootCommand : ICliCommand
            {
                {{Execute}}
            }
            """);

        run.Diagnostics.Should().BeEmpty();
        CompilerErrors(run).Where(e => e.Id != "CS8795").Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("BuildCliRootCommand");
    }

    /// <summary>The generated file compiles without warnings under <c>#nullable enable</c>.</summary>
    [Fact]
    public void GeneratedCode_IsWarningFreeWithNullableEnabled()
    {
        var run = Run("#nullable enable\n" + Usings + $$"""
            namespace Demo;

            [CliRootCommand(Name = "demo", Description = "Demo tool")]
            public partial record RootCommand : ICliCommand
            {
                [CliOption("--name"), NoInterfaceMember] public string? Name { get; set; }
                [CliOption("--label"), NoInterfaceMember] public string Label { get; set; } = "";
                [CliOption("--count", DefaultValue = 2), NoInterfaceMember] public int Count { get; set; }
                [CliOption("--mode", Required = true), NoInterfaceMember] public string Mode { get; set; } = "";
                [CliArgument(0), NoInterfaceMember] public string Path { get; set; } = "";
                [CliArgument(1, Required = false), NoInterfaceMember] public string Extra { get; set; } = "";
                {{Execute}}
            }
            """);

        run.Diagnostics.Should().BeEmpty();
        CompilerErrors(run).Should().BeEmpty(run.ErrorText);

        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
    }
}
