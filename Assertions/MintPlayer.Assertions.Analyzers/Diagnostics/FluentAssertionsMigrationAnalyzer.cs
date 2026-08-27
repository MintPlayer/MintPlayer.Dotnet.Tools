using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// MPA0100: reported on a plain <c>using FluentAssertions;</c> (or <c>FluentAssertions.*</c>)
/// directive. Purely syntax-driven by design: the FluentAssertions package is typically not
/// referenced any more (or about to be removed), so no semantic verification of FluentAssertions
/// symbols is attempted. Alias and <c>using static</c> directives are deliberately skipped — the
/// migration code fix could not safely rewrite those.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class FluentAssertionsMigrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.FluentAssertionsMigrationRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
    }

    internal static bool IsFluentAssertionsUsing(UsingDirectiveSyntax usingDirective)
    {
        if (usingDirective.Alias is not null || !usingDirective.StaticKeyword.IsKind(SyntaxKind.None))
            return false;

        var name = usingDirective.Name?.ToString();
        return name is not null
            && (name == "FluentAssertions" || name.StartsWith("FluentAssertions.", StringComparison.Ordinal));
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;
        if (IsFluentAssertionsUsing(usingDirective))
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.FluentAssertionsMigrationRule, usingDirective.GetLocation()));
    }
}
