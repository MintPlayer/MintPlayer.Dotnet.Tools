using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Models;
using System.Linq;
using MintPlayer.SourceGenerators.Attributes;
using Microsoft.Extensions.DependencyInjection;

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
                    static (node, ct) => node is ClassDeclarationSyntax,
                    static (context2, ct) =>
                    {
                        if (context2.Node is ClassDeclarationSyntax classDeclaration)
                        {
                            var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration, ct);
                            if (classSymbol is INamedTypeSymbol namedTypeSymbol)
                            {
                                var attr = classSymbol.GetAttributes()
                                    .FirstOrDefault(a => a.AttributeClass?.Name == nameof(RegisterAttribute));

                                if (attr is null) return default;

                                return new ServiceRegistration
                                {
                                    ServiceTypeName = attr.ConstructorArguments[0].Value is INamedTypeSymbol symbol
                                                ? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                                : null,
                                    ImplementationTypeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    Lifetime = (ServiceLifetime)attr.ConstructorArguments[1].Value!,
                                };
                            }
                        }

                        return default;
                    }
                )
                .WithComparer(ValueComparers.ServiceRegistrationComparer.Instance)
                .Collect();

            var registerAttributeSourceProvider = classesWithRegisterAttributeProvider
                .Combine(config)
                .Select(static (p, ct) => new Producers.RegistrationsProducer(p.Left, p.Right.RootNamespace!));

            // Combine all source providers
            var sourceProvider = registerAttributeSourceProvider;

            // Generate code
            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }
}
