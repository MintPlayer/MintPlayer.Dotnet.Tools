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
    /// Registers one output per <see cref="Producer"/> in each of <paramref name="providers"/>, for a generator
    /// that emits a variable number of files. Each producer caches independently.
    /// </summary>
    /// <param name="context">context parameter from the <see cref="IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext)"/> method</param>
    /// <param name="providers">The producers to be registered; every producer across all of them must have a distinct <see cref="Producer.Filename"/></param>
    /// <remarks>
    /// Each provider gets its own output step. They are deliberately not merged into one: joining two
    /// <see cref="IncrementalValuesProvider{TValues}"/> needs <c>Collect()</c>, and a collected array changes
    /// whenever any one producer does, which would regenerate every file on every change.
    /// </remarks>
    public static void ProduceCode(this IncrementalGeneratorInitializationContext context, params IncrementalValuesProvider<Producer>[] providers)
    {
        foreach (var provider in providers)
            context.RegisterSourceOutput(provider, static (c, p) => p?.Produce(c));
    }


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
                static (c, p) => ReportSafely(c, p.Left, p.Right));
        }
    }

    /// <summary>
    /// Reports every <see cref="DiagnosticInfo"/> in <paramref name="providers"/>, e.g. from
    /// <see cref="DiscoveryExtensions.Diagnostics{TModel}"/>.
    /// </summary>
    /// <param name="context">context parameter from the <see cref="IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext)"/> method</param>
    /// <param name="providers">The diagnostics to report</param>
    /// <remarks>
    /// Each diagnostic is combined with the compilation on its own, to map its <see cref="LocationKey"/> back onto the
    /// compilation's tree (<c>#pragma</c> and <c>.editorconfig</c> severity depend on that). With no diagnostics there
    /// is nothing to combine, so no step runs on an edit. One diagnostic that cannot be reported never stops the
    /// others; see <see cref="UnreportableDiagnostic"/>.
    /// </remarks>
    public static void ReportDiagnostics(this IncrementalGeneratorInitializationContext context, params IncrementalValuesProvider<DiagnosticInfo>[] providers)
    {
        foreach (var provider in providers)
        {
            context.RegisterSourceOutput(
                provider.Combine(context.CompilationProvider),
                static (c, p) => ReportSafely(c, p.Left, p.Right));
        }
    }

    /// <summary>
    /// Reported instead of a diagnostic that could not be reported, for instance because its location points into a
    /// tree the compilation doesn't contain, so that it is not lost silently and does not crash the generator
    /// (CS8785). A warning: the diagnostic it replaces may have been anything.
    /// </summary>
    public static readonly DiagnosticDescriptor UnreportableDiagnostic = new(
        id: "MPSG002",
        title: "A source generator diagnostic could not be reported",
        messageFormat: "The source generator could not report diagnostic {0}: {1}: {2}",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static void ReportSafely(SourceProductionContext context, DiagnosticInfo? info, Compilation compilation)
    {
        if (info is null) return;
        Diagnostic diagnostic;
        try
        {
            diagnostic = info.ToDiagnostic(compilation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportUnreportable(context, info.Rule.Id, ex);
            return;
        }

        ReportSafely(context, diagnostic, () => info.ToDiagnostic(null));
    }

    private static void ReportSafely(SourceProductionContext context, IDiagnosticReporter reporter, Compilation compilation)
    {
        IEnumerator<Diagnostic> enumerator;
        try
        {
            enumerator = (reporter.GetDiagnostics(compilation) ?? []).GetEnumerator();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportUnreportable(context, reporter.GetType().Name, ex);
            return;
        }

        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext()) return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A reporter's lazy enumeration cannot continue after it threw.
                    ReportUnreportable(context, reporter.GetType().Name, ex);
                    return;
                }

                var diagnostic = enumerator.Current;
                if (diagnostic is not null)
                    ReportSafely(context, diagnostic, retry: null);
            }
        }
    }

    /// <summary>Reports one diagnostic; when that throws, retries it without a location, then falls back to MPSG002.</summary>
    private static void ReportSafely(SourceProductionContext context, Diagnostic diagnostic, Func<Diagnostic>? retry)
    {
        try
        {
            context.ReportDiagnostic(diagnostic);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (retry is not null)
            {
                try
                {
                    context.ReportDiagnostic(retry());
                    return;
                }
                catch (Exception retryException) when (retryException is not OperationCanceledException) { }
            }
            ReportUnreportable(context, diagnostic.Id, ex);
        }
    }

    private static void ReportUnreportable(SourceProductionContext context, string what, Exception ex)
    {
        try
        {
            context.ReportDiagnostic(Diagnostic.Create(UnreportableDiagnostic, Location.None, what, ex.GetType().Name, ex.Message));
        }
        catch (Exception) { }
    }
}
