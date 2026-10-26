using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor RedundantImplementInterfaceProperty = new(
        id: "IMPLINTF008",
        title: "Hand-written property is redundant with [ImplementInterface]",
        messageFormat: "Property '{0}' is exactly what [ImplementInterface] generates for '{1}'; remove it",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}

/// <summary>
/// IMPLINTF008: a hand-written <c>public T P { get; set; }</c> on a type with <c>[ImplementInterface]</c> that the
/// generator would produce identically, typically left over from before the attribute was adopted.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RedundantImplementInterfacePropertyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.RedundantImplementInterfaceProperty];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IPropertySymbol property) return;
        if (ImplementInterfaceSupport.RedundantWith(property, context.CancellationToken) is not { } target) return;
        if (property.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) is not PropertyDeclarationSyntax syntax) return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticRules.RedundantImplementInterfaceProperty,
            syntax.Identifier.GetLocation(),
            property.Name,
            target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }
}
