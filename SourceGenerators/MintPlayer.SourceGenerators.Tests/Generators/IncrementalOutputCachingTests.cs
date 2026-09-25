using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tests._Infrastructure;
using IncrementalGeneratorResult = MintPlayer.SourceGenerators.Testing.IncrementalGeneratorResult;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// After an edit a generator does not care about — a method body changes — every output step of
/// every generator must be served from the driver's cache.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IncrementalityTests"/> asserts that <em>some</em> tracked step reports a cache hit and
/// that the output text is unchanged. Both held for years while every generator here regenerated
/// every file on every keystroke: the syntax steps were cached, but <c>ProduceCode</c> combined each
/// producer with the <c>Compilation</c> (a new object on every edit) and the pipeline models were
/// compared by reference, so the <c>SourceOutput</c> step re-ran every time and happened to write
/// the same text. Identical output cannot tell a cached pipeline from a recomputing one; only the
/// output step's run reason can.
/// </para>
/// <para>
/// So these tests assert on <see cref="IncrementalGeneratorResult.OutputsFullyCached"/>, which is
/// false when there are no output steps at all — a generator that stopped registering output
/// cannot pass vacuously. Each fixture is guarded to emit at least one file on the first run for the
/// same reason: a generator that produces nothing is trivially "cached".
/// </para>
/// <para>
/// Every fixture is paired with a second, unrelated file, and run with four edits, none of which
/// changes a declaration the generator reads:
/// </para>
/// <list type="bullet">
/// <item><see cref="Edit.Trailing"/> changes an expression body on the last member of a relevant
/// class. Nothing the generator reads moves.</item>
/// <item><see cref="Edit.Leading"/> changes a method body in an unrelated class placed before
/// everything the generator reads, and spreads that class over more lines, so every relevant
/// declaration moves down. Typing inside a method above the code a generator watches is the most
/// common edit there is; a model that carries a line-based location (<c>LocationKey</c>) changes on
/// it even though nothing the generator emits depends on it.</item>
/// <item><see cref="Edit.CommentOnly"/> adds comment lines and blank lines above everything the
/// generator reads: no token changes at all, only trivia, and every declaration moves.</item>
/// <item><see cref="Edit.OtherFile"/> edits the unrelated file. Its tree changes and the fixture's
/// does not.</item>
/// </list>
/// <para>
/// The edit is replayed with <see cref="GeneratorHarness.RunKeystroke"/>, which replaces only the
/// edited tree, as an IDE does. Re-parsing the whole second compilation would hand the driver a new
/// tree for every file and hide the difference between "the transform re-ran and a comparer absorbed
/// it" and "the transform never had to run".
/// </para>
/// </remarks>
public class IncrementalOutputCachingTests
{
    private const string BodyToken = "__BODY__";
    private const string LeadToken = "__LEAD__";

    public enum Edit { Trailing, Leading, CommentOnly, OtherFile }

    private const string LeadBefore = "public static class Leading { public static int Touch() { return 1; } }";
    private const string LeadAfter = """
        public static class Leading
        {
            public static int Touch()
            {
                return 42;
            }
        }
        """;

    private const string CommentAfter = """
        // Only trivia changes here: a comment, and blank lines around it,
        // so every declaration below moves down.


        """;

    private const string OtherFileBefore = """
        namespace Elsewhere;

        public static class Unrelated
        {
            public static int Compute() => 1;
        }
        """;

    private const string OtherFileAfter = """
        namespace Elsewhere;

        public static class Unrelated
        {
            public static int Compute()
            {
                return 42;
            }
        }
        """;

    /// <summary>One generator, a fixture that makes it emit, the assembly it lives in, and the edit.</summary>
    /// <remarks>
    /// <c>__LEAD__</c> marks where the leading class or comment goes: before every declaration the
    /// generator reads. <c>__BODY__</c> is the trailing expression body.
    /// </remarks>
    public sealed record Case(string Generator, string Assembly, string Fixture, Edit Edit = Edit.Trailing)
    {
        /// <summary>The fixture, then the unrelated file.</summary>
        public string[] Sources => [Render(lead: Edit == Edit.Leading ? LeadBefore : "", body: "1"), OtherFileBefore];

