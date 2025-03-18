using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class DescriptionsSourceGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var methodDeclarationsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) => node is MethodDeclarationSyntax,
                static (context2, ct) =>
                {
                    if (context2.Node is MethodDeclarationSyntax methodDeclaration
                        && methodDeclaration.Parent is ClassDeclarationSyntax classDeclaration
                        && context2.SemanticModel.GetDeclaredSymbol(methodDeclaration) is IMethodSymbol methodSymbol
                        && context2.SemanticModel.GetDeclaredSymbol(classDeclaration) is INamedTypeSymbol classSymbol
                        && classDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
                    {
                        var trivia = methodDeclaration.GetLeadingTrivia()
                            .Where(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia))
                            .ToArray();

                        if (trivia.Length == 0) return default;

                        var xmlText = string.Join(string.Empty, trivia.Select(t => t.ToFullString())
                            .Where(t => t.StartsWith("/// "))
                            .Select(t => t.Substring(4)));
                        var xmlDoc = System.Xml.Linq.XDocument.Parse($"<root>{xmlText}</root>");

                        var summaryNode = xmlDoc.Descendants("summary").FirstOrDefault();
                        if (summaryNode is null) return default;

                        var className = classSymbol.Name;

                        return new XmlMarkup
                        {
                            Text = summaryNode?.Value.Trim(),
                            MethodName = methodSymbol.Name,
                            MethodGenericParameters = methodSymbol.TypeParameters.Select(p => p.Name).ToArray(),
                            ReturnType = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)),
                            ClassName = className,
                            ClassGenericParameters = classSymbol.TypeParameters.Select(p => p.Name).ToArray(),
                            Namespace = methodSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                            MethodParameters = methodSymbol.Parameters.Select(p => new XmlMarkupParameter
                            {
                                Name = p.Name,
                                Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters).WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included))
                            }).ToArray(),
                            MethodAccessModifiers = methodDeclaration.Modifiers,
                        };
                    }

                    return default;
                }
            )
            .Where(d => d is not null)
            .Collect();

        var methodDeclarationsSourceProvider = methodDeclarationsProvider
            .Combine(settingsProvider)
            .Select(static Producer (p, ct) => new Producers.MethodDescriptionProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

        // Combine all Source Providers
        context.ProduceCode(methodDeclarationsSourceProvider);
    }
}
