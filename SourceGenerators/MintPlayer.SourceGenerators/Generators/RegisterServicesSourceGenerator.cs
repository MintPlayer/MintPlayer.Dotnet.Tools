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
    public class RegisterServicesSourceGenerator : IIncrementalGenerator
    {
        public RegisterServicesSourceGenerator()
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

            //var classDeclarationsProvider = context.SyntaxProvider
            //    .CreateSyntaxProvider(
            //        static (node, ct) =>
            //        {
            //            return node is ClassDeclarationSyntax { AttributeLists.Count: > 1 } classDeclaration;
            //        },
            //        static (context, ct) =>
            //        {
            //            if (context.Node is ClassDeclarationSyntax classDeclaration &&
            //                context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol classSymbol)
            //            {
            //                var attribute = classDeclaration.AttributeLists.SelectMany(l => l.Attributes).OfType<AttributeSyntax>()
            //                    .Select(a => new
            //                    {
            //                        Attribute = a,
            //                        Type = context.SemanticModel.GetTypeInfo(a, ct).ConvertedType,
            //                    })
            //                    .FirstOrDefault(a => a.Type!.Equals(context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(RegisterAttribute).FullName)));



            //                return new Models.ClassDeclaration
            //                {
            //                    Name = classSymbol.Name,
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

            var assemblyAttributesProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) =>
                    {
                        return node is AttributeSyntax;
                    },
                    static (context, ct) =>
                    {
                        return new Models.AssemblyDeclaration
                        {

                        };
                    }
                )
                .Collect();

            var assemblyAttributesSourceProvider = assemblyAttributesProvider
                .Combine(config)
                .Select(static (p, ct) => new Producers.AssemblyProducer(p.Left, rootNamespace: p.Right.RootNamespace));

            // Combine all source providers
            var sourceProvider = assemblyAttributesSourceProvider;

            // Generate code
            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }
}
