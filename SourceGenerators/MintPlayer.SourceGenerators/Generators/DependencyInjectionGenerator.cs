using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Models;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class DependencyInjectionGenerator : IIncrementalGenerator
    {
        public DependencyInjectionGenerator()
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

            var classesWithRegisterAttributeProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) => true,
                    static (context2, ct) => new Models.ServiceRegistration()
                )
                .WithComparer(ValueComparers.ServiceRegistrationComparer.Instance)
                .Collect();

            //var classesWithRegisterAttributeProvider = context.SyntaxProvider
            //    .CreateSyntaxProvider(
            //        static (node, ct) => node is ClassDeclarationSyntax,
            //        static (context2, ct) =>
            //        {
            //            return new ServiceRegistration();
            //        }
            //    )
            //    //.WithComparer(ValueComparers.ServiceRegistrationComparer.Instance)
            //    .Collect();

            //var registerAttributeSourceProvider = classesWithRegisterAttributeProvider
            //    .Combine(config)
            //    .Select(static (p, ct) => new Producers.RegistrationsProducer(p.Left.SelectMany(c => c), p.Right.RootNamespace!));

            //// Combine all source providers
            //var sourceProvider = registerAttributeSourceProvider;

            //// Generate code
            //context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }
}
