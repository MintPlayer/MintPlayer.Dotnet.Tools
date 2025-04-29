using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

public abstract partial class IncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        RegisterComparers();

        var configProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (p, ct) =>
            {
                p.GlobalOptions.TryGetValue("build_property.rootnamespace", out var rootNamespace);
                return new
                {
                    RootNamespace = rootNamespace,
                };
            });

        var compilationInfoProvider = context.CompilationProvider
            .Select(static (comp, ct) =>
            {
                var ivpSymbol = comp.GetTypeByMetadataName(typeof(IncrementalValuesProvider<>).FullName)
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None));

                return new
                {
                    IncrementalValueProviderSymbol = ivpSymbol,
                };
            });

        var settingsProvider = configProvider
            .Combine(compilationInfoProvider)
            .Select(static (p, ct) =>
            {
                var rootNamespace = p.Left.RootNamespace;
                var ivpSymbol = p.Right.IncrementalValueProviderSymbol;
                return new Settings
                {
                    RootNamespace = rootNamespace,
                    IncrementalValueProviderSymbol = ivpSymbol,
                };
            })
            .WithComparer(SettingsValueComparer.Instance);

        Initialize(context, settingsProvider);
    }

    public abstract void RegisterComparers();

    public abstract void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}
