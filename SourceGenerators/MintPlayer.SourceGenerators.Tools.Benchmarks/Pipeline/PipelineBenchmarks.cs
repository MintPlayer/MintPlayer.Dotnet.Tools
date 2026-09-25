using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Pipeline;

/// <summary>One B2 case: which generators, over which corpus.</summary>
public sealed record PipelineScenario(string Set, string[] Assemblies, Corpus Corpus)
{
    /// <summary>
    /// SG5 is the 5 generators in MintPlayer.SourceGenerators over the S1 corpus: the set and corpus of the
    /// PRD's baseline. All adds MapperGenerator, ValueComparerGenerator and JoinMethodGenerator, over the corpus
    /// that gives them inputs.
    /// </summary>
    public static IEnumerable<PipelineScenario> Available() =>
    [
        new("SG5", [GeneratorBuild.SourceGenerators], Corpus.S1),
        new("All", [GeneratorBuild.SourceGenerators, GeneratorBuild.Mapper, GeneratorBuild.ValueComparerGenerator], Corpus.Full),
    ];

    public override string ToString() => Set;
}

/// <summary>
/// B2: the cost of one <c>RunGenerators</c> on a warm driver after a single-file edit, with step tracking off
/// (as in the IDE and the command-line compiler). The edit and the reparse happen in <see cref="IterationSetup"/>,
/// so only the generator run is measured; allocated bytes are the primary metric.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(PipelineConfig))]
[MedianColumn, MinColumn]
public class PipelineBenchmarks
{
    private GeneratorDriver driver = null!;
    private CSharpCompilation compilation = null!;
    private SyntaxTree editedTree = null!;
    private int version;

    [ParamsSource(nameof(Scenarios))]
    public string Scenario { get; set; } = "";

    [Params("Unrelated", "Relevant")]
    public string Edit { get; set; } = "";

    public static IEnumerable<string> Scenarios() => PipelineScenario.Available().Select(s => s.ToString());

    private int EditedFile => Edit == "Relevant" ? Corpus.RelevantFile : Corpus.UnrelatedFile;

    [GlobalSetup]
    public void Setup()
    {
        var scenario = PipelineScenario.Available().Single(s => s.ToString() == Scenario);
        (driver, compilation) = Warm(scenario);
        editedTree = compilation.SyntaxTrees.Single(t => t.FilePath == $"F{EditedFile}.cs");
        version = 1;
    }

    [IterationSetup]
    public void ApplyEdit()
    {
        var next = CSharpSyntaxTree.ParseText(Corpus.Edit(EditedFile, ++version), Corpus.ParseOptions, path: editedTree.FilePath);
        compilation = compilation.ReplaceSyntaxTree(editedTree, next);
        editedTree = next;
    }

    [Benchmark]
    public GeneratorDriver RunGenerators() => driver = driver.RunGenerators(compilation);

    /// <summary>A driver over the scenario's corpus that has already done its cold run.</summary>
    public static (GeneratorDriver Driver, CSharpCompilation Compilation) Warm(PipelineScenario scenario)
    {
        var generators = GeneratorBuild.Load(scenario.Assemblies);
        var compilation = scenario.Corpus.Create();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(Microsoft.CodeAnalysis.GeneratorExtensions.AsSourceGenerator),
            parseOptions: Corpus.ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: false));
        driver = driver.RunGenerators(compilation);
        return (driver, compilation);
    }
}
