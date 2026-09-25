using BenchmarkDotNet.Running;
using MintPlayer.SourceGenerators.Tools.Benchmarks.Equality;
using MintPlayer.SourceGenerators.Tools.Benchmarks.Generated;
#if NET
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tools.Benchmarks.Pipeline;
#endif

// The numbers only mean something if the code under measurement is correct, so the run is gated on it: the
// generated models must compare correctly on every shape (which also proves the generator ran), and each
// pipeline scenario must load its generators and react to a relevant edit (and only to that).
// Pass --verify-only to run just these checks.
Verification.Run();
if (args.Contains("--verify-only")) return;

BenchmarkSwitcher.FromAssembly(typeof(Verification).Assembly).Run(args);

static class Verification
{
    public static void Run()
    {
        Shape("flat 3 strings", Shapes.CreateFlat);
        Shape("IReadOnlyList<string> x20", Shapes.CreateStringList);
        Shape("ImmutableArray<Child> x50", Shapes.CreateChildArray);
        Shape("(Child, Child) tuple", Shapes.CreateChildPair);
        Shape("3-level abstract tree", Shapes.CreateTree);
        Shape("Spark-like List<Model> x20", Shapes.CreateSpark);

        // Equal pairs share no string instance, so nothing short-circuits on a reference below the root.
        var (a, b) = (Shapes.CreateFlat(false), Shapes.CreateFlat(false));
        Require(!ReferenceEquals(a.C, b.C), "equal pairs share string instances");

        // The generated tree dispatches on the runtime type: a Leaf never equals a Binary.
        Require(!EqualityComparer<Node>.Default.Equals(new Leaf(), new Binary()), "a Leaf equals a Binary");

        Console.WriteLine("B1 checks passed: the generated models compare correctly on every shape.");

#if NET
        foreach (var scenario in PipelineScenario.Available())
            Pipeline(scenario);
#endif
    }

    private static void Shape<T>(string name, Func<bool, T> create) where T : class
    {
        Require(typeof(IEquatable<T>).IsAssignableFrom(typeof(T)),
            $"{typeof(T).Name} does not implement IEquatable<{typeof(T).Name}>: the ValueComparerGenerator did not run.");

        // T? because net481's EqualityComparer<T> carries no nullable annotations.
        var comparer = EqualityComparer<T?>.Default;
        var (x, y, diff) = (create(false), create(false), create(true));
        Require(!ReferenceEquals(x, y), $"{name}: an equal pair is one instance.");

        var answers = new (string What, bool Actual, bool Expected)[]
        {
            ("equal pair", comparer.Equals(x, y), true),
            ("equal pair, reversed", comparer.Equals(y, x), true),
            ("equal hashes", comparer.GetHashCode(x) == comparer.GetHashCode(y), true),
            ("last property differs", comparer.Equals(x, diff), false),
            ("last property differs, reversed", comparer.Equals(diff, x), false),
            ("null on the right", comparer.Equals(x, null), false),
            ("null on the left", comparer.Equals(null, x), false),
            ("null against null", comparer.Equals(null, null), true),
        };
        foreach (var (what, actual, expected) in answers)
            Require(actual == expected, $"{name}, {what}: got {actual}, expected {expected}.");
    }

#if NET
    private static void Pipeline(PipelineScenario scenario)
    {
        var generators = GeneratorBuild.Load(scenario.Assemblies);
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
        if (!condition) throw new InvalidOperationException("Verification failed: " + message);
    }
}