        public int EditIndex => Edit == Edit.OtherFile ? 1 : 0;

        /// <summary>The text of <see cref="Sources"/>[<see cref="EditIndex"/>] after the edit.</summary>
        public string Edited => Edit switch
        {
            Edit.Trailing => Render(lead: "", body: "42"),
            Edit.Leading => Render(lead: LeadAfter, body: "1"),
            Edit.CommentOnly => Render(lead: CommentAfter, body: "1"),
            Edit.OtherFile => OtherFileAfter,
            _ => throw new ArgumentOutOfRangeException(nameof(Edit), Edit, null),
        };

        public string Render(string lead, string body) => Fixture.Replace(LeadToken, lead).Replace(BodyToken, body);

        // xunit displays the case by ToString; the fixture text is noise.
        public override string ToString() => $"{Generator} ({Edit})";
    }

    private const string SourceGenerators = "MintPlayer.SourceGenerators";
    private const string Mapper = "MintPlayer.Mapper";
    private const string Cli = "MintPlayer.CliGenerator";
    private const string ValueComparers = "MintPlayer.ValueComparerGenerator";

    private static readonly Case[] Cases =
    [
        new("ClassNamesSourceGenerator", SourceGenerators, """
            namespace Demo;

            __LEAD__

            public class Alpha { }

            public class Beta
            {
                public int Touch() => __BODY__;
            }
            """),

        new("ServiceRegistrationsGenerator", SourceGenerators, """
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            __LEAD__

            public interface IGreeter { string Greet(); }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                public string Greet() => "hi";
                public int Touch() => __BODY__;
            }
            """),

        new("DescriptionSourceGenerator", SourceGenerators, """
            namespace Demo;

            __LEAD__

            /// <summary>A widget that does widget things.</summary>
            public partial class Widget
            {
                public int Touch() => __BODY__;
            }
            """),

        new("InjectSourceGenerator", SourceGenerators, """
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            __LEAD__

            public partial class Service
            {
                [Inject] private readonly System.IServiceProvider _provider;

                public int Touch() => __BODY__;
            }
            """),

        // GenericMethodAttribute ships inside the generator assembly, so the fixture declares it
        // itself under the metadata name the generator looks up (see GenericMethodGenerationTests).
        new("GenericMethodSourceGenerator", SourceGenerators, """
            namespace MintPlayer.SourceGenerators.Generators
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class GenericMethodAttribute : System.Attribute
                {
                    public GenericMethodAttribute(int count) { }
                    public uint Count { get; set; } = 1;
                    public System.Type? Transformer { get; set; }
                }
            }

            namespace Demo
            {
                using MintPlayer.SourceGenerators.Generators;

                __LEAD__

                public partial class Builder
                {
                    [GenericMethod(3)]
                    private partial void Add<T>(T value);

                    public int Touch() => __BODY__;
                }
            }
            """),

        new("MapperGenerator", Mapper, """
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            __LEAD__

            public class PersonDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            [GenerateMapper(typeof(PersonDto))]
            public class Person
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";

                public int Touch() => __BODY__;
            }
            """),

        new("CliCommandSourceGenerator", Cli, """
            using System.Threading;
            using System.Threading.Tasks;
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            __LEAD__

            [CliRootCommand("Demo tool")]
            public partial class RootCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }

            [CliCommand("build")]
            [CliParentCommand(typeof(RootCommand))]
            public partial class BuildCommand : ICliCommand
            {
                [CliOption("--verbose")] public bool Verbose { get; set; }

                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(__BODY__);
            }
            """),

        new("ValueComparerGenerator", ValueComparers, """
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            __LEAD__

            [GenerateEquality]
            public abstract partial class Shape
            {
                public string Name { get; set; } = "";
            }

            public partial class Circle : Shape
            {
                public double Radius { get; set; }

                public int Touch() => __BODY__;
            }
            """),

        new("JoinMethodGenerator", ValueComparers, """
            namespace Demo;

            __LEAD__

            public class Thing
            {
                public int Touch() => __BODY__;
            }
            """),
    ];

