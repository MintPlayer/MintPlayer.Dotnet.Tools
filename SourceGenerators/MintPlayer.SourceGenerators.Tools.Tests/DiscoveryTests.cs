using System.CodeDom.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// PRD-TypeKindRobustness D0.3, D0.4 and C7: the discovery scaffolding caches, diagnostics are reported robustly,
/// and the producer preamble.
/// </summary>
public class DiscoveryTests
{
    private const string Attribute = """
        namespace N { [System.AttributeUsage(System.AttributeTargets.All)] public class MarkAttribute : System.Attribute { } }
        """;

    private const string Marked = """
        namespace N
        {
            [Mark] public partial class A { }
            [Mark] public class NotPartial { }
        }
        """;

    private const string Unrelated = "namespace N { public partial record R(int X); }";

    [Fact]
    public void GeneratesModelsAndReportsDiagnosticsAtTheirSource()
    {
        var (driver, compilation) = Run(new MarkGenerator(), Attribute, Marked, Unrelated);
        var result = driver.GetRunResult().Results.Single();

        result.Exception.Should().BeNull();
        result.GeneratedSources.Select(s => s.HintName).Should().Equal(["Marked.g.cs"]);
        var text = result.GeneratedSources.Single().SourceText.ToString();
        text.Should().StartWith(Producer.Header);
        text.Should().Contain("#nullable enable");
        text.Should().Contain("namespace N");

        var diagnostic = result.Diagnostics.Single();
        diagnostic.Id.Should().Be("TEST001");
        diagnostic.GetMessage().Should().Be("NotPartial is not partial");
        // Mapped back onto the compilation's own tree, so #pragma and .editorconfig apply.
        diagnostic.Location.SourceTree.Should().Be(compilation.SyntaxTrees.ElementAt(1));
        diagnostic.Location.SourceSpan.Should().Be(SpanOf(compilation.SyntaxTrees.ElementAt(1), "NotPartial"));
    }

