using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tools.Models;

namespace MintPlayer.SourceGenerators.Tools;

public abstract partial class IncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // AnalyzerInfo, LangVersion and Settings implement IEquatable<T>, so every step below compares by
        // value under the default comparer Roslyn uses; no explicit comparer is needed.
        var analyzerInfo = context.AnalyzerConfigOptionsProvider
            .Select(static (p, ct) => AnalyzerInfo.FromGlobalOptions(p.GlobalOptions));

        // Read from the parse options, not by walking CompilationProvider.SyntaxTrees: every tree of a
        // project shares them, and the compilation is a new object on every edit, so the walk re-ran on
        // every keystroke to produce the same answer.
        var languageVersionProvider = context.ParseOptionsProvider
            .SelectMany(static (p, ct) => new[] { p }.OfType<CSharpParseOptions>()
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
            .Collect()
            .Select(static (p, ct) => p.OrderBy(x => x.Weight).FirstOrDefault());

        var settingsProvider = analyzerInfo
            .Combine(languageVersionProvider)
            .Select(static (p, ct) => Settings.FromAnalyzerAndLangVersion(p.Left, p.Right));

        Initialize(context, settingsProvider);
    }

    public abstract void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}
