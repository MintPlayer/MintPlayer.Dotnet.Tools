using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

public static class GeneratorExtensions
{
    /// <summary>
    /// Similar to <see cref="IncrementalValueProviderExtensions.Select{TSource, TResult}(IncrementalValueProvider{TSource}, Func{TSource, CancellationToken, TResult})"/>, but always returns a <see cref="Producer"/>
    /// </summary>
    /// <typeparam name="TValue">Selector type</typeparam>
    /// <typeparam name="TProducer">Producer type</typeparam>
    /// <param name="provider">Value provider</param>
    /// <param name="selector">Mapping function that returns a <see cref="Producer"/></param>
    /// <returns></returns>
    public static IncrementalValueProvider<Producer> WithProducer<TValue, TProducer>(this IncrementalValueProvider<TValue> provider, Func<TValue, CancellationToken, TProducer> selector)
        where TProducer : Producer
        => provider.Select<TValue, Producer>((p, ct) => selector(p, ct) as Producer);

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

    /// <summary>
    /// Call this method with all <see cref="IncrementalValueProvider{ImmutableArrayOfDiagnostic}" /> you want to register.
    /// </summary>
    /// <param name="context">context parameter from the <see cref="IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext)"/> method</param>
    /// <param name="providers">All the diagnostic providers to be registered</param>
    internal static void ReportDiagnostics(this IncrementalGeneratorInitializationContext context, params IncrementalValueProvider<Diagnostic>[] providers)
    {
        switch (providers.Length)
        {
            case 0: return;
            case 1:
                context.RegisterSourceOutput(providers[0], static (c, d) => c.ReportDiagnostic(d));
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

        context.RegisterSourceOutput(sourceProvider, static (c, d) => c.ReportDiagnostic(d));
    }
}
