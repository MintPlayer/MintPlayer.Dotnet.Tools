using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System.Net;
using System.Text.RegularExpressions;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public partial class DescriptionSourceGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<ICompilationCache> valueComparerCacheProvider)
    {
        var xmlCommentProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, ct) => node is ClassDeclarationSyntax
                                      or RecordDeclarationSyntax
                                      or StructDeclarationSyntax
                                      or InterfaceDeclarationSyntax
                                      or EnumDeclarationSyntax
                                      or ConstructorDeclarationSyntax
                                      or PropertyDeclarationSyntax
                                      or EventDeclarationSyntax,
            static (ctx, ct) =>
            {
                if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is INamedTypeSymbol symbol)
                {
                    //var xml = symbol?.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: ct);
                    var xml = ctx.Node.GetLeadingTrivia().ToString();
                    if (string.IsNullOrWhiteSpace(xml) || xml.All(c => c is '\r' or '\n')) return default;
                    if (SanitizeMarkup(xml) is not { Length: > 0 } sanitized) return default;

                    return new Models.SymbolWithMarkups
                    {
                        // TODO: XML documentation might also be used on methods and properties
                        Name = symbol!.Name,
                        TypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        TypeKind = symbol.TypeKind.ToPlaintext(),
                        PathSpec = symbol.GetPathSpec(ct),
                        IsPartial = ctx.Node is TypeDeclarationSyntax typeDeclaration && typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                        MarkupText = sanitized,
                    };
                }

                return default;
            })
            .WithNullableComparer()
            .Where(static item => item is { MarkupText: not null, TypeName: not null, TypeKind: not null, PathSpec.AllPartial: true, IsPartial: true })
            .Collect();

        var xmlCommentSourceProvider = xmlCommentProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new DescriptionsProducer(p.Item1, p.Item2.RootNamespace!));

        context.ProduceCode(xmlCommentSourceProvider);
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
