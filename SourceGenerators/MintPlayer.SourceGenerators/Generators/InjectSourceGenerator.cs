using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using MintPlayer.SourceGenerators.Tools.Extensions;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class InjectSourceGenerator : IIncrementalGenerator
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

            var attributesProvider = context.SyntaxProvider
                .CreateSyntaxProvider<Models.FieldDeclaration[]>(
                    (Func<SyntaxNode, System.Threading.CancellationToken, bool>)(static (node, ct) =>
                    {
                        return node is FieldDeclarationSyntax { } fieldDeclaration;
                    }),
                    static (context, ct) =>
                    {
                        if (!(context.Node is FieldDeclarationSyntax { AttributeLists.Count: > 0 } fieldDeclaration))
                        {
                            return default;
                        }
                        else if (fieldDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword))
                        {
                            var fieldDeclarations = fieldDeclaration.Declaration.Variables
                                                    .Select(v =>
                                                    {
                                                        var sym = context.SemanticModel.GetDeclaredSymbol(v, ct);
                                                        return sym is IFieldSymbol symbol
                                                            ? new Models.FieldDeclaration
                                                            {
                                                                IsReadonly = symbol.IsReadOnly,
                                                                FieldName = symbol.Name,
                                                                FieldType = new Models.TypeInformation
                                                                {
                                                                    Name = symbol.Type.Name,
                                                                    FullyQualifiedName = symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)//.RemoveBegin("global::"),
                                                                },
                                                                Class = fieldDeclaration.Parent is ClassDeclarationSyntax syntax
                                                                    && context.SemanticModel.GetDeclaredSymbol(syntax, ct) is ITypeSymbol typeSymbol
                                                                    ? new Models.ClassInformation
                                                                    {
                                                                        Name = typeSymbol.Name,
                                                                        FullyQualifiedName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)//.RemoveBegin("global::"),
                                                                    }
                                                                    : null
                                                            }
                                                            : null;
                                                    })
                                                    .Where(v => v != null)
                                                    .ToArray();
                            return fieldDeclarations;
                        }
                        else
                        {
                            return default;
                        }
                    }
                )
                .Collect();

            var attributesSourceProvider = attributesProvider
                .Combine(config)
                .Select(static (p, ct) => new Producers.FieldNamesProducer(p.Right.RootNamespace));

            // Combine all source providers
            var sourceProvider = attributesSourceProvider;

            // Generate code
            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }
}