    [Fact]
    public void AnUnrelatedEditRerunsNothing()
    {
        // Without diagnostics: a diagnostic needs the compilation to be reported, so its step re-runs by design.
        var (driver, compilation) = Run(new MarkGenerator(), Attribute, "namespace N { [Mark] public partial class A { } [Mark] partial struct B { } }", Unrelated);

        var unrelated = compilation.SyntaxTrees.Last();
        var edited = compilation.ReplaceSyntaxTree(unrelated, unrelated.WithChangedText(SourceText.From(Unrelated + " // edit")));
        var result = driver.RunGenerators(edited).GetRunResult().Results.Single();

        result.Diagnostics.Should().BeEmpty();
        result.TrackedSteps[MarkGenerator.ModelsStep].SelectMany(s => s.Outputs).Select(o => o.Reason)
            .Should().Equal([IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached]);
        result.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs)
            .Select(o => o.Reason).Distinct().Should().Equal([IncrementalStepRunReason.Cached]);
    }

    [Fact]
    public void MovingADiagnosticLeavesTheModelsUnchanged()
    {
        var (driver, compilation) = Run(new MarkGenerator(), Attribute, Marked, Unrelated);

        var marked = compilation.SyntaxTrees.ElementAt(1);
        var edited = compilation.ReplaceSyntaxTree(marked, marked.WithChangedText(SourceText.From("// moved\r\n" + Marked)));
        var result = driver.RunGenerators(edited).GetRunResult().Results.Single();

        result.TrackedSteps[MarkGenerator.ModelsStep].SelectMany(s => s.Outputs).Select(o => o.Reason)
            .Should().NotContain(IncrementalStepRunReason.Modified);
        result.Diagnostics.Single().Location.GetLineSpan().StartLinePosition.Line.Should().Be(4);
    }

    [Fact]
    public void AStaleLocationIsReportedWithoutOne()
    {
        // A LocationKey whose line is past the end of the file: ToLocation used to index out of range (C7).
        var stale = new DiagnosticInfo(Rule, new LocationKey("File0.cs", 999, 0, 999, 1), new[] { "Stale" });
        var (driver, _) = Run(new FixedDiagnosticsGenerator(stale), "class C { }");
        var result = driver.GetRunResult().Results.Single();

        result.Exception.Should().BeNull();
        var diagnostic = result.Diagnostics.Single();
        diagnostic.Id.Should().Be("TEST001");
        diagnostic.Location.Should().Be(Location.None);
    }

    [Fact]
    public void AThrowingReporterDoesNotStopTheGenerator()
    {
        var (driver, _) = Run(new ThrowingReporterGenerator(), "class C { }");
        var result = driver.GetRunResult().Results.Single();

        result.Exception.Should().BeNull();
        result.Diagnostics.Select(d => d.Id).Should().Equal(["TEST001", "MPSG002"]);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void ADiagnosticOutsideTheCompilationDoesNotCrash()
    {
        var (driver, _) = Run(new ForeignLocationReporterGenerator(), "class C { }");
        var result = driver.GetRunResult().Results.Single();

        result.Exception.Should().BeNull();
        result.Diagnostics.Select(d => d.Id).Should().Equal(["MPSG002", "TEST001"]);
    }

    [Theory]
    [InlineData(false, false, "class X { }\r\n")]
    [InlineData(true, false, "{Header}\r\n\r\nclass X { }\r\n")]
    [InlineData(true, true, "{Header}\r\n\r\n#nullable enable\r\n\r\nclass X { }\r\n")]
    public void Preamble(bool preamble, bool nullable, string expected)
    {
        var (driver, _) = Run(new ProducerGenerator(new TestProducer(preamble, nullable, "class X { }")), "class C { }");
        var source = driver.GetRunResult().Results.Single().GeneratedSources.Single().SourceText.ToString();
        expected = expected.Replace("{Header}", Producer.Header);
        source.Replace("\r\n", "\n").Should().Be(expected.Replace("\r\n", "\n"));
    }

    [Fact]
    public void APreambleAloneIsNotAFile()
    {
        var (driver, _) = Run(new ProducerGenerator(new TestProducer(true, true, null)), "class C { }");
        driver.GetRunResult().Results.Single().GeneratedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(DiagnosticInfo))]
    [InlineData(typeof(Discovered<string>))]
    [InlineData(typeof(LocationKey))]
    public void PipelineValuesHoldNoRoslynObjects(Type type)
    {
        // Models flow through the incremental pipeline: a Roslyn object in them (a Location, a symbol, even a
        // DiagnosticDescriptor) keeps compilation-bound state alive and is exactly what LocationKey exists to avoid.
        var roslynFields = type
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.FieldType.Namespace?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true)
            .Select(f => $"{f.Name}: {f.FieldType.Name}");
        roslynFields.Should().BeEmpty();
    }

    [Fact]
    public void TwoDifferentDescriptorsCannotShareAnId()
    {
        var warning = new DiagnosticDescriptor("TEST900", "Test", "{0}", "Test", DiagnosticSeverity.Warning, true);
        var info = new DiagnosticDescriptor("TEST900", "Test", "{0}", "Test", DiagnosticSeverity.Info, true);
        DiagnosticInfo.Create(warning, null, "x");
        DiagnosticInfo.Create(warning, null, "y"); // the same descriptor again is fine

        var act = () => DiagnosticInfo.Create(info, null, "x");

        act.Should().Throw<InvalidOperationException>().WithMessage("*TEST900*");
    }

    [Fact]
    public void DiagnosticInfoIsAValue()
    {
        var a = DiagnosticInfo.Create(Rule, null, "x", 1.5);
        var b = new DiagnosticInfo(new DiagnosticDescriptor("TEST001", "Test", "{0} is not partial", "Test", DiagnosticSeverity.Warning, true), null, new[] { "x", "1.5" });
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(DiagnosticInfo.Create(Rule, null, "y", 1.5));
        new Discovered<string>("m", new[] { a }).Should().Be(new Discovered<string>("m", new[] { b }));
        new Discovered<string>("m").Should().NotBe(Discovered<string>.None);
        Discovered<string>.Fail(a).Model.Should().BeNull();
    }

    [Fact]
    public void ToLocationChecksBounds()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { }\r\nclass D { }", path: "F.cs");
        var compilation = CSharpCompilation.Create("T", [tree]);
        var key = Location.Create(tree, SpanOf(tree, "D")).AsKey();
        ((LocationKey?)key).ToLocation(compilation)!.SourceSpan.Should().Be(SpanOf(tree, "D"));

        ((LocationKey?)null).ToLocation(compilation).Should().BeNull();
        ((LocationKey?)LocationKey.Null).ToLocation(compilation).Should().BeNull();
        ((LocationKey?)new LocationKey("Other.cs", 0, 0, 0, 1)).ToLocation(compilation).Should().BeNull();
        ((LocationKey?)new LocationKey("F.cs", 2, 0, 2, 1)).ToLocation(compilation).Should().BeNull();
        ((LocationKey?)new LocationKey("F.cs", 1, 0, 1, 500)).ToLocation(compilation).Should().BeNull();
        ((LocationKey?)new LocationKey("F.cs", 1, 5, 0, 0)).ToLocation(compilation).Should().BeNull();
        ((LocationKey?)new LocationKey("F.cs", -1, 0, 0, 0)).ToLocation(compilation).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "x();\n")]
    [InlineData("", "x();\n")]
    [InlineData("<global namespace>", "x();\n")]
    [InlineData("A.B", "namespace A.B\n{\n    x();\n}\n")]
    public void OpenNamespace(string? ns, string expected)
    {
        using var text = new StringWriter();
        using var writer = new IndentedTextWriter(text, "    ");
        using (writer.OpenNamespace(ns))
            writer.WriteLine("x();");
        writer.Flush();
        text.ToString().Replace("\r\n", "\n").Should().Be(expected);
    }

    internal static readonly DiagnosticDescriptor Rule = new("TEST001", "Test", "{0} is not partial", "Test", DiagnosticSeverity.Warning, true);

    private static TextSpan SpanOf(SyntaxTree tree, string identifier)
    {
        var index = tree.GetText().ToString().IndexOf(" " + identifier + " ", StringComparison.Ordinal);
        return new TextSpan(index + 1, identifier.Length);
    }

    private static (GeneratorDriver Driver, Compilation Compilation) Run(IIncrementalGenerator generator, params string[] sources)
    {
        var compilation = PathSpecCompiler.Compile("latest", sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        return (driver, compilation);
    }

    private sealed class MarkGenerator : IIncrementalGenerator
    {
        public const string ModelsStep = "Mark.Models";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var discovered = context.SyntaxProvider.ForAttribute(
                "N.MarkAttribute",
                static (node, ct) => node is TypeDeclarationSyntax,
                static (ctx, ct) =>
                {
                    var type = (INamedTypeSymbol)ctx.TargetSymbol;
                    var spec = type.GetPathSpec(ct)!;
                    return spec.IsSupported
                        ? new Discovered<PathSpec>(spec)
                        : Discovered<PathSpec>.Fail(DiagnosticInfo.Create(Rule, type.Locations.First(), type.Name));
                });

            var models = discovered.Models(ModelsStep).Collect()
                .Select(static (m, ct) => m.ToEquatableArray());
            context.ProduceCode(models.Select(static Producer (m, ct) => new MarkProducer(m)));
            context.ReportDiagnostics(discovered.Diagnostics());
        }
    }

    private sealed class MarkProducer(EquatableArray<PathSpec> specs) : Producer("N", "Marked.g.cs")
    {
        protected override bool WritePreamble => true;

        protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            foreach (var spec in specs)
            {
                using (writer.OpenNamespace(spec.ContainingNamespace))
                using (writer.OpenPathSpec(spec, includeTarget: true))
                    writer.WriteLine("public int Marked => 1;");
            }
        }
    }

    private sealed class TestProducer(bool preamble, bool nullable, string? body) : Producer("N", "Test.g.cs")
    {
        protected override bool WritePreamble => preamble;
        protected override bool NullableEnable => nullable;

        protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            if (body is not null) writer.WriteLine(body);
        }
    }

    private sealed class ProducerGenerator(Producer producer) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
            => context.ProduceCode(context.CompilationProvider.Select((c, ct) => producer));
    }

    private sealed class FixedDiagnosticsGenerator(params DiagnosticInfo[] diagnostics) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
            => context.ReportDiagnostics(context.CompilationProvider.SelectMany((c, ct) => diagnostics));
    }

    private sealed class ThrowingReporterGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
            => context.ReportDiagnostics(context.CompilationProvider.Select(static IDiagnosticReporter (c, ct) => new ThrowingReporter()));

        private sealed class ThrowingReporter : IDiagnosticReporter
        {
            public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
            {
                yield return Diagnostic.Create(Rule, Location.None, "First");
                throw new InvalidOperationException("boom");
            }
        }
    }

    private sealed class ForeignLocationReporterGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
            => context.ReportDiagnostics(context.CompilationProvider.Select(static IDiagnosticReporter (c, ct) => new ForeignReporter()));

        private sealed class ForeignReporter : IDiagnosticReporter
        {
            public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
            {
                // A tree the compilation does not contain: the driver refuses it.
                var foreign = CSharpSyntaxTree.ParseText("class Foreign { }", path: "Foreign.cs");
                yield return Diagnostic.Create(Rule, Location.Create(foreign, new TextSpan(6, 7)), "Foreign");
                yield return Diagnostic.Create(Rule, Location.None, "After");
            }
        }
    }
}
