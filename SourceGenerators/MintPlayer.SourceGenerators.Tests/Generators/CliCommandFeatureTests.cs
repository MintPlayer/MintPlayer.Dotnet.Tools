using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// The <see cref="CliCommandSourceGenerator"/> surface beyond the bare command tree: option
/// metadata, arguments, handler emission, and name derivation.
/// </summary>
/// <remarks>
/// <para>
/// <c>OtherGeneratorTests.ItBuildsACommandTree</c> covers a root command with one plain
/// <c>[CliOption("--verbose")]</c>. Everything else in the producer — descriptions, Required,
/// Hidden, DefaultValue, arguments, and the whole handler body — was reached only by the manual
/// <c>TestProjects/CliCommandDebugging</c> playground, which is not measured. Fixture syntax is
/// taken from that playground.
/// </para>
/// <para>
/// <b>A non-nested subcommand needs <c>[CliParentCommand]</c>.</b> A class carrying only
/// <c>[CliCommand("x")]</c>, with no parent attribute and not nested inside another command, is
/// silently dropped from the tree — it is not attached to the root and no diagnostic is reported.
/// That is worth knowing when reading these fixtures: the first drafts of every test here used the
/// orphan shape and produced a root-only tree with an empty <c>Errors</c>, so they failed on the
/// content assertions rather than on compilation. See the note on
/// <see cref="AnOrphanCommand_IsSilentlyDroppedFromTheTree"/>.
/// </para>
/// </remarks>
public class CliCommandFeatureTests
{
    private static GeneratorRun Run(string source)
        => GeneratorHarness.Run("CliCommandSourceGenerator", [source],
            generatorAssemblyName: "MintPlayer.CliGenerator");

    private const string Root = """
        using System.Threading;
        using System.Threading.Tasks;
        using MintPlayer.CliGenerator.Attributes;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        [CliRootCommand(Name = "demo", Description = "Demo tool")]
        public partial class RootCommand : ICliCommand
        {
            public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
        }
        """;

