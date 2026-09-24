using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.SourceGenerator.Tests.Generators;

/// <summary>
/// After an edit a generator does not care about, every output step of both assertion generators
/// must be served from the driver's cache.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>MintPlayer.SourceGenerators.Tests.Generators.IncrementalOutputCachingTests</c>
/// for the generators that ship inside MintPlayer.Assertions. Identical output after a second run
/// proves nothing: a pipeline that recomputes everything on every keystroke writes the same text.
/// Only the output step's run reason tells the two apart, so that is what is asserted —
/// <see cref="IncrementalGeneratorResult.OutputsFullyCached"/>, which is false when there are no
/// output steps at all, so a generator that stopped registering output cannot pass vacuously.
/// </para>
/// <para>
/// Every fixture is paired with an unrelated second file and run with four edits:
/// <see cref="Edit.Trailing"/> changes a body after everything the generator reads;
/// <see cref="Edit.Leading"/> grows a class before it, so every declaration moves down — a model
/// carrying a line-based <c>LocationKey</c> changes on that even though nothing emitted depends on
/// it; <see cref="Edit.CommentOnly"/> adds only comments and blank lines above it; and
/// <see cref="Edit.OtherFile"/> edits the other file. Each is replayed with
/// <see cref="GeneratorHarness.RunKeystroke"/>, which replaces only the edited tree, as an IDE does.
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

    public sealed record Case(string Generator, string Fixture, Edit Edit = Edit.Trailing)
    {
        /// <summary>The fixture, then the unrelated file.</summary>
        public string[] Sources => [Render(lead: Edit == Edit.Leading ? LeadBefore : "", body: "1"), OtherFileBefore];

        public int EditIndex => Edit == Edit.OtherFile ? 1 : 0;

        public string Edited => Edit switch
        {
            Edit.Trailing => Render(lead: "", body: "42"),
            Edit.Leading => Render(lead: LeadAfter, body: "1"),
            Edit.CommentOnly => Render(lead: CommentAfter, body: "1"),
            Edit.OtherFile => OtherFileAfter,
            _ => throw new ArgumentOutOfRangeException(nameof(Edit), Edit, null),
        };

        public string Render(string lead, string body) => Fixture.Replace(LeadToken, lead).Replace(BodyToken, body);

        public override string ToString() => $"{Generator} ({Edit})";
    }

    private static readonly Case[] Cases =
    [
        new("GenerateAssertionGenerator", """
            using MintPlayer.Assertions;

            namespace Demo;

            __LEAD__

            public static class Predicates
            {
                [GenerateAssertion]
                public static bool IsEven(int value) => value % 2 == 0;

                public static int Touch() => __BODY__;
            }
            """),

        new("EquivalencyRegistrationGenerator", """
            using MintPlayer.Assertions;

            namespace Demo;

            __LEAD__

            public class Person
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }

            public class Test
            {
                public void Run(Person actual, Person expected)
                    => actual.Should().BeEquivalentTo(expected);
            }

            public static class Unrelated
            {
                public static int Touch() => __BODY__;
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
        var run = Harness.Instance.RunKeystroke(c.Generator, c.Sources, c.EditIndex, _ => c.Edited);

        // Guard: a generator that emits nothing, or registers no output, is vacuously "cached".
        run.First.GeneratedSources.Should().NotBeEmpty(
            $"the {c.Generator} fixture must make the generator emit something for this test to mean anything");
        run.OutputReasons.Should().NotBeEmpty(
            "trackIncrementalGeneratorSteps must be on and the generator must register output");

        // The edit changes nothing the generator reads, so the output must be byte-identical.
        run.OutputUnchanged.Should().BeTrue($"a {c.Edit} edit must not change the generated sources");

        run.OutputsFullyCached.Should().BeTrue(
            $"a {c.Edit} edit must not re-run any output step of {c.Generator}. "
            + $"OutputReasons: [{string.Join(", ", run.OutputReasons)}]. "
            + $"TrackedOutputSteps: {string.Join(", ", run.Second.TrackedOutputSteps.Keys)}");
    }

    /// <summary>
    /// Every generator in the assembly must have a case above, so a new one cannot ship without an
    /// incrementality test.
    /// </summary>
    [Fact]
    public void EveryGenerator_HasAnIncrementalityCase()
    {
        var generators = Harness.Instance.GeneratorTypes();
        generators.Should().NotBeEmpty("the harness must find the generators, or this guard guards nothing");

        var covered = Cases.Select(c => c.Generator).ToHashSet(StringComparer.Ordinal);
        var missing = generators.Select(g => g.Name).Where(name => !covered.Contains(name)).ToList();

        missing.Should().BeEmpty(
            $"every generator needs a fixture in {nameof(IncrementalOutputCachingTests)}.{nameof(Cases)}. Missing: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The counterpart to the cache tests: a comparer that calls everything equal passes all of
    /// them and fails here.
    /// </summary>
    [Fact]
    public void ARelevantEdit_ReRunsTheOutput()
    {
        const string find = "public static bool IsEven(int value) => value % 2 == 0;";
        var source = Cases.Single(c => c.Generator == "GenerateAssertionGenerator").Render(lead: "", body: "1");
        source.Should().Contain(find, "the edit must apply to the fixture");

        var run = Harness.Instance.RunKeystroke("GenerateAssertionGenerator", [source, OtherFileBefore], 0,
            text => text.Replace(find, find + "\n\n    [GenerateAssertion]\n    public static bool IsOdd(int value) => value % 2 != 0;"));

        run.First.AllSources().Should().NotContain("BeOdd");
        run.Second.AllSources().Should().Contain("BeOdd");
        run.OutputReasons.Should().Contain(IncrementalStepRunReason.Modified,
            $"the output step must have re-run to emit the new assertion. OutputReasons: [{string.Join(", ", run.OutputReasons)}]");
    }
}

file static class GeneratorRunResultExtensions
{
    public static string AllSources(this GeneratorRunResult result)
        => string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
}
