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
    public class ServiceRegistrationsGenerator : IncrementalGenerator
    {
        public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
        {
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

                                if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol interfaceSymbol) return default;

                                // Verify that the class implements the interface
                                if (namedTypeSymbol.AllInterfaces.All(i => i != interfaceSymbol)) return default;

                                return new ServiceRegistration
                                {
                                    ServiceTypeName = interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    ImplementationTypeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    Lifetime = (ServiceLifetime)attr.ConstructorArguments[1].Value!,
                                    MethodNameHint = (string?)attr.ConstructorArguments[2].Value ?? string.Empty,
                                };
                            }
                        }

                        return default;
                    }
                )
                .WithComparer(ValueComparers.ServiceRegistrationComparer.Instance)
                .Collect();

            var registerAttributeSourceProvider = classesWithRegisterAttributeProvider
                .Combine(settingsProvider)
                .Select(static (providers, ct) => new Producers.RegistrationsProducer(providers.Left, providers.Right.RootNamespace!) as Producer);

            // Combine all source providers
            var sourceProvider = registerAttributeSourceProvider;

            // Generate code
            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }
}
