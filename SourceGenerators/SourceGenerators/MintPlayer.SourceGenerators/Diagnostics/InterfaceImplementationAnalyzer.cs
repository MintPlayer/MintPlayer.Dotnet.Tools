using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MintPlayer.SourceGenerators.Attributes;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class InterfaceImplementationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.MissingInterfaceMemberRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        switch (context.Symbol)
        {
            case INamedTypeSymbol namedTypeSymbol:
                var ignoreAttributeSymbol = context.Compilation.GetTypeByMetadataName(typeof(NoInterfaceMemberAttribute).FullName);

                // Check if the class implements an interface
                var implementedInterfaces = namedTypeSymbol.Interfaces;
                if (!implementedInterfaces.Any() || namedTypeSymbol.TypeKind != TypeKind.Class)
                    return;

                foreach (var iface in implementedInterfaces)
                {
                    // Get all members of the interface and sub-interfaces
                    var interfaceMembers = iface.GetMembers()
                        .Concat(iface.AllInterfaces.SelectMany(i => i.GetMembers()))
                        .ToArray();
                    var classMembers = namedTypeSymbol.GetMembers()
                        .Where(m => (m.DeclaredAccessibility == Accessibility.Public) && !m.IsStatic && m.CanBeReferencedByName && !m.IsImplicitlyDeclared)
                        .Where(m => !m.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, ignoreAttributeSymbol)));

                    foreach (var member in classMembers)
                    {
                        // Member already exists
                        if (interfaceMembers.Any(im => im.Name == member.Name)) continue;

                        if (member is IMethodSymbol method)
                        {
                            // Try to ignore events
                            if (method.Kind is SymbolKind.Event) continue;
                        }

                        // Report diagnostic for missing member
                        var syntaxNode = member.DeclaringSyntaxReferences.First().GetSyntax(context.CancellationToken);
                        var diagnostic = Diagnostic.Create(DiagnosticRules.MissingInterfaceMemberRule, syntaxNode.GetLocation(), member.Name, iface.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
                break;
        }
    }
}
