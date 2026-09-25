using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Guards;

/// <summary>
/// PRD-EqualitySingleFile D4: every generator emits a fixed set of files. The set of hint names must not depend on
/// how many inputs there are, nor on what they are called.
/// </summary>
/// <remarks>
/// <para>
/// A hint name derived from a type grows with namespace depth, nesting and generic arity. Once a name passes 255
/// characters, a build with <c>EmitCompilerGeneratedFiles</c> fails with <c>CS0016</c>, because the file is
/// written to <c>obj/generated</c> under that name. 12.0.0's equality generator emitted
/// <c>&lt;Namespace&gt;.&lt;Type&gt;.Equality.g.cs</c> per model and did exactly that.
/// </para>
/// <para>
/// Each generator runs twice over the same kind of corpus: once with one decorated input, once with five. Every
/// input is a file of its own, in a namespace of its own, declaring a type named <c>Item{i}</c>, so a hint name built
/// from a type name or a namespace, and a file count that grows with the inputs, both change the set. No name is
/// hard-coded here.
/// </para>
/// <para>
/// The corpus has to be per generator (spike S1): each generator triggers on a different attribute or shape.
/// <c>JoinMethodGenerator</c> is the exception to "decorated types": its only input is one assembly-level
/// <c>[GenerateJoinMethods(n)]</c>, so its corpus varies that count and the number of unrelated classes instead.
/// The Assertions generators are guarded the same way in their own test project.
/// </para>
/// </remarks>
public class FixedFileSetGuardTests
{
    private const string SourceGenerators = "MintPlayer.SourceGenerators";
    private const string Mapper = "MintPlayer.Mapper";
    private const string Cli = "MintPlayer.CliGenerator";
    private const string ValueComparers = "MintPlayer.ValueComparerGenerator";

    /// <summary>
    /// One generator and its corpus: <see cref="Item"/> is repeated with <c>{i}</c> replaced by 1 to n, each copy a
    /// file of its own with <see cref="Usings"/> and the namespace <c>Demo.N{i}</c>. <see cref="Shared"/>, when set, is
    /// one more file, the same in both runs.
    /// </summary>
    /// <param name="ExpectItemNames">
    /// Whether the output names every item: proof that the generator saw all five, so the five-input run means
    /// something. False only where the output names no input.
    /// </param>
    public sealed record Case(string Generator, string Assembly, string Usings, string Item, string Shared = "", bool ExpectItemNames = true)
    {
        public string[] Corpus(int count)
        {
            var items = Enumerable.Range(1, count).Select(i => string.Join(Environment.NewLine,
                Usings, "", $"namespace Demo.N{i};", "", Item.Replace("{i}", i.ToString())));
            return Shared.Length == 0 ? [.. items] : [Shared, .. items];
        }

        public override string ToString() => Generator;
    }

    private static readonly Case[] Cases =
    [
        new("ClassNamesSourceGenerator", SourceGenerators, "", """
            public class Item{i} { }
            """),

        new("ServiceRegistrationsGenerator", SourceGenerators, """
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;
            """, """
            public interface IItem{i} { }

            [Register(typeof(IItem{i}), ServiceLifetime.Scoped)]
            public class Item{i} : IItem{i} { }
            """),

        new("DescriptionSourceGenerator", SourceGenerators, "", """
            /// <summary>Item{i} is documented.</summary>
            public partial class Item{i} { }
            """),

        new("InjectSourceGenerator", SourceGenerators, "using MintPlayer.SourceGenerators.Attributes;", """
            public partial class Item{i}
            {
                [Inject] private readonly System.IServiceProvider _provider;
            }
            """),

        // GenericMethodAttribute ships inside the generator assembly, so the corpus declares it itself under the
        // metadata name the generator looks up (see IncrementalOutputCachingTests).
        new("GenericMethodSourceGenerator", SourceGenerators, "using MintPlayer.SourceGenerators.Generators;", """
            public partial class Item{i}
            {
                [GenericMethod(2)]
                private partial void Add<T>(T value);
            }
            """, Shared: """
            namespace MintPlayer.SourceGenerators.Generators;

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class GenericMethodAttribute : System.Attribute
            {
                public GenericMethodAttribute(int count) { }
                public uint Count { get; set; } = 1;
                public System.Type? Transformer { get; set; }
            }
            """),

        new("MapperGenerator", Mapper, "using MintPlayer.Mapper.Attributes;", """
            public class Item{i}Dto
            {
                public int Id { get; set; }
            }

            [GenerateMapper(typeof(Item{i}Dto))]
            public class Item{i}
            {
                public int Id { get; set; }
            }
            """),

        new("CliCommandSourceGenerator", Cli, """
            using System.Threading;
            using System.Threading.Tasks;
            using MintPlayer.CliGenerator.Attributes;
            """, """
            [CliCommand("item{i}")]
            [CliParentCommand(typeof(global::Demo.RootCommand))]
            public partial class Item{i} : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult({i});
            }
            """, Shared: """
            using System.Threading;
            using System.Threading.Tasks;
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliRootCommand("Demo tool")]
            public partial class RootCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """),

        new("ValueComparerGenerator", ValueComparers, "using MintPlayer.ValueComparerGenerator.Attributes;", """
            [GenerateEquality]
            public partial class Item{i}
            {
                public string Name { get; set; } = "";
            }
            """),

        // One assembly attribute is the whole input, so Run varies its count (6 + n) beside the n classes. The output
        // names neither.
        new("JoinMethodGenerator", ValueComparers, "", """
            public class Item{i} { }
            """, ExpectItemNames: false),
    ];

