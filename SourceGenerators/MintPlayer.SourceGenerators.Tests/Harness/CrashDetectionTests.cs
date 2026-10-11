using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using MintPlayer.SourceGenerators.Testing;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Tests.Harness;

/// <summary>
/// PRD-TypeKindRobustness H1/D5: the harness fails loudly when the component under test crashes.
/// </summary>
/// <remarks>
/// <para>
/// Before D5 a crash was silent. A generator exception becomes CS8785, which is a warning and never
/// reaches <see cref="GeneratorResult.Errors"/>; an analyzer exception becomes AD0001, which
/// <c>RunAnalyzerAsync</c> filtered out with every other id the analyzer does not declare. Either
/// way a crashing component read as one that had nothing to say.
/// </para>
/// <para>
/// The components below crash on purpose, so these tests pin the harness itself rather than any
/// shipped generator. They live in this assembly and are loaded through
/// <see cref="Testing.GeneratorHarness.ForAssembly"/> like any other component.
/// </para>
/// </remarks>
public class CrashDetectionTests
{
    private const string Source = """
        namespace Demo;

        public class Thing { }
        """;

    private static readonly Testing.GeneratorHarness Strict =
        Testing.GeneratorHarness.ForAssembly(typeof(CrashDetectionTests).Assembly.GetName().Name!);

    private static readonly Testing.GeneratorHarness Lenient = Strict.AllowComponentCrashes();

    #region Generators

    [Fact]
    public void ACrashingGeneratorThrowsByDefault()
    {
        var ex = Assert.Throws<ComponentCrashedException>(
            () => Strict.RunGenerator(nameof(CrashingGenerator), Source));

        ex.Message.Should().Contain("CS8785");
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        ex.InnerException!.Message.Should().Be(CrashingGenerator.Message);
    }

    [Fact]
    public void ACrashingGeneratorIsReportedOnTheResultWhenCrashesAreAllowed()
    {
        var run = Lenient.RunGenerator(nameof(CrashingGenerator), Source);

        var exception = Assert.Single(run.GeneratorExceptions);
        exception.Message.Should().Be(CrashingGenerator.Message);
        Assert.Contains(run.CrashDiagnostics, d => d.Id == "CS8785");

        // The gap H1 describes: CS8785 is a warning, so Errors alone never shows the crash.
        run.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EnsureNoCrashThrowsOnALenientResult()
    {
        var run = Lenient.RunGenerator(nameof(CrashingGenerator), Source);

        Assert.Throws<ComponentCrashedException>(() => run.EnsureNoCrash());
    }

    [Fact]
    public void AWellBehavedGeneratorPassesInStrictMode()
    {
        var run = Strict.RunGenerator(nameof(QuietGenerator), Source);

        run.GeneratorExceptions.Should().BeEmpty();
        run.CrashDiagnostics.Should().BeEmpty();
        Assert.Same(run, run.EnsureNoCrash());
    }

    [Fact]
    public void AllowComponentCrashesSurvivesTheOtherBuilders()
    {
        var run = Lenient.AddReferences(typeof(object)).WithRootNamespace("Other")
            .RunGenerator(nameof(CrashingGenerator), Source);

        Assert.Single(run.GeneratorExceptions);
    }

    [Fact]
    public void AllowComponentCrashesCanBeTurnedOffAgain()
        => Assert.Throws<ComponentCrashedException>(
            () => Lenient.AllowComponentCrashes(false).RunGenerator(nameof(CrashingGenerator), Source));

    [Fact]
    public void ACrashingGeneratorThrowsFromRunGeneratorTwice()
        => Assert.Throws<ComponentCrashedException>(
            () => Strict.RunGeneratorTwice(nameof(CrashingGenerator), [Source], [Source + " "]));

    [Fact]
    public void ACrashingGeneratorThrowsFromRunKeystroke()
        => Assert.Throws<ComponentCrashedException>(
            () => Strict.RunKeystroke(nameof(CrashingGenerator), [Source], 0, s => s + " "));

    [Fact]
    public void AnUnreportableDiagnosticThrowsByDefault()
    {
        // MPSG002: the generator survived, but a diagnostic it meant to report was lost. Without it in
        // the crash ids, the run reads as a clean one with a stray warning.
        var ex = Assert.Throws<ComponentCrashedException>(
            () => Strict.RunGenerator(nameof(UnreportableDiagnosticGenerator), Source));

        ex.Message.Should().Contain("MPSG002");
        ex.Message.Should().Contain(UnreportableDiagnosticGenerator.Message);
    }

    [Fact]
    public void AnUnreportableDiagnosticIsACrashDiagnosticWhenCrashesAreAllowed()
    {
        var run = Lenient.RunGenerator(nameof(UnreportableDiagnosticGenerator), Source);

        run.GeneratorExceptions.Should().BeEmpty();
        Assert.Contains(run.CrashDiagnostics, d => d.Id == "MPSG002");
        Assert.Throws<ComponentCrashedException>(() => run.EnsureNoCrash());
    }

    [Fact]
    public void RunKeystrokeReturnsWhenCrashesAreAllowed()
    {
        var run = Lenient.RunKeystroke(nameof(CrashingGenerator), [Source], 0, s => s + " ");

        Assert.NotNull(run);
    }

    #endregion

    #region Analyzers

    [Fact]
    public async Task ACrashingAnalyzerThrowsByDefault()
    {
        var ex = await Assert.ThrowsAsync<ComponentCrashedException>(
            () => Strict.RunAnalyzerAsync(nameof(CrashingAnalyzer), Source));

        ex.Message.Should().Contain("AD0001");
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        ex.InnerException!.Message.Should().Be(CrashingAnalyzer.Message);
    }

    [Fact]
    public async Task ACrashingAnalyzerReturnsAD0001WhenCrashesAreAllowed()
    {
        var diagnostics = await Lenient.RunAnalyzerAsync(nameof(CrashingAnalyzer), Source);

        // Roslyn hands AD0001 to onAnalyzerException INSTEAD of the result set (spike S2); the
        // harness has to put it back, or this list would be empty exactly as it was before H1.
        Assert.Contains(diagnostics, d => d.Id == "AD0001");
    }

    [Fact]
    public async Task ACrashingAnalyzerThrowsFromApplyCodeFixByDefault()
        => await Assert.ThrowsAsync<ComponentCrashedException>(
            () => Strict.ApplyCodeFixAsync(nameof(CrashingAnalyzer), nameof(CrashingCodeFixProvider), Source));

    [Fact]
    public async Task ACrashingAnalyzerIsReportedOnTheCodeFixResultWhenCrashesAreAllowed()
    {
        var result = await Lenient.ApplyCodeFixAsync(
            nameof(CrashingAnalyzer), nameof(CrashingCodeFixProvider), Source);

        result.Applied.Should().BeFalse();
        var exception = Assert.Single(result.AnalyzerExceptions);
        exception.Message.Should().Be(CrashingAnalyzer.Message);
        Assert.Contains(result.Diagnostics, d => d.Id == "AD0001");
    }

    #endregion

    #region Code fixes

    /// <summary>
    /// A code-fix provider that throws is not caught: there is no result to report it on, so its own
    /// exception escapes in both modes rather than being folded into a "nothing offered" result.
    /// </summary>
    [Fact]
    public async Task ACrashingCodeFixProviderEscapesInBothModes()
    {
        foreach (var harness in new[] { Strict, Lenient })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.ApplyCodeFixAsync(nameof(ReportingAnalyzer), nameof(CrashingCodeFixProvider), Source));

            ex.Message.Should().Be(CrashingCodeFixProvider.Message);
        }
    }

    #endregion
}

