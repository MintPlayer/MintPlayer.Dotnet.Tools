using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// Code fix for MPA0003: converts the local declaration holding the AssertionScope into a
/// <c>using</c> declaration; a bare <c>new AssertionScope();</c> statement becomes
/// <c>using var scope = new AssertionScope();</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AssertionScopeNotDisposedCodeFixProvider)), Shared]
public class AssertionScopeNotDisposedCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("MPA0003");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        if (node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is { } localDeclaration)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Convert to using declaration",
                    createChangedDocument: ct => ConvertToUsingDeclarationAsync(context.Document, localDeclaration, ct),
                    equivalenceKey: "ConvertToUsingDeclaration"),
                diagnostic);
        }
        else if (node.FirstAncestorOrSelf<ExpressionStatementSyntax>() is { } expressionStatement)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Assign to a using declaration",
                    createChangedDocument: ct => WrapInUsingDeclarationAsync(context.Document, expressionStatement, ct),
                    equivalenceKey: "WrapInUsingDeclaration"),
                diagnostic);
        }
    }

    private static async Task<Document> ConvertToUsingDeclarationAsync(Document document, LocalDeclarationStatementSyntax localDeclaration, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var firstToken = localDeclaration.GetFirstToken();
        var newDeclaration = localDeclaration
            .ReplaceToken(firstToken, firstToken.WithLeadingTrivia())
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword)
                .WithLeadingTrivia(firstToken.LeadingTrivia)
                .WithTrailingTrivia(SyntaxFactory.Space));

        return document.WithSyntaxRoot(root.ReplaceNode(localDeclaration, newDeclaration));
    }

    private static async Task<Document> WrapInUsingDeclarationAsync(Document document, ExpressionStatementSyntax statement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var newStatement = SyntaxFactory
            .ParseStatement($"using var scope = {statement.Expression.WithoutTrivia().ToFullString()};")
            .WithTriviaFrom(statement);

        return document.WithSyntaxRoot(root.ReplaceNode(statement, newStatement));
    }
}
