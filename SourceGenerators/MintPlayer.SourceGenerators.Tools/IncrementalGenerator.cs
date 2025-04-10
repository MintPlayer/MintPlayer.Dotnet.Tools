using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.Extensions;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools;

public abstract partial class IncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatorName = GetType().Name; // Determine class name
        var editorConfigField = $"disable_{generatorName.SwitchCasing(ECasingStyle.Pascal, ECasingStyle.Snake)}";
        var config = context.AnalyzerConfigOptionsProvider
            .Select((p, ct) =>
            {
                p.GlobalOptions.TryGetValue("build_property.rootnamespace", out var rootNamespace);
                p.GlobalOptions.TryGetValue(editorConfigField, out var disable);
                return new Settings
                {
                    RootNamespace = rootNamespace,
                    Disable = bool.TrueString.Equals(disable, StringComparison.OrdinalIgnoreCase),
                };
            })
            .WithComparer(SettingsValueComparer.Instance);

        Initialize(context, config);
    }

    public abstract void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}
