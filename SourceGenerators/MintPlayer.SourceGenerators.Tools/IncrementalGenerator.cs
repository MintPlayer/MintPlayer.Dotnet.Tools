using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tools.Models;

namespace MintPlayer.SourceGenerators.Tools;

public abstract partial class IncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        RegisterComparers();

        var analyzerInfo = context.AnalyzerConfigOptionsProvider
            .Select(static (p, ct) => AnalyzerInfo.FromGlobalOptions(p.GlobalOptions))
            .WithComparer(AnalyzerInfoComparer.Instance);

        var languageVersionProvider = context.CompilationProvider
            .SelectMany(static (p, ct) => p.SyntaxTrees.Select(t => t.Options).OfType<CSharpParseOptions>()
                .Select((po) =>
                {
                    switch (po.LanguageVersion)
                    {
                        case LanguageVersion.LatestMajor:
                        case LanguageVersion.Preview:
                        case LanguageVersion.Latest:
                            return new LangVersion
                            {
                                LanguageVersion = po.LanguageVersion,
                                Weight = (int)po.LanguageVersion,
                            };
                        case LanguageVersion.Default:
                            return new LangVersion
                            {
                                LanguageVersion = po.LanguageVersion,
                                Weight = (int)LanguageVersion.Latest,
                            };
                    }

                    var intVersion = (int)po.LanguageVersion;
                    if (intVersion <= 7)
                    {
                        return new LangVersion
                        {
                            LanguageVersion = po.LanguageVersion,
                            Weight = intVersion * 100,
                        };
                    }
                    else
                    {
                        return new LangVersion
                        {
                            LanguageVersion = po.LanguageVersion,
                            Weight = intVersion,
                        };
                    }
                }))
            .WithComparer(LangVersionComparer.Instance)
            .Collect()
            .Select(static (p, ct) => p.OrderBy(x => x.Weight).FirstOrDefault())
            .WithComparer(LangVersionComparer.Instance);

        var settingsProvider = analyzerInfo
            .Combine(languageVersionProvider)
            .Select(static (p, ct) => Settings.FromAnalyzerAndLangVersion(p.Left, p.Right))
            .WithComparer(SettingsValueComparer.Instance);

        Initialize(context, settingsProvider);
    }

    public abstract void RegisterComparers();

    public abstract void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}