    public static TheoryData<Case> AllGenerators()
    {
        var data = new TheoryData<Case>();
        foreach (var c in Cases)
        foreach (var edit in Enum.GetValues<Edit>())
            data.Add(c with { Edit = edit });
        return data;
    }

    [Theory]
    [MemberData(nameof(AllGenerators))]
    public void AnIrrelevantEdit_IsServedFromCache(Case c)
    {
        var run = GeneratorHarness.RunKeystroke(
            c.Generator, c.Sources, c.EditIndex, _ => c.Edited, generatorAssemblyName: c.Assembly);

        // Guard: a generator that emits nothing, or registers no output, is vacuously "cached".
        run.First.GeneratedSources.Should().NotBeEmpty(
            $"the {c.Generator} fixture must make the generator emit something for this test to mean anything");
        run.OutputReasons.Should().NotBeEmpty(
            "trackIncrementalGeneratorSteps must be on and the generator must register output");

        // The edit changes nothing the generator reads, so the output must be byte-identical.
        Texts(run.Second).Should().BeEquivalentTo(Texts(run.First));

        // And it must not have been recomputed to get there.
        run.OutputsFullyCached.Should().BeTrue(
            $"a {c.Edit} edit must not re-run any output step of {c.Generator}. {Describe(run)}");
    }

    /// <summary>
    /// Every generator the harness can load must have a case above, so a new generator cannot ship
    /// without an incrementality test.
    /// </summary>
    /// <remarks>
    /// The theory only covers what it is given. Before it existed, nothing noticed that nine of the
    /// eleven generators regenerated every file on every keystroke; a twelfth added without a case
    /// would go just as unnoticed. The Assertions generators are guarded the same way in their own
    /// test project.
    /// </remarks>
    [Fact]
    public void EveryGenerator_HasAnIncrementalityCase()
    {
        var generators = GeneratorHarness.AllGenerators();
        generators.Should().NotBeEmpty("the harness must find the generators, or this guard guards nothing");

        var covered = Cases.Select(c => (c.Assembly, c.Generator)).ToHashSet();
        var missing = generators
            .Where(g => !covered.Contains((g.Assembly, g.Generator.Name)))
            .Select(g => $"{g.Generator.Name} ({g.Assembly})")
            .ToList();

        missing.Should().BeEmpty(
            $"every generator needs a fixture in {nameof(IncrementalOutputCachingTests)}.{nameof(Cases)}. Missing: {string.Join(", ", missing)}");
    }

    /// <summary>A relevant edit, and a fragment the output must contain afterwards.</summary>
    public sealed record RelevantCase(string Generator, string Assembly, string Fixture, string Find, string Replace, string Expected)
    {
        public override string ToString() => Generator;
    }

    /// <summary>
    /// The counterpart to the theory above, for the generator families that have no other test of
    /// it: a comparer that calls everything equal passes every cache test and fails here.
    /// </summary>
    /// <remarks>
    /// ServiceRegistrations is covered by <see cref="IncrementalityTests.ARelevantEditIsNotServedFromCache"/>,
    /// and Mapper, per producer, by <see cref="AnEditToOneMapperProducersInput_LeavesTheOtherProducersOutputCached"/>.
    /// </remarks>
    public static TheoryData<RelevantCase> RelevantEdits() => new()
    {
        new RelevantCase("ValueComparerGenerator", ValueComparers,
            Fixture("ValueComparerGenerator"),
            // On the [GenerateEquality] base. (A property added to Circle is emitted too: derived types get
            // their own members now, attribute or not.)
            Find: "public string Name { get; set; } = \"\";",
            Replace: "public string Name { get; set; } = \"\";\n    public int Sides { get; set; }",
            Expected: "Sides"),

        new RelevantCase("CliCommandSourceGenerator", Cli,
            Fixture("CliCommandSourceGenerator"),
            Find: "[CliOption(\"--verbose\")] public bool Verbose { get; set; }",
            Replace: "[CliOption(\"--verbose\")] public bool Verbose { get; set; }\n    [CliOption(\"--quiet\")] public bool Quiet { get; set; }",
            Expected: "--quiet"),
    };

