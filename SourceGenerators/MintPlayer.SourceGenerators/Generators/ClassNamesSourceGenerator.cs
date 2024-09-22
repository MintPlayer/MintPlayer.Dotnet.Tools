using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System;
using System.Collections.Generic;
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

            var classNamesProvider = context.SyntaxProvider.CreateSyntaxProvider(
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
            );

            var classNamesSourceProvider = classNamesProvider
                .WithComparer(ValueComparers.ClassDeclarationValueComparer.Instance)
                // Group whatever you want
                .Collect()

                .Combine(config)
                // only call once
                .Select(static (p, ct) => new Producers.ClassNamesProducer(declarations: p.Left, rootNamespace: p.Right.RootNamespace!));

            // Combine all Source Providers
            var sourceProvider = classNamesSourceProvider;

            // Generate Code
            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }
}
