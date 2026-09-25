using BenchmarkDotNet.Running;
using MintPlayer.SourceGenerators.Tools.Benchmarks;
using MintPlayer.SourceGenerators.Tools.Benchmarks.Equality;
using MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;
using L = MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;
using G = MintPlayer.SourceGenerators.Tools.Benchmarks.Generated;
#if NET
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tools.Benchmarks.Pipeline;
#endif

// A comparison is only meaningful if both sides do the same work, so the run is gated on proving it:
// Legacy and Generated must give the same answer on every shape, the generator must really have run, and
// each pipeline scenario must load its generators and react to a relevant edit (and only to that).
// Pass --verify-only to run just these checks.
Verification.Run();
if (args.Contains("--verify-only")) return;

BenchmarkSwitcher.FromAssembly(typeof(Verification).Assembly).Run(args);

static class Verification
{
    public static void Run()
    {
        Shape("flat 3 strings", Shapes.LegacyFlat, Shapes.GeneratedFlat);
        Shape("IReadOnlyList<string> x20", Shapes.LegacyStringList, Shapes.GeneratedStringList);
        Shape("ImmutableArray<Child> x50", Shapes.LegacyChildArray, Shapes.GeneratedChildArray);
        Shape("(Child, Child) tuple", Shapes.LegacyChildPair, Shapes.GeneratedChildPair);
        Shape("3-level abstract tree", Shapes.LegacyTree, Shapes.GeneratedTree);
        Shape("Spark-like List<Model> x20", Shapes.LegacySpark, Shapes.GeneratedSpark);

        // Equal pairs share no string instance, so nothing short-circuits on a reference below the root.
        var (a, b) = (Shapes.GeneratedFlat(false), Shapes.GeneratedFlat(false));
        Require(!ReferenceEquals(a.C, b.C), "equal pairs share string instances");

        // The generated tree dispatches on the runtime type: a Leaf never equals a Binary, in either variant.
        Require(!ComparerRegistry.For<L.Node>().Equals(new L.Leaf(), new L.Binary()), "Legacy: Leaf equals Binary");
        Require(!EqualityComparer<G.Node>.Default.Equals(new G.Leaf(), new G.Binary()), "Generated: Leaf equals Binary");

        Console.WriteLine("B1 fairness checks passed: Legacy and Generated agree on every shape.");

#if NET
        foreach (var scenario in PipelineScenario.Available())
            Pipeline(scenario);
        if (GeneratorBuild.Master is null)
            Console.WriteLine("B2: BENCH_MASTER_ROOT is not set, so only this branch's generators are measured.");
#endif
    }

    private static void Shape<TL, TG>(string name, Func<bool, TL> legacy, Func<bool, TG> generated)
        where TL : class where TG : class
    {
        Require(typeof(IEquatable<TG>).IsAssignableFrom(typeof(TG)),
            $"{typeof(TG).Name} does not implement IEquatable<T>: the ValueComparerGenerator did not run.");

        var lc = ComparerRegistry.For<TL>();
        Require(lc.GetType().Name == typeof(TL).Name + "ValueComparer",
            $"ComparerRegistry.For<{typeof(TL).Name}>() returned {lc.GetType().Name}, not the generated-style comparer.");
        var gc = EqualityComparer<TG>.Default;

        var (l1, l2, l3) = (legacy(false), legacy(false), legacy(true));
        var (g1, g2, g3) = (generated(false), generated(false), generated(true));

        Require(!ReferenceEquals(l1, l2) && !ReferenceEquals(g1, g2), $"{name}: an equal pair is one instance.");

        var answers = new (string What, bool Legacy, bool Generated, bool Expected)[]
        {
            ("equal pair", lc.Equals(l1, l2), gc.Equals(g1, g2), true),
            ("last property differs", lc.Equals(l1, l3), gc.Equals(g1, g3), false),
            ("symmetry", lc.Equals(l3, l1), gc.Equals(g3, g1), false),
            ("null", lc.Equals(l1, null), gc.Equals(g1, null), false),
            ("equal hashes", lc.GetHashCode(l1) == lc.GetHashCode(l2), gc.GetHashCode(g1) == gc.GetHashCode(g2), true),
        };
        foreach (var (what, l, g, expected) in answers)
            Require(l == expected && g == expected, $"{name}, {what}: Legacy={l}, Generated={g}, expected {expected}.");
    }

#if NET
    private static void Pipeline(PipelineScenario scenario)
    {
        var generators = scenario.Build.Load(scenario.Assemblies);
        Require(generators.Length > 0, $"{scenario}: no generators loaded.");
        var (driver, compilation) = PipelineBenchmarks.Warm(scenario);

        var inputErrors = compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        var cold = driver.GetRunResult();
        Require(cold.Results.All(r => r.Exception is null), $"{scenario}: a generator threw: {cold.Results.FirstOrDefault(r => r.Exception is not null).Exception}");
        Require(cold.GeneratedTrees.Length > 0, $"{scenario}: nothing was generated.");

        var before = Snapshot(driver);
        (driver, compilation) = EditAndRun(driver, compilation, Corpus.UnrelatedFile, 2);
        Require(Snapshot(driver) == before, $"{scenario}: an unrelated edit changed the generated output.");
        (driver, compilation) = EditAndRun(driver, compilation, Corpus.RelevantFile, 3);
        Require(Snapshot(driver) != before, $"{scenario}: a relevant edit (lifetime toggle) did not change the generated output.");

        Console.WriteLine($"B2 {scenario}: {generators.Length} generators ({string.Join(", ", generators.Select(g => g.GetType().Name))}); " +
                          $"{cold.GeneratedTrees.Length} generated files; input compile errors {inputErrors}; unrelated edit leaves output unchanged, relevant edit changes it.");
    }

    private static (GeneratorDriver, CSharpCompilation) EditAndRun(GeneratorDriver driver, CSharpCompilation compilation, int file, int version)
    {
        var old = compilation.SyntaxTrees.Single(t => t.FilePath == $"F{file}.cs");
        var next = CSharpSyntaxTree.ParseText(Corpus.Edit(file, version), Corpus.ParseOptions, path: old.FilePath);
        compilation = compilation.ReplaceSyntaxTree(old, next);
        return (driver.RunGenerators(compilation), compilation);
    }

    private static string Snapshot(GeneratorDriver driver) =>
        string.Join("\n", driver.GetRunResult().GeneratedTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal).Select(t => t.FilePath + "\n" + t.GetText()));
#endif

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Fairness check failed: " + message);
    }
}