    [Theory]
    [MemberData(nameof(RelevantEdits))]
    public void ARelevantEdit_ReRunsTheOutput(RelevantCase c)
    {
        var source = new Case(c.Generator, c.Assembly, c.Fixture).Render(lead: "", body: "1");
        source.Should().Contain(c.Find, "the edit must apply to the fixture");

        var run = GeneratorHarness.RunKeystroke(
            c.Generator, [source, OtherFileBefore], 0, text => text.Replace(c.Find, c.Replace),
            generatorAssemblyName: c.Assembly);

        string.Join("\n", Texts(run.First)).Should().NotContain(c.Expected);
        string.Join("\n", Texts(run.Second)).Should().Contain(c.Expected);
        run.OutputReasons.Should().Contain(IncrementalStepRunReason.Modified,
            $"the output step must have re-run to emit the new code. {Describe(run)}");
    }

    private static string Fixture(string generator) => Cases.Single(c => c.Generator == generator).Fixture;

    /// <summary>
    /// A relevant edit that only one producer reads must leave the other producer's output alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MapperGenerator has two producers with different inputs: <c>Mappers.g.cs</c> reads the
    /// <c>[GenerateMapper]</c> types <em>and</em> the <c>[MapperConversion]</c> static classes;
    /// <c>MapperEntrypoint.g.cs</c> reads only the types. Adding a conversion method therefore
    /// changes the first file and not the second. ClassNames is no use for this: both its producers
    /// read the same provider.
    /// </para>
    /// <para>
    /// The output step is identified by the hint names it emitted, recovered from the step's value.
    /// While <c>ProduceCode</c> funnels every producer into one output step, that step emits both
    /// files and the assertion fails — which is the defect being pinned.
    /// </para>
    /// <para>
    /// The conversions live in a file of their own. In the same file, the new method would push
    /// <c>Target</c> down a line, and the location-carrying <c>TypeToMap</c> would change for a
    /// reason unrelated to producer independence — that case is what <see cref="Edit.Leading"/> is for.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEditToOneMapperProducersInput_LeavesTheOtherProducersOutputCached()
    {
        const string types = """
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class Source { public string? Value { get; set; } }

            [GenerateMapper(typeof(Source))]
            public class Target { public int? Value { get; set; } }
            """;

        const string conversionsBefore = """
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public static class Conversions
            {
                [MapperConversion]
                public static int? StringToNullableInt(string? input)
                    => int.TryParse(input, out var r) ? r : null;
            }
            """;

        const string conversionsAfter = """
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public static class Conversions
            {
                [MapperConversion]
                public static int? StringToNullableInt(string? input)
                    => int.TryParse(input, out var r) ? r : null;

                [MapperConversion]
                public static string? NullableIntToString(int? input) => input?.ToString();
            }
            """;

        const string entrypoint = "MapperEntrypoint.g.cs";
        const string mappers = "Mappers.g.cs";

        var run = GeneratorHarness.RunKeystroke(
            "MapperGenerator", [types, conversionsBefore], 1, _ => conversionsAfter, generatorAssemblyName: Mapper);

        // Guards: both files exist, and the edit really is relevant to one of them.
        HintNames(run.First).Should().Contain(entrypoint);
        HintNames(run.First).Should().Contain(mappers);
        SourceOf(run.Second, mappers).Should().Contain("NullableIntToString",
            "the new conversion method belongs in the conversion switch");
        SourceOf(run.Second, entrypoint).Should().Be(SourceOf(run.First, entrypoint),
            "the entrypoint does not read the conversion methods");

        var entrypointSteps = OutputSteps(run)
            .Where(s => s.HintNames.Contains(entrypoint))
            .ToList();

        entrypointSteps.Should().NotBeEmpty($"some output step must emit {entrypoint}. {Describe(run)}");
        entrypointSteps.All(s => s.Reasons.All(IsCacheHit)).Should().BeTrue(
            $"the output step emitting {entrypoint} must be served from cache when only the conversion methods changed. {Describe(run)}");

        // The per-producer counter-test: the producer whose input did change must have re-run. A
        // pipeline that served everything from cache would pass the assertion above.
        OutputSteps(run).Where(s => s.HintNames.Contains(mappers)).SelectMany(s => s.Reasons)
            .Should().Contain(IncrementalStepRunReason.Modified,
                $"the output step emitting {mappers} must re-run when a conversion method is added. {Describe(run)}");
    }

