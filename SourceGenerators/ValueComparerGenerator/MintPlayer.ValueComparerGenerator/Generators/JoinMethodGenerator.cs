using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;
using MintPlayer.ValueComparerGenerator.Models;

namespace MintPlayer.ValueComparerGenerator.Generators;

[Generator(LanguageNames.CSharp)]
public class JoinMethodGenerator : IncrementalGenerator
{
    public override void RegisterComparers()
    {
    }

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var numberOfJoinMethodsProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            typeof(GenerateJoinMethodsAttribute).FullName,
            static (node, ct) => node is Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax,
            static (context, ct) =>
            {
                var hasCodeAnalysisReferenceProvider = context.SemanticModel.Compilation.ReferencedAssemblyNames
                    .Any(a => a.Name == "Microsoft.CodeAnalysis");


                var attributeData = context.Attributes.FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString() == typeof(GenerateJoinMethodsAttribute).FullName);
                if (attributeData is { ConstructorArguments.Length: 1 } && attributeData.ConstructorArguments[0].Value is uint numParamsArg)
                {
                    return new JoinMethodInfo { HasCodeAnalysisReference = hasCodeAnalysisReferenceProvider, NumberOfJoinMethods = numParamsArg };
                }

                return new JoinMethodInfo { HasCodeAnalysisReference = hasCodeAnalysisReferenceProvider };
            })
            .WithComparer(JoinMethodInfoComparer.Instance)
            .Collect();


        var numberOfJoinMethodsSourceProvider = numberOfJoinMethodsProvider
            .Join(settingsProvider)
            .Select(static Producer (provider, ct) => new JoinMethodProducer(provider.Item1.Single(), provider.Item2.RootNamespace!));

        context.ProduceCode(numberOfJoinMethodsSourceProvider);
    }
}