    public static TheoryData<Case> AllCases()
    {
        var data = new TheoryData<Case>();
        foreach (var c in Cases) data.Add(c);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void EveryGenerator_EmitsTheSameFiles_ForOneInputAndForFive(Case c)
    {
        var one = Run(c, 1);
        var five = Run(c, 5);

        // No compile assertion: this guards file names only, and whether each generator's output compiles is the job
        // of its own tests. (GenericMethod's output for a private partial method, for one, does not.)
        one.GeneratedSources.Should().NotBeEmpty($"the {c.Generator} corpus must make the generator emit something");

        if (c.ExpectItemNames)
        {
            var text = string.Join("\n", five.GeneratedSources.Select(s => s.Source));
            foreach (var i in Enumerable.Range(1, 5))
                text.Should().Contain($"Item{i}", $"{c.Generator} must have seen all five inputs for the comparison to mean anything");
        }

        HintNames(five).Should().BeEquivalentTo(HintNames(one), because:
            $"{c.Generator} must emit a fixed set of files. A hint name derived from a type or namespace grows without "
            + "bound and fails the build with CS0016 past 255 characters (PRD-EqualitySingleFile)");
    }

    /// <summary>Every generator the harness can load must have a case, so a new one cannot skip the guard.</summary>
    [Fact]
    public void EveryGenerator_HasAFixedFileSetCase()
    {
        var generators = GeneratorHarness.AllGenerators();
        generators.Should().NotBeEmpty("the harness must find the generators, or this guard guards nothing");

        var covered = Cases.Select(c => (c.Assembly, c.Generator)).ToHashSet();
        var missing = generators
            .Where(g => !covered.Contains((g.Assembly, g.Generator.Name)))
            .Select(g => $"{g.Generator.Name} ({g.Assembly})")
            .ToList();

        missing.Should().BeEmpty($"every generator needs a corpus in {nameof(FixedFileSetGuardTests)}.{nameof(Cases)}. Missing: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Spike S1, kept as the guard's negative control: a generator that names each file after its type must fail
    /// the comparison the theory makes. Without this, a guard that compared nothing would pass forever.
    /// </summary>
    [Fact]
    public void TheGuard_DetectsAPerTypeHintName()
    {
        var c = new Case(nameof(PerTypeHintNameGenerator), typeof(PerTypeHintNameGenerator).Assembly.GetName().Name!, "", """
            public class Item{i} { }
            """);

        var one = Run(c, 1);
        var five = Run(c, 5);

        one.GeneratedSources.Should().ContainSingle();
        five.GeneratedSources.Should().HaveCount(5);
        HintNames(five).Should().NotBeEquivalentTo(HintNames(one));
    }

    private static GeneratorRun Run(Case c, int count)
    {
        var sources = c.Generator == "JoinMethodGenerator"
            ? [$"[assembly: MintPlayer.ValueComparerGenerator.Attributes.GenerateJoinMethods({6 + count})]", .. c.Corpus(count)]
            : c.Corpus(count);

        return GeneratorHarness.Run(c.Generator, sources, generatorAssemblyName: c.Assembly);
    }

    private static List<string> HintNames(GeneratorRun run) => run.GeneratedSources.Select(s => s.HintName).ToList();
}

/// <summary>The shape the guard forbids: one file per class, named after it. Only <see cref="FixedFileSetGuardTests"/> runs it.</summary>
public sealed class PerTypeHintNameGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classes = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, ct) => node is ClassDeclarationSyntax,
            static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct)?.ToDisplayString());

        context.RegisterSourceOutput(classes, static (spc, name) =>
        {
            if (name is not null) spc.AddSource($"{name}.g.cs", $"// {name}");
        });
    }
}
