using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class ReadonlyClassGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var config = context.AnalyzerConfigOptionsProvider
                .Select(static (p, ct) =>
                {
                    p.GlobalOptions.TryGetValue("build_property.rootnamespace", out var rootNamespace);
                    return new Settings
                    {
                        RootNamespace = rootNamespace,
                    };
                })
                .WithComparer(SettingsValueComparer.Instance);

            var propertyDeclarationsProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) => node is PropertyDeclarationSyntax propertyDeclaration,
                    static (context2, ct) =>
                    {
                        if (context2.Node is PropertyDeclarationSyntax propertyDeclaration &&
                            context2.SemanticModel.GetDeclaredSymbol(propertyDeclaration, ct) is IPropertySymbol propertySymbol)
                        {
                            if (propertyDeclaration.Parent is ClassDeclarationSyntax classDeclaration)
                            {
                                var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);
                                switch (classDeclaration.Parent)
                                {
                                    case NamespaceDeclarationSyntax namespaceDeclaration:
                                        return new Models.PropertyDeclaration
                                        {
                                            Namespace = namespaceDeclaration.Name.ToString(),
                                            FQClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            ClassName = classSymbol.Name,
                                            PropertyName = propertySymbol.Name,
                                            FQPropertyType = propertySymbol.Type.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                        };
                                    case FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration:
                                        return new Models.PropertyDeclaration
                                        {
                                            Namespace = fileScopedNamespaceDeclaration.Name.ToString(),
                                            FQClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            ClassName = classSymbol.Name,
                                            PropertyName = propertySymbol.Name,
                                            FQPropertyType = propertySymbol.Type.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                        };
                                    default:
                                        return new Models.PropertyDeclaration
                                        {
                                            Namespace = null,
                                            FQClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            ClassName = classSymbol.Name,
                                            PropertyName = propertySymbol.Name,
                                            FQPropertyType = propertySymbol.Type.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                        };
                                }
                            }
                        }

                        return default;
                    }
                )
                //.WithComparer(ValueComparers.FieldDeclarationComparer.Instance)
                .Collect();

            var x = propertyDeclarationsProvider
                //.Select(p => "");
                .Combine(config)
                .Select(static (p, c) => "");
        }
    }
}
