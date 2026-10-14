using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spike.S3.Generator;

const string PipelineSrc = """
    namespace Spike.S3;
    public static partial class Pipeline
    {
        public static int PlainCalls, StateCalls, Intercepted;
        public static System.Threading.Tasks.ValueTask<T> ExecuteAsync<T>(System.Func<System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<T>> cb, System.Threading.CancellationToken ct) => cb(ct);
        public static System.Threading.Tasks.ValueTask<T> ExecuteAsync<T, TState>(System.Func<TState, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<T>> cb, TState s, System.Threading.CancellationToken ct) => cb(s, ct);
    }
    """;
const string SitesSrc = """
    using System.Threading.Tasks;
    namespace Spike.S3;
    public class A
    {
        public int M(int id, System.Threading.CancellationToken ct) => Pipeline.ExecuteAsync(c => new ValueTask<int>(id), ct).Result;
        public int N(string s, System.Threading.CancellationToken ct) => Pipeline.ExecuteAsync(c => new ValueTask<int>(s.Length), ct).Result;
    }
    """;
const string OtherSrc = "namespace Spike.S3; public class B { public int X => 1; }";

var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Select(p => MetadataReference.CreateFromFile(p));
SyntaxTree T(string src, string path) => CSharpSyntaxTree.ParseText(src, parse, path);
var pipelineTree = T(PipelineSrc, "Pipeline.cs");
var sitesTree = T(SitesSrc, "Sites.cs");
var otherTree = T(OtherSrc, "Other.cs");
var comp = CSharpCompilation.Create("C", new[] { pipelineTree, sitesTree, otherTree }, refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

GeneratorDriver driver = CSharpGeneratorDriver.Create(
    new[] { new InterceptorGenerator().AsSourceGenerator() }, parseOptions: parse,
    driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
driver = driver.RunGenerators(comp);
Console.WriteLine($"initial: {driver.GetRunResult().Results[0].GeneratedSources.Length} source(s)");

var failures = 0;
void Step(string label, Compilation c, string expectSites, string expectAll)
{
    driver = driver.RunGenerators(c);
    var r = driver.GetRunResult().Results[0];
    string Reasons(string name) => string.Join(",", r.TrackedSteps[name].SelectMany(s => s.Outputs).Select(o => o.Reason).Distinct());
    var sites = Reasons("Sites");
    var all = Reasons("AllSites");
    var ok = sites == expectSites && all == expectAll;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {label,-58} Sites=[{sites}] AllSites=[{all}]  (expected [{expectSites}] / [{expectAll}])");
}

// 1. Edit a file with no call sites: per-site models are re-used, the output step is skipped.
var otherEdited = T(OtherSrc + "\n// edit", "Other.cs");
var c1 = comp.ReplaceSyntaxTree(otherTree, otherEdited);
Step("edit an unrelated file", c1, "Cached", "Cached");

// 2. Re-parse the sites file with identical text: the driver sees an equivalent tree and re-uses the models.
var sitesSame = T(SitesSrc, "Sites.cs");
var c2 = c1.ReplaceSyntaxTree(sitesTree, sitesSame);
Step("re-parse the sites file, identical text", c2, "Cached", "Cached");

// 3. Append a comment AFTER both call sites: positions are unchanged, but Data embeds the file's content
//    checksum, so every site in the file gets new Data and the output is regenerated.
var sitesComment = T(SitesSrc + "\n// trailing comment", "Sites.cs");
var c3 = c2.ReplaceSyntaxTree(sitesSame, sitesComment);
Step("append a comment after the call sites (same file)", c3, "Modified", "Modified");

// 4. Is InterceptableLocation itself value-equatable across two compilations of the same text?
static InterceptableLocation Loc(Compilation c, SyntaxTree t)
{
    var inv = t.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First(i => i.ToString().StartsWith("Pipeline.ExecuteAsync"));
    return c.GetSemanticModel(t).GetInterceptableLocation(inv)!;
}
var la = Loc(comp, sitesTree);
var lb = Loc(c2, sitesSame);
Console.WriteLine($"InterceptableLocation.Equals across compilations: {la.Equals(lb)}; Data equal: {la.Data == lb.Data}; Version {la.Version}");
Console.WriteLine($"Data: {la.Data}  Display: {la.GetDisplayLocation()}");

Console.WriteLine(failures == 0 ? "CACHE CHECKS PASSED" : $"{failures} CACHE CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
