using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools;

public static class GeneratorExtensions
{
    /// <summary>
    /// Call this method with all <see cref="IncrementalValueProvider{Producer}" /> you want to register.
    /// </summary>
    /// <param name="context">context parameter from the <see cref="IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext)"/> method</param>
    /// <param name="providers">All the source providers to be registered</param>
    /// <remarks>
    /// Each provider gets its own output step, and none of them sees the <see cref="Compilation"/>.
    /// This used to combine every producer with <c>context.CompilationProvider</c> and funnel them all
    /// into one output step. A <see cref="Compilation"/> is a new object on every edit, so every file of
    /// every generator was regenerated on every keystroke — and the compilation was never even read.
    /// Now an output step re-runs only when its own producer changed.
    /// </remarks>
    public static void ProduceCode(this IncrementalGeneratorInitializationContext context, params IncrementalValueProvider<Producer>[] providers)
    {
        foreach (var provider in providers)
            context.RegisterSourceOutput(provider, static (c, p) => p?.Produce(c));
    }

    /// <summary>
    /// Registers one output per <see cref="Producer"/> in <paramref name="providers"/>, for a generator that
    /// emits a variable number of files. Each producer caches independently.
    /// </summary>
    /// <param name="context">context parameter from the <see cref="IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext)"/> method</param>
    /// <param name="providers">The producers to be registered; each must have a distinct <see cref="Producer.Filename"/></param>
    public static void ProduceCode(this IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<Producer> providers)
        => context.RegisterSourceOutput(providers, static (c, p) => p?.Produce(c));


    /// <summary>
    /// Call this method with all <see cref="IncrementalValueProvider{ImmutableArrayOfDiagnostic}" /> you want to register.
    /// </summary>
    /// <param name="context">context parameter from the <see cref="IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext)"/> method</param>
    /// <param name="providers">All the diagnostic providers to be registered</param>
    /// <remarks>
    /// The compilation is still needed here — it turns stored locations back into in-tree ones, which
    /// <c>#pragma</c> and <c>.editorconfig</c> severity depend on — but only by a reporter that has
    /// something to report. An <see cref="IConditionalDiagnosticReporter"/> with
    /// <see cref="IConditionalDiagnosticReporter.HasDiagnostics"/> false is filtered out before the
    /// combine, and a combine over zero values runs nothing. Any other reporter is combined with the
    /// compilation as before, and so re-runs on every edit.
    /// </remarks>
    public static void ReportDiagnostics(this IncrementalGeneratorInitializationContext context, params IncrementalValueProvider<IDiagnosticReporter>[] providers)
    {
        foreach (var provider in providers)
        {
            var reportersWithWork = provider.SelectMany(static (r, ct) =>
                r is null || r is IConditionalDiagnosticReporter { HasDiagnostics: false }
                    ? ImmutableArray<IDiagnosticReporter>.Empty
                    : ImmutableArray.Create(r));

            context.RegisterSourceOutput(
                reportersWithWork.Combine(context.CompilationProvider),
                static (c, p) => c.ReportDiagnostic(p.Left.GetDiagnostics(p.Right)));
        }
    }
}
