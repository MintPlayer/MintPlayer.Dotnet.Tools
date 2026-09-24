using Microsoft.CodeAnalysis;

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
    public static void ReportDiagnostics(this IncrementalGeneratorInitializationContext context, params IncrementalValueProvider<IDiagnosticReporter>[] providers)
    {
        switch (providers.Length)
        {
            case 0: return;
            case 1:
                //context.RegisterSourceOutput(providers[0], static (c, d) => c.ReportDiagnostic(d.GetDiagnostics()));
                context.RegisterSourceOutput(context.CompilationProvider
                    .Combine(providers[0])
                    .Select(static (p, ct) => p.Right.GetDiagnostics(p.Left)),
                    static (c, d) => c.ReportDiagnostic(d));
                return;
        }

        var sourceProvider = providers[0]
            .Combine(providers[1])
            .SelectMany(static (p, ct) => new[] { p.Left, p.Right });

        for (int i = 2; i < providers.Length; i++)
        {
            sourceProvider = sourceProvider
                .Collect()
                .Combine(providers[i])
                .SelectMany(static (p, ct) => p.Left.Concat([p.Right]));
        }

        var sourceAndCompilationProvider = context.CompilationProvider
            .Combine(sourceProvider.Collect())
            .Select(static (p, ct) => p.Right.Select(rep => (compilation: p.Left, reporter: rep)));

        context.RegisterSourceOutput(sourceAndCompilationProvider, static (c, cd) => c.ReportDiagnostic(cd.SelectMany(d => d.reporter.GetDiagnostics(d.compilation))));
    }
}
