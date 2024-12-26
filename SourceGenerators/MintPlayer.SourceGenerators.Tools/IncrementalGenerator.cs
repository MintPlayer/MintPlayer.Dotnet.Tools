using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools;

public abstract class IncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var config = context.AnalyzerConfigOptionsProvider
            .Select(static (p, ct) =>
            {
                p.GlobalOptions.TryGetValue("build_property.rootnamespace", out var rootNamespace);
                return new Settings
                {
                    RootNamespace = rootNamespace,
                };
            })
            .WithComparer(SettingsValueComparer.Instance);

        Setup(context, config);

        var providers = new[] { classNamesSourceProvider, classNameListSourceProvider1, classNameListSourceProvider2, classNameListSourceProvider3 };
        var sourceProvider = providers[0].SelectMany(static (p, _) => new ImmutableArray<Producer> { p });
        for (var i = 1; i < providers.Length; i++)
        {
            sourceProvider = sourceProvider
                .Collect()
                .Combine(providers[i])
                .SelectMany(static (p, _) => p.Left.Concat([p.Right]));
        }

        // Generate Code
        context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
    }

    public abstract IncrementalValuesProvider<Producer>[] Setup(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}
