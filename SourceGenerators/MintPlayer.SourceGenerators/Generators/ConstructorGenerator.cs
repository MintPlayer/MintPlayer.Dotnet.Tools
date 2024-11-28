using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class ConstructorGenerator : IIncrementalGenerator
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

            //var classDeclarationsProvider = context.SyntaxProvider
            //    .CreateSyntaxProvider(
            //        static (node, ct) =>
            //        {
            //            return node is ClassDeclarationSyntax { } classDeclaration;
            //        },
            //        static (context, ct) =>
            //        {
            //            if (context.Node is ClassDeclarationSyntax classDeclaration &&
            //                context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol symbol)
            //            {
            //                return new Models.ClassDeclaration
            //                {
            //                    Name = symbol.Name,
            //                };
            //            }
            //            else
            //            {
            //                return default;
            //            }
            //        }
            //    )
            //    .WithComparer(ValueComparers.ClassDeclarationValueComparer.Instance)
            //    .Collect();

            var fieldDeclarationsProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) => node is FieldDeclarationSyntax { AttributeLists.Count: > 0 } fieldDeclaration
                        && fieldDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword),
                    static (context2, ct) =>
                    {
                        if (context2.Node is FieldDeclarationSyntax fieldDeclaration &&
                            fieldDeclaration.Declaration.Variables.Count == 1 &&
                            fieldDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword) &&
                            context2.SemanticModel.GetDeclaredSymbol(fieldDeclaration.Declaration.Variables[0], ct) is IFieldSymbol fieldSymbol)
                        {
                            if (fieldDeclaration.Parent is ClassDeclarationSyntax classDeclaration)
                            {
                                var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);
                                switch (classDeclaration.Parent)
                                {
                                    case NamespaceDeclarationSyntax namespaceDeclaration:
                                        return new Models.FieldDeclaration
                                        {
                                            Namespace = namespaceDeclaration.Name.ToString(),
                                            FullyQualifiedClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            ClassName = classSymbol.Name,
                                            Name = fieldSymbol.Name,
                                            FullyQualifiedTypeName = fieldSymbol.Type.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            Type = fieldSymbol.Type.Name,
                                            Attributes = fieldSymbol.GetAttributes().Select(a => new Models.Attribute
                                            {
                                                AttributeType = a.AttributeClass?.ToDisplayString(new SymbolDisplayFormat(
                                                    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                                )),
                                            }).ToArray()
                                        };
                                    case FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration:
                                        return new Models.FieldDeclaration
                                        {
                                            Namespace = fileScopedNamespaceDeclaration.Name.ToString(),
                                            FullyQualifiedClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            ClassName = classSymbol.Name,
                                            Name = fieldSymbol.Name,
                                            FullyQualifiedTypeName = fieldSymbol.Type.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            Type = fieldSymbol.Type.Name,
                                            Attributes = fieldSymbol.GetAttributes().Select(a => new Models.Attribute
                                            {
                                                AttributeType = a.AttributeClass?.ToDisplayString(new SymbolDisplayFormat(
                                                    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                                )),
                                            }).ToArray(),
                                        };
                                    default:
                                        return new Models.FieldDeclaration
                                        {
                                            Namespace = null,
                                            FullyQualifiedClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            ClassName = classSymbol.Name,
                                            Name = fieldSymbol.Name,
                                            FullyQualifiedTypeName = fieldSymbol.Type.ToDisplayString(new SymbolDisplayFormat(
                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                            )),
                                            Type = fieldSymbol.Type.Name,
                                            Attributes = fieldSymbol.GetAttributes().Select(a => new Models.Attribute
                                            {
                                                AttributeType = a.AttributeClass?.ToDisplayString(new SymbolDisplayFormat(
                                                    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                                                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                                                )),
                                            }).ToArray(),
                                        };
                                }
                            }
                        }

                        return default;
                    }
                )
                .WithComparer(ValueComparers.FieldDeclarationComparer.Instance);
            //.Collect();

            var classDeclarationProvider = fieldDeclarationsProvider
                .Collect()
                .Select((f, ct) => f
                    .GroupBy(fd => fd!.FullyQualifiedClassName)
                    .Select(g => new
                    {
                        FullyQualifiedClassName = g.Key,
                        Fields = g.Select(field => new
                        {
                            field.Name,
                            field.FullyQualifiedTypeName,
                        })
                    }));

            var baseConstructorAttributesProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 } classDeclaration
                        && classDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword),
                    static (context2, ct) =>
                    {
                        if (context2.Node is ClassDeclarationSyntax classDeclaration &&
                            context2.SemanticModel.GetDeclaredSymbol(classDeclaration) is INamedTypeSymbol classSymbol &&
                            classSymbol.GetAttributes().Where(a => a.AttributeClass.Name is nameof(BaseConstructorParameterAttribute)).ToArray() is { Length: > 0 } ctorAttributes)
                        {
                            return new 
                        }
                    }
        }
    }
}
