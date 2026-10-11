using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;

namespace MintPlayer.ValueComparerGenerator.Generators;

[Generator(LanguageNames.CSharp)]
public class JoinMethodGenerator : IncrementalGenerator
{
    /// <summary>By metadata name, so the lookup does not depend on the attribute assembly the generator was built against (P24).</summary>
    private const string GenerateJoinMethodsMetadataName = Discovery.AttributesNamespace + ".GenerateJoinMethodsAttribute";

    //public override void RegisterComparers()
    //{
    //}

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var numberOfJoinMethodsProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            GenerateJoinMethodsMetadataName,
            static (node, ct) => node is Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax,
            static (context, ct) =>
            {
                var attributeData = context.Attributes.FirstOrDefault(ad => ad.AttributeClass.IsAttribute(GenerateJoinMethodsMetadataName));
                return attributeData.GetArgument("numberOfParameters", 5u);
            })
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
