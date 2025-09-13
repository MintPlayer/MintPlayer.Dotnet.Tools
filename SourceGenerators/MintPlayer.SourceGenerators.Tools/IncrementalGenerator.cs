using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

public abstract partial class IncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        RegisterComparers();

        var config = context.AnalyzerConfigOptionsProvider
            .Select(static (p, ct) => Settings.FromGlobalOptions(p.GlobalOptions))
            .WithComparer(SettingsValueComparer.Instance);

        Initialize(context, config);
    }

    public abstract void RegisterComparers();

    public abstract void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}
