using Microsoft.CodeAnalysis;

namespace MintPlayer.Resilience.SourceGenerator.Tests.Generators;

/// <summary>
/// After an edit the generator does not care about, every output step must be served from the driver's cache.
/// </summary>
/// <remarks>
/// The counterpart of the Assertions generator's <c>IncrementalOutputCachingTests</c>. Identical output after a second
/// run proves nothing (a pipeline that recomputes everything writes the same text), so the output steps' run reasons
/// are asserted: <see cref="IncrementalGeneratorResult.OutputsFullyCached"/>. Each fixture is paired with an unrelated
/// second file and replayed with four edits as keystrokes (<see cref="GeneratorHarness.RunKeystroke"/>): a body after
/// everything the generator reads, a class that grows before the pipeline (every declaration moves down), comment-only
/// lines above it, and an edit in the other file.
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

    private const string Fixture = """
        using System;
        using System.Threading.Tasks;
        using MintPlayer.Resilience;
        using MintPlayer.Resilience.Retry;

        namespace Demo;

        __LEAD__

        [ResiliencePipeline<int>(Reloadable = true)]
        [Timeout(TimeoutMs = 10_000)]
        [Retry(MaxRetryAttempts = 3, DelayMs = 0)]
        [CircuitBreaker(MinimumThroughput = 10)]
        public sealed partial class CatalogPipeline
        {
            [RetryWhen] static bool Transient(Outcome<int> outcome) => outcome.Exception is not null;

            [OnRetry] static ValueTask Log(OnRetryArguments<int> args) => default;

            public static int Touch() => __BODY__;
        }
        """;

    public sealed record Case(Edit Edit)
    {
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

        public static string Render(string lead, string body) => Fixture.Replace(LeadToken, lead).Replace(BodyToken, body);

        public override string ToString() => $"{Harness.Generator} ({Edit})";
    }

    public static TheoryData<Case> AllEdits()
    {
        var data = new TheoryData<Case>();
        foreach (var edit in Enum.GetValues<Edit>())
        {
            data.Add(new Case(edit));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllEdits))]
    public void AnIrrelevantEdit_IsServedFromCache(Case c)
    {
        var run = Harness.Instance.RunKeystroke(Harness.Generator, c.Sources, c.EditIndex, _ => c.Edited);

        // Guard: a generator that emits nothing, or registers no output, is vacuously "cached".
        run.First.GeneratedSources.Should().NotBeEmpty("the fixture must make the generator emit something for this test to mean anything");
        run.OutputReasons.Should().NotBeEmpty("trackIncrementalGeneratorSteps must be on and the generator must register output");

        run.OutputUnchanged.Should().BeTrue($"a {c.Edit} edit must not change the generated sources");
        run.OutputsFullyCached.Should().BeTrue(
            $"a {c.Edit} edit must not re-run the output step. OutputReasons: [{string.Join(", ", run.OutputReasons)}]. "
            + $"TrackedOutputSteps: {string.Join(", ", run.Second.TrackedOutputSteps.Keys)}");
    }

    /// <summary>Every generator in the assembly is covered by the fixture above.</summary>
    [Fact]
    public void EveryGenerator_HasAnIncrementalityCase()
    {
        var generators = Harness.Instance.GeneratorTypes();
        generators.Should().NotBeEmpty("the harness must find the generators, or this guard guards nothing");

        generators.Select(g => g.Name).Should().Equal([Harness.Generator]);
    }

    /// <summary>The counterpart to the cache tests: a comparer that calls everything equal passes them all and fails here.</summary>
    [Fact]
    public void ARelevantEdit_ReRunsTheOutput()
    {
        var source = Case.Render(lead: "", body: "1");
        const string find = "[Retry(MaxRetryAttempts = 3, DelayMs = 0)]";
        source.Should().Contain(find, "the edit must apply to the fixture");

        var run = Harness.Instance.RunKeystroke(Harness.Generator, [source, OtherFileBefore], 0,
            text => text.Replace(find, "[Retry(MaxRetryAttempts = 5, DelayMs = 0)]"));

        run.First.GeneratedSources.Single().SourceText.ToString().Should().Contain("MaxRetryAttempts = 3");
        run.Second.GeneratedSources.Single().SourceText.ToString().Should().Contain("MaxRetryAttempts = 5");
        run.OutputReasons.Should().Contain(IncrementalStepRunReason.Modified,
            $"the output step must have re-run to emit the new value. OutputReasons: [{string.Join(", ", run.OutputReasons)}]");
    }

    /// <summary>A hook method's body is not part of the model: editing it must not regenerate the file.</summary>
    [Fact]
    public void EditingAHookBody_IsServedFromCache()
    {
        var source = Case.Render(lead: "", body: "1");
        const string find = "outcome.Exception is not null";

        var run = Harness.Instance.RunKeystroke(Harness.Generator, [source, OtherFileBefore], 0,
            text => text.Replace(find, "outcome.Exception is InvalidOperationException"));

        run.OutputUnchanged.Should().BeTrue();
        run.OutputsFullyCached.Should().BeTrue($"OutputReasons: [{string.Join(", ", run.OutputReasons)}]");
    }
}
