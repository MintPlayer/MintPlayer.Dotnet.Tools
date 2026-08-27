using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// Code fix for MPA0100: migrates a file from FluentAssertions to MintPlayer.Assertions.
/// <para>
/// The fix is <b>syntax-driven by design</b>: FluentAssertions is usually no longer referenced by
/// the project when this fix runs, so no semantic verification of FluentAssertions symbols is
/// attempted. It performs two document-wide rewrites:
/// </para>
/// <list type="number">
/// <item>Replaces every plain <c>using FluentAssertions;</c> / <c>using FluentAssertions.*;</c>
/// directive with <c>using MintPlayer.Assertions;</c> (always) and
/// <c>using MintPlayer.Assertions.Execution;</c> (when <c>FluentAssertions.Execution</c> was
/// imported), deduplicated against usings already present.</item>
/// <item>Renames the known-renamed assertion methods listed in <see cref="RenameTable"/> at every
/// member-access site. Calls with no direct equivalent (e.g. <c>NotThrowAfter</c>) and shape-
/// compatible members (<c>Invoking</c>, <c>Awaiting</c>, ...) are left untouched.</item>
/// </list>
/// Supports Fix All across document/project/solution via the batch fixer.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FluentAssertionsMigrationCodeFixProvider)), Shared]
public class FluentAssertionsMigrationCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// FluentAssertions member name → MintPlayer.Assertions member name. Extend here when another
    /// rename between the two libraries surfaces.
    /// </summary>
    internal static readonly ImmutableDictionary<string, string> RenameTable = new Dictionary<string, string>
    {
        ["HaveCountGreaterOrEqualTo"] = "HaveCountGreaterThanOrEqualTo",
        ["BeGreaterOrEqualTo"] = "BeGreaterThanOrEqualTo",
        ["BeLessOrEqualTo"] = "BeLessThanOrEqualTo",
        ["WithInnerExceptionExactly"] = "WithInnerExactly",
    }.ToImmutableDictionary();

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("MPA0100");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Migrate file to MintPlayer.Assertions",
                createChangedDocument: ct => MigrateDocumentAsync(context.Document, ct),
                equivalenceKey: "MigrateToMintPlayerAssertions"),
            diagnostic);
        return Task.CompletedTask;
    }

    private static async Task<Document> MigrateDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        root = RenameKnownMembers(root);
        root = RewriteUsings(root);

        return document.WithSyntaxRoot(root);
    }

    /// <summary>Renames every member access whose name appears in <see cref="RenameTable"/>.</summary>
    private static SyntaxNode RenameKnownMembers(SyntaxNode root)
    {
        var namesToRename = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Select(memberAccess => memberAccess.Name)
            .Where(name => RenameTable.ContainsKey(name.Identifier.ValueText))
            .ToList();

        if (namesToRename.Count == 0)
            return root;

        return root.ReplaceNodes(namesToRename, (original, _) =>
        {
            var newIdentifier = SyntaxFactory
                .Identifier(RenameTable[original.Identifier.ValueText])
                .WithTriviaFrom(original.Identifier);
            return original switch
            {
                IdentifierNameSyntax identifierName => identifierName.WithIdentifier(newIdentifier),
                GenericNameSyntax genericName => genericName.WithIdentifier(newIdentifier),
                _ => original,
            };
        });
    }

    /// <summary>
    /// Replaces the FluentAssertions using directives with the MintPlayer.Assertions equivalents,
    /// deduplicated against the usings already present in the file.
    /// </summary>
    private static SyntaxNode RewriteUsings(SyntaxNode root)
    {
        var fluentAssertionsUsings = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(FluentAssertionsMigrationAnalyzer.IsFluentAssertionsUsing)
            .ToList();

        if (fluentAssertionsUsings.Count == 0)
            return root;

        var existingUsingNames = new HashSet<string>(root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(u => u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None))
            .Select(u => u.Name?.ToString())
            .OfType<string>());

        var replacements = new List<UsingDirectiveSyntax>();
        if (!existingUsingNames.Contains("MintPlayer.Assertions"))
            replacements.Add(CreateUsing("MintPlayer.Assertions"));
        if (fluentAssertionsUsings.Any(u => u.Name?.ToString() == "FluentAssertions.Execution")
            && !existingUsingNames.Contains("MintPlayer.Assertions.Execution"))
            replacements.Add(CreateUsing("MintPlayer.Assertions.Execution"));

        root = root.TrackNodes(fluentAssertionsUsings);

        if (replacements.Count > 0)
        {
            var first = root.GetCurrentNode(fluentAssertionsUsings[0]);
            if (first is not null)
            {
                replacements[0] = replacements[0].WithLeadingTrivia(first.GetLeadingTrivia());
                root = root.InsertNodesBefore(first, replacements);
            }
        }

        var currentFluentAssertionsUsings = fluentAssertionsUsings
            .Select(u => root.GetCurrentNode(u))
            .OfType<UsingDirectiveSyntax>()
            .ToList();

        return root.RemoveNodes(currentFluentAssertionsUsings, SyntaxRemoveOptions.KeepNoTrivia) ?? root;
    }

    private static UsingDirectiveSyntax CreateUsing(string namespaceName) =>
        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
}
