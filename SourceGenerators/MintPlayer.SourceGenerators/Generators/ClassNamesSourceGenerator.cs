using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Extensions;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class ClassNamesSourceGenerator : IIncrementalGenerator
    {
        public ClassNamesSourceGenerator()
        {
        }

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

            var classDeclarationsProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) =>
                    {
                        return node is ClassDeclarationSyntax { } classDeclaration;
                    },
                    static (context, ct) =>
                    {
                        if (context.Node is ClassDeclarationSyntax classDeclaration &&
                            context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol symbol)
                        {
                            return new Models.ClassDeclaration
                            {
                                Name = symbol.Name,
                            };
                        }
                        else
                        {
                            return default;
                        }
                    }
                )
                .WithComparer(ValueComparers.ClassDeclarationValueComparer.Instance)
                .Collect();

            // Provides fields with their class and base-class
            var fieldDeclarationsProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) => node is FieldDeclarationSyntax { AttributeLists.Count: > 0 } fieldDeclaration
                        && fieldDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword),
                    static (context2, ct) =>
                    {
                        if (context2.Node is FieldDeclarationSyntax fieldDeclaration &&
                            fieldDeclaration.Declaration.Variables.Count == 1 &&
                            context2.SemanticModel.GetDeclaredSymbol(fieldDeclaration.Declaration.Variables[0], ct) is IFieldSymbol symbol)
                        {
                            if (fieldDeclaration.Parent is ClassDeclarationSyntax classDeclaration &&
                                classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                            {
                                var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);
                                return new Models.FieldDeclaration
                                {
                                    Namespace = classDeclaration.Parent is BaseNamespaceDeclarationSyntax stx ? stx.Name.ToString() : null,
                                    Class = new Models.ClassInformation
                                    {
                                        Name = classSymbol.Name,
                                        FullyQualifiedName = classSymbol.ToFullyQualifiedName(),
                                        BaseType = new Models.TypeInformation
                                        {
                                            Name = classSymbol.BaseType.Name, //.Constructors[0].Parameters
                                            FullyQualifiedName = classSymbol.BaseType.ToFullyQualifiedName(),
                                            Constructors = classSymbol.BaseType.Constructors.Select(ctor => new Models.ConstructorInformation
                                            {
                                                Parameters = ctor.Parameters.Select(p => new Models.ParameterInformation
                                                {
                                                    Name = p.Name,
                                                    Type = new Models.TypeInformation
                                                    {
                                                        Name = p.Type.Name,
                                                        FullyQualifiedName = p.Type.ToFullyQualifiedName(),
                                                    }
                                                }).ToArray()
                                            }).ToArray()
                                        }
                                    },
                                    FieldName = symbol.Name,
                                    FieldType = new Models.TypeInformation
                                    {
                                        Name = symbol.Type.Name,
                                        FullyQualifiedName = symbol.Type.ToFullyQualifiedName(),
                                    }
                                };
                            }
                        }
                        return default;
                    }
                )
                .WithComparer(ValueComparers.FieldDeclarationComparer.Instance)
                .Collect();

            //var classesWithFieldsProvider = fieldDeclarationsProvider
            //    .Select((fields, ct) =>
            //    {
            //        return fields.GroupBy(f => f.Class.FullyQualifiedName)
            //            .Select(classGrouping => new Models.ClassInformation
            //            {
            //                Name = classGrouping.Key
            //            })
            //    });

            var classNamesSourceProvider = classDeclarationsProvider
                .Combine(config)
                .Select(static (p, ct) => new Producers.ClassNamesProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

            var classNameListSourceProvider = classDeclarationsProvider
                .Combine(config)
                .Select(static (p, ct) => new Producers.ClassNameListProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

            var fieldDeclarationSourceProvider = fieldDeclarationsProvider
                .Combine(config)
                .Select(static (p, ct) => new Producers.FieldNameListProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

            // Combine all Source Providers
            var sourceProvider = classNamesSourceProvider
                .Combine(fieldDeclarationSourceProvider)
                .SelectMany(static (p, _) => new Producer[] { p.Left, p.Right })
                .Collect()
                .Combine(classNameListSourceProvider)
                .SelectMany(static (p, _) => p.Left.Concat(new Producer[] { p.Right }));

            // Generate Code
            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }

    //public static partial class Ext
    //{
    //    [GenericMethod(Count = 5, Transformer = typeof(GenericMethodTransformer))]
    //    public static void RegisterSourceOutput(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action)
    //    {
    //    }

    //    //    public static void RegisterSourceOutput<T1, T2>(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action, IncrementalValueProvider<T1> p1, IncrementalValueProvider<T2> p2)
    //    //    {

    //    //    }
    //    //    public static void RegisterSourceOutput<T1, T2, T3>(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action, IncrementalValueProvider<T1> p1, IncrementalValueProvider<T2> p2, IncrementalValueProvider<T3> p3)
    //    //    {

    //    //    }
    //}

    //public class GenericMethodTransformer : IGenericMethodTransformer
    //{
    //    public string Transform(string name) => $"IncrementalValueProvider<{name}>";
    //}
    //public interface IGenericMethodTransformer
    //{
    //    string Transform(string name);
    //}
}
