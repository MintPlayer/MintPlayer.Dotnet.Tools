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

                // Only consider classes implementing at least one interface
                if (namedTypeSymbol.TypeKind != TypeKind.Class || !namedTypeSymbol.Interfaces.Any())
                    return;

                foreach (var iface in namedTypeSymbol.Interfaces)
                {
                    // Skip interfaces that are not defined in source (metadata-only)
                    if (iface.Locations.All(l => !l.IsInSource))
                        continue;

                    // Get all members of the interface and sub-interfaces
                    var interfaceMembers = iface.GetMembers()
                        .Concat(iface.AllInterfaces.SelectMany(i => i.GetMembers()))
                        .ToArray();

                    // Only consider public instance methods and properties; ignore nested types, events, fields, etc.
                    var classMembers = namedTypeSymbol.GetMembers()
                        .Where(m => m.DeclaredAccessibility == Accessibility.Public
                                    && !m.IsStatic
                                    && m.CanBeReferencedByName
                                    && !m.IsImplicitlyDeclared
                                    && (m is IMethodSymbol || m is IPropertySymbol))
                        .Where(m => !m.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, ignoreAttributeSymbol)));

                    foreach (var member in classMembers)
                    {
                        // Ignore constructors and static constructors
                        if (member is IMethodSymbol method && (method.MethodKind == MethodKind.Constructor || method.MethodKind == MethodKind.StaticConstructor))
                            continue;

                        // Member already exists in any of the interface hierarchies
                        if (interfaceMembers.Any(im => im.Name == member.Name))
                            continue;

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
