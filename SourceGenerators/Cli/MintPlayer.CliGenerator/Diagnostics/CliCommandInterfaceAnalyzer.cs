using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.CliGenerator.Diagnostics;

public sealed partial class CliCommandInterfaceAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(static symbolContext =>
        {
            if (symbolContext.Symbol is not INamedTypeSymbol typeSymbol)
            {
                return;
            }

            AnalyzeType(symbolContext, typeSymbol);
        }, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind != TypeKind.Class || typeSymbol.IsStatic)
        {
            return;
        }

        var cliCommandAttribute = context.Compilation.GetTypeByMetadataName("MintPlayer.CliGenerator.Attributes.CliCommandAttribute");
        var cliRootCommandAttribute = context.Compilation.GetTypeByMetadataName("MintPlayer.CliGenerator.Attributes.CliRootCommandAttribute");
        if (cliCommandAttribute is null || cliRootCommandAttribute is null)
        {
            return;
        }

        var attributes = typeSymbol.GetAttributes();
        var matchingAttribute = attributes.FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, cliCommandAttribute) ||
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, cliRootCommandAttribute));

        if (matchingAttribute is null)
        {
            return;
        }

        var cliCommandInterface = context.Compilation.GetTypeByMetadataName("MintPlayer.CliGenerator.Attributes.ICliCommand");
        if (cliCommandInterface is null)
        {
            return;
        }

        var implementsInterface = typeSymbol.AllInterfaces.Any(@interface =>
            SymbolEqualityComparer.Default.Equals(@interface, cliCommandInterface));

        if (implementsInterface)
        {
            return;
        }

        var attributeDisplayName = matchingAttribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? matchingAttribute.AttributeClass?.Name ?? "CliCommandAttribute";
        var location = matchingAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? typeSymbol.Locations.FirstOrDefault(location => location.IsInSource)
            ?? Location.None;

        context.ReportDiagnostic(Rule.Create(location, typeSymbol.Name, attributeDisplayName));
    }
}
