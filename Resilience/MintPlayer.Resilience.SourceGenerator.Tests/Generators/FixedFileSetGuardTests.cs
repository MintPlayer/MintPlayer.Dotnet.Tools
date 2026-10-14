namespace MintPlayer.Resilience.SourceGenerator.Tests.Generators;

/// <summary>
/// The generator emits a fixed set of files: the hint names must not depend on how many pipelines there are, nor on
/// what they are called (SourceGenerators/CLAUDE.md: a per-type hint name grows past 255 characters and breaks a build
/// with <c>EmitCompilerGeneratedFiles</c>, CS0016). One pipeline and five, each in a namespace of its own, must give
/// the same hint names.
/// </summary>
public class FixedFileSetGuardTests
{
    private const string Item = """
        [ResiliencePipeline<int>]
        [Retry(DelayMs = 0)]
        public sealed partial class Item{i}Pipeline
        {
            [RetryWhen] static bool Handle(Outcome<int> outcome) => outcome.Result == {i};
        }
        """;

    private static string Corpus(int count)
        => "using MintPlayer.Resilience;" + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine,
            Enumerable.Range(1, count).Select(i =>
                $"namespace Demo.A.Very.Long.Namespace.Segment.N{i}{Environment.NewLine}{{{Environment.NewLine}{Item.Replace("{i}", i.ToString())}{Environment.NewLine}}}"));

    [Fact]
    public void TheGenerator_EmitsTheSameFiles_ForOneInputAndForFive()
    {
        var one = Harness.Instance.RunGenerator(Harness.Generator, Corpus(1));
        var five = Harness.Instance.RunGenerator(Harness.Generator, Corpus(5));

        one.GeneratedSources.Should().NotBeEmpty("the corpus must make the generator emit something");
        foreach (var i in Enumerable.Range(1, 5))
        {
            five.AllSources.Should().Contain($"Item{i}Pipeline", "the generator must have seen all five inputs for the comparison to mean anything");
        }

        five.GeneratedSources.Select(s => s.HintName).ToList().Should().BeEquivalentTo(
            one.GeneratedSources.Select(s => s.HintName).ToList(),
            because: "the generator must emit a fixed set of files; a hint name derived from a type grows without bound");
        one.GeneratedSources.Select(s => s.HintName).Should().Equal(["ResiliencePipelines.g.cs"]);
    }
}
