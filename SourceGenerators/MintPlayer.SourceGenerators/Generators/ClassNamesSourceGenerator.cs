//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
//using MintPlayer.SourceGenerators.Tools;
//using MintPlayer.SourceGenerators.Tools.ValueComparers;
//using System;
//using System.Collections.Generic;
//using System.Collections.Immutable;
//using System.Linq;
//using System.Text;

//namespace MintPlayer.SourceGenerators.Generators
//{
//    [Generator(LanguageNames.CSharp)]
//    public class ClassNamesSourceGenerator : IIncrementalGenerator
//    {
//        public ClassNamesSourceGenerator()
//        {
//        }

//        public void Initialize(IncrementalGeneratorInitializationContext context)
//        {
//            var config = context.AnalyzerConfigOptionsProvider
//                .Select(static (p, ct) =>
//                {
//                    p.GlobalOptions.TryGetValue("build_property.rootnamespace", out var rootNamespace);
//                    return new Settings
//                    {
//                        RootNamespace = rootNamespace,
//                    };
//                })
//                .WithComparer(SettingsValueComparer.Instance);

//            var classDeclarationsProvider = context.SyntaxProvider
//                .CreateSyntaxProvider(
//                    static (node, ct) =>
//                    {
//                        return node is ClassDeclarationSyntax { } classDeclaration;
//                    },
//                    static (context, ct) =>
//                    {
//                        if (context.Node is ClassDeclarationSyntax classDeclaration &&
//                            context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol symbol)
//                        {
//                            return new Models.ClassDeclaration
//                            {
//                                Name = symbol.Name,
//                            };
//                        }
//                        else
//                        {
//                            return default;
//                        }
//                    }
//                )
//                .WithComparer(ValueComparers.ClassDeclarationValueComparer.Instance)
//                .Collect();

//            var fieldDeclarationsProvider = context.SyntaxProvider
//                .CreateSyntaxProvider(
//                    static (node, ct) => node is FieldDeclarationSyntax { AttributeLists.Count: > 0 } fieldDeclaration
//                        && fieldDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword),
//                    static (context2, ct) =>
//                    {
//                        if (context2.Node is FieldDeclarationSyntax fieldDeclaration &&
//                            fieldDeclaration.Declaration.Variables.Count == 1 &&
//                            context2.SemanticModel.GetDeclaredSymbol(fieldDeclaration.Declaration.Variables[0], ct) is IFieldSymbol symbol)
//                        {
//                            if (fieldDeclaration.Parent is ClassDeclarationSyntax classDeclaration)
//                            {
//                                var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);
//                                switch (classDeclaration.Parent)
//                                {
//                                    case NamespaceDeclarationSyntax namespaceDeclaration:
//                                        return new Models.FieldDeclaration
//                                        {
//                                            Namespace = namespaceDeclaration.Name.ToString(),
//                                            FullyQualifiedClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
//                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
//                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
//                                            )),
//                                            ClassName = classSymbol.Name,
//                                            Name = symbol.Name,
//                                            FullyQualifiedTypeName = symbol.Type.ToDisplayString(new SymbolDisplayFormat(
//                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
//                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
//                                            )),
//                                            Type = symbol.Type.Name,
//                                        };
//                                    case FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration:
//                                        return new Models.FieldDeclaration
//                                        {
//                                            Namespace = fileScopedNamespaceDeclaration.Name.ToString(),
//                                            FullyQualifiedClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
//                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
//                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
//                                            )),
//                                            ClassName = classSymbol.Name,
//                                            Name = symbol.Name,
//                                            FullyQualifiedTypeName = symbol.Type.ToDisplayString(new SymbolDisplayFormat(
//                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
//                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
//                                            )),
//                                            Type = symbol.Type.Name,
//                                        };
//                                    default:
//                                        return new Models.FieldDeclaration
//                                        {
//                                            Namespace = null,
//                                            FullyQualifiedClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
//                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
//                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
//                                            )),
//                                            ClassName = classSymbol.Name,
//                                            Name = symbol.Name,
//                                            FullyQualifiedTypeName = symbol.Type.ToDisplayString(new SymbolDisplayFormat(
//                                                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
//                                                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
//                                            )),
//                                            Type = symbol.Type.Name,
//                                        };
//                                }

//                            }
//                        }
                     
//                        return default;
//                    }
//                )
//                .WithComparer(ValueComparers.FieldDeclarationComparer.Instance)
//                .Collect();

//            var classNamesSourceProvider = classDeclarationsProvider
//                .Combine(config)
//                .Select(static (p, ct) => new Producers.ClassNamesProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

//            var classNameListSourceProvider = classDeclarationsProvider
//                .Combine(config)
//                .Select(static (p, ct) => new Producers.ClassNameListProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

//            var fieldDeclarationSourceProvider = fieldDeclarationsProvider
//                .Combine(config)
//                .Select(static (p, ct) => new Producers.FieldNameListProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

//            // Combine all Source Providers
//            var sourceProvider = classNamesSourceProvider
//                .Combine(fieldDeclarationSourceProvider)
//                .SelectMany(static (p, _) => new Producer[] { p.Left, p.Right })
//                .Collect()
//                .Combine(classNameListSourceProvider)
//                .SelectMany(static (p, _) => p.Left.Concat(new Producer[] { p.Right }));

//            // Generate Code
//            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
//        }
//    }

//    //public static partial class Ext
//    //{
//    //    [GenericMethod(Count = 5, Transformer = typeof(GenericMethodTransformer))]
//    //    public static void RegisterSourceOutput(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action)
//    //    {
//    //    }

//    //    //    public static void RegisterSourceOutput<T1, T2>(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action, IncrementalValueProvider<T1> p1, IncrementalValueProvider<T2> p2)
//    //    //    {

//    //    //    }
//    //    //    public static void RegisterSourceOutput<T1, T2, T3>(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action, IncrementalValueProvider<T1> p1, IncrementalValueProvider<T2> p2, IncrementalValueProvider<T3> p3)
//    //    //    {

//    //    //    }
//    //}

//    //public class GenericMethodTransformer : IGenericMethodTransformer
//    //{
//    //    public string Transform(string name) => $"IncrementalValueProvider<{name}>";
//    //}
//    //public interface IGenericMethodTransformer
//    //{
//    //    string Transform(string name);
//    //}
//}