#region Deliberately crashing components

// No [Generator] / [DiagnosticAnalyzer] / [ExportCodeFixProvider]: the harness instantiates by name,
// and these are never shipped or loaded by a compiler. RS1001 (missing attribute) and RS2008 (release
// tracking) are rules for shipped analyzers; adding the attribute instead only trades RS1001 for
// RS1036/RS1038/RS1041, which are about the assembly hosting it.
#pragma warning disable RS1001, RS2008

public sealed class CrashingGenerator : IIncrementalGenerator
{
    public const string Message = "Deliberate generator crash.";

    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.RegisterSourceOutput(
            context.CompilationProvider,
            (_, _) => throw new InvalidOperationException(Message));
}

/// <summary>
/// Reports through <c>ReportDiagnostics</c> with a reporter that throws: the Tools plumbing turns
/// the lost diagnostic into MPSG002 instead of crashing the generator (CS8785).
/// </summary>
public sealed class UnreportableDiagnosticGenerator : IIncrementalGenerator
{
    public const string Message = "Deliberate reporter crash.";

    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.ReportDiagnostics(context.CompilationProvider.Select(
            static MintPlayer.SourceGenerators.Tools.IDiagnosticReporter (_, _) => new ThrowingReporter()));

    private sealed class ThrowingReporter : MintPlayer.SourceGenerators.Tools.IDiagnosticReporter
    {
        public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
            => throw new InvalidOperationException(Message);
    }
}

public sealed class QuietGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.RegisterPostInitializationOutput(c => c.AddSource("Quiet.g.cs", "namespace Quiet; internal class Marker { }"));
}

public sealed class CrashingAnalyzer : DiagnosticAnalyzer
{
    public const string Message = "Deliberate analyzer crash.";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [ReportingAnalyzer.Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            _ => throw new InvalidOperationException(Message),
            SyntaxKind.ClassDeclaration);
    }
}

public sealed class ReportingAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        "CRASHTEST001", "Every class", "Class found", "Testing", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            c => c.ReportDiagnostic(Diagnostic.Create(Rule, c.Node.GetLocation())),
            SyntaxKind.ClassDeclaration);
    }
}

public sealed class CrashingCodeFixProvider : CodeFixProvider
{
    public const string Message = "Deliberate code-fix crash.";

    public override ImmutableArray<string> FixableDiagnosticIds => [ReportingAnalyzer.Rule.Id];

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
        => throw new InvalidOperationException(Message);
}

#pragma warning restore RS1001, RS2008

#endregion
