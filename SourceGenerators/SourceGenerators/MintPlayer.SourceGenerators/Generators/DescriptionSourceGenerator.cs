using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using System.Net;
using System.Text.RegularExpressions;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public partial class DescriptionSourceGenerator : IncrementalGenerator
{
    private const string DescriptionAttributeName = "System.ComponentModel.DescriptionAttribute";

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var xmlCommentProvider = context.SyntaxProvider.CreateSyntaxProvider(
            // Only a partial type declaration with a doc comment: the generator re-declares the type, so nothing else
            // can produce output. The predicate this replaces also admitted every constructor, property and event,
            // each costing a GetDeclaredSymbol whose result was then thrown away (P22).
            static (node, ct) => node is TypeDeclarationSyntax typeDeclaration
                                 && typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                                 && HasDocumentationComment(typeDeclaration),
            static (ctx, ct) =>
            {
                if (ctx.SemanticModel.GetDeclaredSymbol((TypeDeclarationSyntax)ctx.Node, ct) is not INamedTypeSymbol symbol)
                    return null;

                // A [Description] the user wrote wins; a second one is CS0579 (P21).
                if (symbol.HasAttribute(DescriptionAttributeName))
                    return null;

                // Not re-declarable (a non-partial parent, a `file` type, which a generated file would turn into a
                // second type, ...): nothing is emitted. Description is opportunistic, every documented type is a
                // candidate, so there is no diagnostic for the types it cannot extend.
                var pathSpec = symbol.GetPathSpec(ct);
                if (pathSpec is not { IsSupported: true })
                    return null;

                // ToFullString, not ToString: ToString leaves out the first trivia's own
                // leading trivia, and for a doc comment that is the "///" exterior. A summary
                // at column 0 directly below the previous line was then read without its
                // prefixes, and SanitizeMarkup found nothing.
                var xml = ctx.Node.GetLeadingTrivia().ToFullString();
                if (SanitizeMarkup(xml) is not { Length: > 0 } sanitized)
                    return null;

                return new Models.SymbolWithMarkups
                {
                    TypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    PathSpec = pathSpec,
                    MarkupText = sanitized,
                };
            })
            .Where(static item => item is not null)
            .Collect()
            // One model per type: a partial type with a summary on two parts used to get two [Description]
            // attributes, CS0579 (W1). The first part in compilation order wins.
            .Select(static (items, ct) => items
                .GroupBy(item => item!.TypeName, StringComparer.Ordinal)
                .Select(group => group.First()!)
                .ToEquatableArray());

        var xmlCommentSourceProvider = xmlCommentProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new DescriptionsProducer(p.Item1, p.Item2.RootNamespace!));

        context.ProduceCode(xmlCommentSourceProvider);
    }

    /// <summary>
    /// A <c>///</c> comment in the leading trivia. With <c>DocumentationMode.None</c> the parser keeps it as an ordinary
    /// single-line comment, so both kinds are accepted.
    /// </summary>
    private static bool HasDocumentationComment(SyntaxNode node)
    {
        if (!node.HasLeadingTrivia) return false;
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && trivia.ToString().StartsWith("///", StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    private static string SanitizeMarkup(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup)) return string.Empty;

        // Normalize line endings and strip leading XML doc prefix.
        var lines = markup!.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => l.StartsWith("///"))
            .Select(l => l.RemoveBegin("///").Trim())
            .Where(l => !string.IsNullOrEmpty(l));

        var xml = string.Join("\n", lines);

        // Extract <summary>...</summary>
        var match = summaryRegex.Value.Match(xml);
        if (!match.Success) return string.Empty;

        // Replace common inline doc tags with readable text.
        var content = match.Groups[1].Value;
        content = seeRegex.Value.Replace(content, "$1");
        content = paramrefRegex.Value.Replace(content, "$1");
        content = typeparamrefRegex.Value.Replace(content, "$1");
        content = tagsRegex.Value.Replace(content, string.Empty);

        // Decode XML/HTML entities.
        content = WebUtility.HtmlDecode(content);

        // Collapse all whitespace/newlines to single spaces.
        content = Regex.Replace(content, @"\s+", " ").Trim();

        return content;
    }

    private static Lazy<Regex> summaryRegex = new(() => new Regex(@"<summary\b[^>]*>(.*?)</summary>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase));
    private static Lazy<Regex> seeRegex = new(() => new Regex(@"<see\s+cref=""([^""]+)""\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase));
    private static Lazy<Regex> paramrefRegex = new(() => new Regex(@"<paramref\s+name=""([^""]+)""\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase));
    private static Lazy<Regex> typeparamrefRegex = new(() => new Regex(@"<typeparamref\s+name=""([^""]+)""\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase));
    private static Lazy<Regex> tagsRegex = new(() => new Regex(@"<.*?>", RegexOptions.Compiled | RegexOptions.Singleline));
}
