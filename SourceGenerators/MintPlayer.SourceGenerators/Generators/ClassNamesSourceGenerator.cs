using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

            var classNamesProvider = context.SyntaxProvider
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
                            return new Models.ClassDeclaration { Name = symbol.Name };
                        }
                        else
                        {
                            return default;
                        }
                    }
                )
                .WithComparer(ValueComparers.ClassDeclarationValueComparer.Instance)
                .Collect();

            var classNamesSourceProvider = classNamesProvider
                .Combine(config)
                .Select(static (p, ct) => new Producers.ClassNamesProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

            var classNameListSourceProvider = classNamesProvider
                .Combine(config)
                .Select(static (p, ct) => new Producers.ClassNameListProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

            // Combine all Source Providers
            var sourceProvider = classNamesSourceProvider
                .Combine(classNameListSourceProvider)
                .SelectMany(static (p, _) => new Producer[] { p.Left, p.Right });

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
