using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace MintPlayer.SourceGenerators.Diagnostics.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class InterfaceImplementationAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.MissingInterfaceMemberRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
        }

        private static void AnalyzeSymbol(SymbolAnalysisContext context)
        {
            var namedTypeSymbol = (INamedTypeSymbol)context.Symbol;

            // Check if the class implements an interface
            var implementedInterfaces = namedTypeSymbol.Interfaces;
            if (!implementedInterfaces.Any() || namedTypeSymbol.TypeKind != TypeKind.Class)
                return;

            foreach (var iface in implementedInterfaces)
            {
                // Get members of the interface
                var interfaceMembers = iface.GetMembers();
                var classMembers = namedTypeSymbol.GetMembers()
                    .Where(m => m.DeclaredAccessibility == Accessibility.Public);

                foreach (var member in classMembers)
                {
                    if (!interfaceMembers.Any(im => im.Name == member.Name) && !member.IsImplicitlyDeclared)
                    {
                        if (member is IMethodSymbol method && method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet) continue;

                        // Report diagnostic for missing member
                        var syntaxNode = member.DeclaringSyntaxReferences.First().GetSyntax(context.CancellationToken);
                        //var diagnostic = Diagnostic.Create(DiagnosticRules.MissingInterfaceMemberRule, member.Locations[0], member.Name, iface.Name);
                        var diagnostic = Diagnostic.Create(DiagnosticRules.MissingInterfaceMemberRule, syntaxNode.GetLocation(), member.Name, iface.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }
}
