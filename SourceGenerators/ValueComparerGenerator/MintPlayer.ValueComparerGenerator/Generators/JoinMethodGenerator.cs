using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.ValueComparerGenerator.Attributes;
using MintPlayer.ValueComparerGenerator.Models;

namespace MintPlayer.ValueComparerGenerator.Generators;

[Generator(LanguageNames.CSharp)]
public class JoinMethodGenerator : IncrementalGenerator
{
    //public override void RegisterComparers()
    //{
    //}

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<PerCompilationCache> cacheProvider)
    {
        var numberOfJoinMethodsProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            typeof(GenerateJoinMethodsAttribute).FullName,
            static (node, ct) => node is Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax,
            static (context, ct) =>
            {
                var attributeData = context.Attributes.FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString() == typeof(GenerateJoinMethodsAttribute).FullName);
                if (attributeData is { ConstructorArguments.Length: 1 } && attributeData.ConstructorArguments[0].Value is uint numParamsArg)
                    return numParamsArg;

                return 5u;
            })
            .WithComparer(ValueComparer<uint>.Instance)
            .Collect()
            .Select((x, ct) => x.Any() ? x.First() : 5u);

        var hasCodeAnalysisReferenceProvider = context.CompilationProvider
            .Select(static (compilation, ct) => compilation.ReferencedAssemblyNames
                .Any(a => a.Name == "Microsoft.CodeAnalysis"));

        var joinMethodsSourceProvider = numberOfJoinMethodsProvider
            .Join(hasCodeAnalysisReferenceProvider)
            .Join(settingsProvider)
            .Select(static Producer (provider, ct) => new JoinMethodProducer(provider.Item1, provider.Item2, provider.Item3.RootNamespace!));

        context.ProduceCode(joinMethodsSourceProvider);
    }
}
