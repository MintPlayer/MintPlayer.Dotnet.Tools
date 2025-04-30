using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.Extensions;

namespace MintPlayer.SourceGenerators.Tools;

public static class GeneratorExtensions
{
    /// <summary>
    /// Call this method with all <see cref="IncrementalValueProvider{Producer}" /> you want to register.
    /// </summary>
    /// <param name="context">context parameter from the <see cref="IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext)"/> method</param>
    /// <param name="providers">All the source providers to be registered</param>
    public static void ProduceCode(this IncrementalGeneratorInitializationContext context, params IncrementalValueProvider<Producer>[] providers)
    {
        switch (providers.Length)
        {
            case 0: return;
            case 1:
                context.RegisterSourceOutput(providers[0], static (c, g) => g?.Produce(c));
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

        context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
    }

    public static void ProduceCode(this IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Producer[]> providers)
    {
        context.RegisterSourceOutput(providers, static (c, g) =>
        {
            foreach (var item in g)
                item?.Produce(c);
        });
    }


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
                context.RegisterSourceOutput(providers[0], static (c, d) => c.ReportDiagnostic(d.GetDiagnostics()));
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

        context.RegisterSourceOutput(sourceProvider, static (c, d) => c.ReportDiagnostic(d.GetDiagnostics()));
    }
}
