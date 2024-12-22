using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

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

        Initialize(context, config);
    }

    public abstract void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}
