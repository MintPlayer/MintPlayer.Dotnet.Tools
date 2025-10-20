using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.ValueComparers.NewtonsoftJson;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class GenericMethodSourceGenerator : IncrementalGenerator
{
    //public override void RegisterComparers()
    //{
    //    NewtonsoftJsonComparers.Register();
    //}

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<PerCompilationCache> cacheProvider)
    {
        var methodsProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, ct) =>
            {
                return node is MethodDeclarationSyntax { AttributeLists.Count: > 0 } methodDeclaration;
            },
            static (context, ct) =>
            {
                if (context.Node is MethodDeclarationSyntax methodDeclaration)
                {
                    var x = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, ct);
                    if (x is IMethodSymbol symbol)
                    {
                        var attr = symbol.GetAttributes();
                        var classDeclaration = (ClassDeclarationSyntax)methodDeclaration.Parent;
                        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct);

                        var attributeSyntax = methodDeclaration.AttributeLists.SelectMany(l => l.Attributes).OfType<AttributeSyntax>()
                            .Select(a => new
                            {
                                Attribute = a,
                                Type = context.SemanticModel.GetTypeInfo(a, ct).ConvertedType
                            })
                            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.Type, context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(GenericMethodAttribute).FullName)));

                        if (int.TryParse(attributeSyntax.Attribute.ArgumentList.Arguments[0].Expression.ToFullString(), out var countValue))
                        {
                            return new Models.GenericMethodDeclaration
                            {
                                Method = new Models.MethodDeclaration
                                {
                                    MethodName = symbol.Name,
                                    ClassName = classSymbol.Name,
                                    MethodModifiers = methodDeclaration.Modifiers,
                                    ClassModifiers = classDeclaration.Modifiers,
                                    ContainingNamespace = classSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    //ClassIsPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                    //MethodIsPartial = methodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                    //ClassIsStatic = classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword),
                                    //MethodIsStatic = methodDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword),
                                    //GenericMethodAttribute = 
                                },
                                Count = countValue,
                            };
                        }
                    }
                }

                return default;
            })
            .Collect();

        var methodsSourceProvider = methodsProvider
            .Join(settingsProvider)
            .Select(static Producer (providers, ct) => new GenericMethodProducer(providers.Item1.NotNull(), providers.Item2.RootNamespace!));

        context.ProduceCode(methodsSourceProvider);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GenericMethodAttribute : Attribute
{
    public uint Count { get; set; } = 1;
    public Type? Transformer { get; set; }
}
