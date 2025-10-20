using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tools.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools;

public abstract partial class IncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1) Flow the Compilation as a handle to the cache
        var cacheProvider = context.CompilationProvider
            .Select(static (compilation, _) => ComparerCacheHub.Get(compilation))
            .WithComparer(ReferenceEqualityComparer<PerCompilationCache>.Instance);


        var analyzerInfo = context.AnalyzerConfigOptionsProvider
            .Select(static (p, ct) => AnalyzerInfo.FromGlobalOptions(p.GlobalOptions))
            .WithComparer(ComparerRegistry.For<AnalyzerInfo>());

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
            .WithComparer(ComparerRegistry.For<LangVersion>())
            .Collect()
            .Select(static (p, ct) => p.OrderBy(x => x.Weight).FirstOrDefault())
            .WithComparer(ComparerRegistry.For<LangVersion>());

        var settingsProvider = analyzerInfo
            .Combine(languageVersionProvider)
            .Select(static (p, ct) => Settings.FromAnalyzerAndLangVersion(p.Left, p.Right))
            .WithComparer(ComparerRegistry.For<Settings>());

        Initialize(context, settingsProvider, cacheProvider);
    }

    public abstract void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<PerCompilationCache> valueComparerCacheProvider);
}