    private static bool IsCacheHit(IncrementalStepRunReason r)
        => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged;

    private static List<string> Texts(GeneratorRunResult result)
        => result.GeneratedSources.Select(s => s.SourceText.ToString()).ToList();

    private static List<string> HintNames(GeneratorRunResult result)
        => result.GeneratedSources.Select(s => s.HintName).ToList();

    private static string? SourceOf(GeneratorRunResult result, string hintName)
        => result.GeneratedSources.FirstOrDefault(s => s.HintName == hintName).SourceText?.ToString();

    private sealed record OutputStep(string Key, IReadOnlyList<IncrementalStepRunReason> Reasons, IReadOnlyList<string> HintNames);

    /// <summary>Every output step of the second run, with the files it emitted.</summary>
    private static List<OutputStep> OutputSteps(IncrementalGeneratorResult run)
        => run.Second.TrackedOutputSteps
            .SelectMany(kv => kv.Value.Select(step => new OutputStep(
                kv.Key,
                step.Outputs.Select(o => o.Reason).ToList(),
                step.Outputs.SelectMany(o => EmittedHintNames(o.Value)).ToList())))
            .ToList();

    /// <summary>
    /// The hint names inside an output step's value.
    /// </summary>
    /// <remarks>
    /// A <c>SourceOutput</c> step's value is <c>(ImmutableArray&lt;GeneratedSourceText&gt;,
    /// ImmutableArray&lt;Diagnostic&gt;)</c>, and <c>GeneratedSourceText</c> is internal to Roslyn —
    /// hence reflection. It is the only way to tell which producer an output step belongs to.
    /// </remarks>
    private static IEnumerable<string> EmittedHintNames(object? value)
    {
        if (value is not ITuple tuple) yield break;

        for (var i = 0; i < tuple.Length; i++)
        {
            if (tuple[i] is not IEnumerable items || tuple[i] is string) continue;

            foreach (var item in items)
            {
                if (item?.GetType().GetProperty("HintName")?.GetValue(item) is string hint)
                    yield return hint;
            }
        }
    }

    /// <summary>The output steps and their reasons, formatted for an assertion failure message.</summary>
    private static string Describe(IncrementalGeneratorResult run)
    {
        var steps = OutputSteps(run).Select(s =>
            $"{s.Key}[{string.Join(",", s.Reasons)}] emits [{string.Join(",", s.HintNames)}]");

        return $"OutputReasons: [{string.Join(", ", run.OutputReasons)}]. "
            + $"TrackedOutputSteps: {string.Join(", ", run.Second.TrackedOutputSteps.Keys)}. "
            + $"Steps: {string.Join("; ", steps)}";
    }
}
