using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;

namespace MintPlayer.SourceGenerators.Diagnostics;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnusedUsingsCodeFixProvider)), Shared]
public class UnusedUsingsCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("MP001");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        if (node is not UsingDirectiveSyntax) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Remove all unused usings",
                createChangedDocument: c => RemoveAllUnusedUsings(context.Document, context.Diagnostics, c),
                equivalenceKey: "RemoveAllUnusedUsings"),
            diagnostic);
    }

    private static async Task<Document> RemoveAllUnusedUsings(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Find all unused using nodes
        var usingDirectives = diagnostics
            .Select(d => root.FindNode(d.Location.SourceSpan))
            .OfType<UsingDirectiveSyntax>()
            .ToList();

        var newRoot = root.RemoveNodes(usingDirectives, SyntaxRemoveOptions.KeepNoTrivia);
        return document.WithSyntaxRoot(newRoot);
    }
}
