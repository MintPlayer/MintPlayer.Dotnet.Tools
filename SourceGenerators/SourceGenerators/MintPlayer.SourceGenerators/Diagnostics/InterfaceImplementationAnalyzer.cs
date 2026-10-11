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

                // Only consider classes and structs (records and record structs included) implementing at least one
                // interface. Structs were left out (K8); spike S5 ran the rule over ~2,400 files with them in and found
                // no new warnings.
                if (namedTypeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct) || !namedTypeSymbol.Interfaces.Any())
                    return;

                // Interfaces a fix could edit. If every implemented interface lives in metadata
                // there is nothing to report against — otherwise every IDisposable implementation
                // with an extra public member would light up.
                var editableInterfaces = InterfaceMemberCandidates.EditableInterfaces(namedTypeSymbol);
                if (editableInterfaces.Count == 0)
                    return;

                // Membership is tested against the union of ALL declared interfaces, including
                // metadata-only ones. A member carried by one interface is not missing merely
                // because another lacks it.
                var interfaceNames = string.Join(", ", editableInterfaces.Select(i => i.Name));

                foreach (var member in InterfaceMemberCandidates.In(namedTypeSymbol, ignoreAttributeSymbol))
                {
                    if (InterfaceMemberCandidates.IsSatisfied(namedTypeSymbol, member))
                        continue;

                    // Reported once per member, not once per interface. Which of several candidate
                    // interfaces the member should be added to is a genuine choice, and the code
                    // fix offers it as one action each; raising a diagnostic per interface would
                    // instead let "fix all occurrences" add the member to every one of them.
                    var syntaxNode = member.DeclaringSyntaxReferences.First().GetSyntax(context.CancellationToken);
                    var diagnostic = Diagnostic.Create(
                        DiagnosticRules.MissingInterfaceMemberRule,
                        syntaxNode.GetLocation(),
                        ImmutableDictionary<string, string?>.Empty
                            .Add(InterfaceMemberCandidates.MemberNameProperty, member.Name)
                            .Add(InterfaceMemberCandidates.MemberSignatureProperty, InterfaceMemberCandidates.SignatureKey(member)),
                        member.Name,
                        interfaceNames);

                    context.ReportDiagnostic(diagnostic);
                }
                break;
        }
    }
}
