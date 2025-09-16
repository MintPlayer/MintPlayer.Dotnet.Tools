using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

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
            static uint? (context, ct) =>
            {
                var attributeData = context.Attributes.FirstOrDefault(ad =>
                    ad.AttributeClass?.ToDisplayString() == typeof(GenerateJoinMethodsAttribute).FullName);
                if (attributeData == null)
                    return null;
                if (attributeData.ConstructorArguments.Length != 1)
                    return null;
                var numberOfParameters = attributeData.ConstructorArguments[0].Value;
                if (numberOfParameters is not uint numParams)
                    return null;
                return numParams;
            })
            .Collect()
            .Select(static (numParams, ct) => numParams.FirstOrDefault() ?? 5u);

        var numberOfJoinMethodsSourceProvider = numberOfJoinMethodsProvider
            .Join(settingsProvider)
            .Select(static Producer (provider, ct) => new JoinMethodProducer(provider.Item1, provider.Item2.RootNamespace));

        context.ProduceCode(numberOfJoinMethodsSourceProvider);
    }
}
