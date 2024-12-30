using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Generators;
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
            //context.Compilation.gets
            var namedTypeSymbol = (INamedTypeSymbol)context.Symbol;
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
                    .Where(m => m.DeclaredAccessibility == Accessibility.Public)
                    .Where(m => m.GetAttributes().All(attr => attr.AttributeClass?.Name != nameof(NoInterfaceMemberAttribute)));

                foreach (var member in classMembers)
                {
                    if (!interfaceMembers.Any(im => im.Name == member.Name) && !member.IsImplicitlyDeclared)
                    {
                        if (member is IMethodSymbol method)
                        {
                            // We don't need to process the Xxx_get and Xxx_set methods
                            if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet) continue;

                            // Try to ignore events
                            if (method.Kind == SymbolKind.Event) continue;
                        }

                        if (member.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, ignoreAttributeSymbol)))
                            continue;

                        // Report diagnostic for missing member
                        var syntaxNode = member.DeclaringSyntaxReferences.First().GetSyntax(context.CancellationToken);
                        var diagnostic = Diagnostic.Create(DiagnosticRules.MissingInterfaceMemberRule, syntaxNode.GetLocation(), member.Name, iface.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }
}
