namespace MintPlayer.Resilience.SourceGenerator.Tests.Generators;

/// <summary>
/// The golden test: the generator run over the sample sources (<c>Samples/*.cs</c>, embedded) must produce exactly
/// <c>Snapshots/ResiliencePipelines.g.cs</c>, and that output must compile.
/// </summary>
/// <remarks>
/// <para>
/// The samples are the same files this project compiles (so the generated code they get is also executed by the parity
/// and allocation tests), which makes the snapshot the reviewed, readable form of what every strategy emits.
/// </para>
/// <para>
/// <b>Refreshing the snapshot</b> after an intended change to the generator: build this project, then copy
/// <c>obj/Generated/MintPlayer.Resilience.SourceGenerator/MintPlayer.Resilience.SourceGenerator.Generators.ResiliencePipelineGenerator/ResiliencePipelines.g.cs</c>
/// over <c>Snapshots/ResiliencePipelines.g.cs</c> (the project sets <c>EmitCompilerGeneratedFiles</c>), and review the diff.
/// </para>
/// </remarks>
public class SnapshotTests
{
    [Fact]
    public void Samples_GenerateTheSnapshot()
    {
        var run = Harness.Instance.RunGenerator(Harness.Generator, Harness.Samples());

        run.GeneratedSources.Select(s => s.HintName).Should().Equal(["ResiliencePipelines.g.cs"]);
        var actual = Harness.Normalize(run.SourceFor("ResiliencePipelines.g.cs")!);
        var expected = Harness.Normalize(Harness.Resource("Snapshots.ResiliencePipelines.g.cs"));

        actual.Should().Be(expected, "the generated code must match the reviewed snapshot; see the remarks on refreshing it");
    }

    [Fact]
    public void Samples_GeneratedCodeCompiles()
    {
        var run = Harness.Instance.RunGenerator(Harness.Generator, Harness.Samples());

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void Samples_EveryPipelineIsGenerated()
    {
        var all = Harness.Instance.RunGenerator(Harness.Generator, Harness.Samples()).AllSources;

        foreach (var name in new[]
        {
            "CatalogPipeline", "OrdersPipeline", "RetryTimeoutPipeline", "BreakerPipeline", "FallbackPipeline", "GenericPipeline",
            "ReloadablePipeline", "HedgingPipeline", "InstanceBreakerPipeline", "AllocationPipeline", "UnpooledPipeline",
            "EmptyPipeline", "RetryFallbackPipeline", "KitchenSinkPipeline", "TenantPipeline", "InstanceHedgingPipeline", "NestedPipeline",
        })
        {
            all.Should().Contain($"partial class {name} :", $"{name} must be generated, not skipped");
        }
    }
}