    /// <summary>
    /// Each of Description, Required, Hidden and DefaultValue is emitted by its own guarded block
    /// in the producer, and none had a fixture. DefaultValue additionally emits a
    /// <c>DefaultValueFactory</c> lambda rather than a plain assignment.
    /// </summary>
    [Fact]
    public void OptionMetadata_IsEmittedForEveryFacet()
    {
        var run = Run($$"""
            {{Root}}

            [CliCommand("build", Description = "Builds the thing")]
            [CliParentCommand(typeof(RootCommand))]
            public partial class BuildCommand : ICliCommand
            {
                [CliOption("--config", "-c", Description = "Build configuration", Required = true), NoInterfaceMember]
                public string Config { get; set; } = "";

                [CliOption("--secret", Hidden = true), NoInterfaceMember]
                public bool Secret { get; set; }

                [CliOption("--times", "-t", DefaultValue = 3), NoInterfaceMember]
                public int Times { get; set; }

                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Build configuration");
        run.AllSources.Should().Contain(".Required = true");
        run.AllSources.Should().Contain(".Hidden = true");
        run.AllSources.Should().Contain("DefaultValueFactory");
    }

    /// <summary>
    /// Arguments are a separate declaration path from options — different type, an Arity, and a
    /// position-ordered binding list. <c>CliArgumentDefinition</c> was 0% covered.
    /// </summary>
    [Fact]
    public void Arguments_AreDeclaredWithNameAndArity()
    {
        var run = Run($$"""
            {{Root}}

            [CliCommand("greet", Description = "Greets a person")]
            [CliParentCommand(typeof(RootCommand))]
            public partial class GreetCommand : ICliCommand
            {
                [CliArgument(0, Name = "name", Description = "Person to greet"), NoInterfaceMember]
                public string Name { get; set; } = "world";

                [CliArgument(1, Name = "salutation", Required = false), NoInterfaceMember]
                public string Salutation { get; set; } = "Hello";

                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Argument<");
        run.AllSources.Should().Contain("Person to greet");
        run.AllSources.Should().Contain("Arity");
    }

    /// <summary>
    /// The handler body resolves the command from DI, assigns every bound option and argument onto
    /// it, and awaits Execute. A command with both kinds of binding exercises both assignment
    /// loops, which are separate blocks in the producer.
    /// </summary>
    [Fact]
    public void TheHandler_ResolvesFromDiAndBindsOptionsAndArguments()
    {
        var run = Run($$"""
            {{Root}}

            [CliCommand("run")]
            [CliParentCommand(typeof(RootCommand))]
            public partial class RunCommand : ICliCommand
            {
                [CliOption("--verbose"), NoInterfaceMember]
                public bool Verbose { get; set; }

                [CliArgument(0, Name = "target"), NoInterfaceMember]
                public string Target { get; set; } = "";

                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("GetRequiredService<global::Demo.RunCommand>");
        run.AllSources.Should().Contain("handler.Verbose = parseResult.GetValue(");
        run.AllSources.Should().Contain("handler.Target = parseResult.GetRequiredValue(");
    }

    /// <summary>
    /// With no usable name, the generator derives the command name from the class name and the
    /// option name from the property name, both in kebab-case. This is the only fixture that
    /// executes <c>StringExtensions.ToKebabCase</c> or <c>ToCamelCase</c> at all.
    /// </summary>
    /// <remarks>
    /// <c>CliCommandAttribute</c> requires a name positionally, so the derivation branch is reached
    /// with an empty one — the generator tests <c>IsNullOrWhiteSpace</c>, not null — and only for a
    /// non-root command. <c>[CliOption]</c> with no aliases is legal because the constructor takes
    /// <c>params string[]</c>.
    /// </remarks>
    [Fact]
    public void WithNoUsableNames_NamesAreDerivedInKebabCase()
    {
        var run = Run($$"""
            {{Root}}

            [CliCommand("")]
            [CliParentCommand(typeof(RootCommand))]
            public partial class PublishArtifactCommand : ICliCommand
            {
                [CliOption, NoInterfaceMember]
                public bool DryRun { get; set; }

                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("publish-artifact-command");
        run.AllSources.Should().Contain("--dry-run");
    }

    /// <summary>
    /// A command nested inside another command class is attached to it without needing
    /// <c>[CliParentCommand]</c> — the nesting is the parent relationship. This is the shape the
    /// debugging playground uses.
    /// </summary>
    [Fact]
    public void ANestedCommand_IsAttachedToItsParent()
    {
        var run = Run($$"""
            {{Root}}

            [CliCommand("remote")]
            [CliParentCommand(typeof(RootCommand))]
            public partial class RemoteCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);

                [CliCommand("add")]
                public partial class AddCommand : ICliCommand
                {
                    [CliArgument(0, Name = "url"), NoInterfaceMember]
                    public string Url { get; set; } = "";

                    public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
                }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("remote");
        run.AllSources.Should().Contain("add");
    }

    /// <summary>
    /// Pins the behaviour that cost the first draft of this file: a <c>[CliCommand]</c> that is
    /// neither nested nor given a <c>[CliParentCommand]</c> vanishes from the generated tree, with
    /// no diagnostic to say so.
    /// </summary>
    /// <remarks>
    /// This test documents the current behaviour rather than endorsing it. Silently dropping a
    /// decorated command is a poor failure mode — the consumer gets a CLI missing a subcommand and
    /// nothing to search for — and <c>OtherGeneratorTests.ItBuildsACommandTree</c> hides it, since
    /// that test declares an orphan <c>BuildCommand</c> and then asserts only that <c>Errors</c> is
    /// empty and that <em>something</em> was generated. If the generator later grows a diagnostic
    /// for this, or attaches orphans to the root, this test should be updated to match, and that
    /// is the point of pinning it.
    /// </remarks>
    [Fact]
    public void AnOrphanCommand_IsSilentlyDroppedFromTheTree()
    {
        var run = Run($$"""
            {{Root}}

            [CliCommand("orphan")]
            public partial class OrphanCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("\"orphan\"",
            "an orphan command is currently dropped - if this starts failing the generator has " +
            "started handling it, and the remark on this test needs revisiting");
    }
}
