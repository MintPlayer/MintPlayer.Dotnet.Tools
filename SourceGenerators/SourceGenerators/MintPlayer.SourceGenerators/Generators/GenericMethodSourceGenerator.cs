using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class GenericMethodSourceGenerator : IncrementalGenerator
{
    //public override void RegisterComparers()
    //{
    //    NewtonsoftJsonComparers.Register();
    //}

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
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

                        if (attributeSyntax is { Attribute.ArgumentList.Arguments.Count: > 0 } && int.TryParse(attributeSyntax.Attribute.ArgumentList.Arguments[0].Expression.ToFullString(), out var countValue))
                        {
                            var pathSpec = classSymbol.GetPathSpec(ct);
                            return new Models.GenericMethodDeclaration
                            {
                                Method = new Models.MethodDeclaration
                                {
                                    MethodName = symbol.Name,
                                    ClassName = classSymbol.Name,
                                    ClassIsPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                    ClassIsStatic = classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword),
                                    MethodIsPrivate = methodDeclaration.Modifiers.Any(SyntaxKind.PrivateKeyword),
                                    MethodIsPartial = methodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                    MethodIsStatic = methodDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword),
                                    ContainingNamespace = pathSpec?.ContainingNamespace,
                                    PathSpec = pathSpec,
                                },
                                Count = countValue,
                            };
                        }
                    }
                }

                return default;
            })
            // The transform allocates a new model on every run; without the generated comparer the
            // collected array compared by reference and never matched the previous one.
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
