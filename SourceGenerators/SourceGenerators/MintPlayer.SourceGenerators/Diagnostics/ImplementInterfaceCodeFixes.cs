using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;

namespace MintPlayer.SourceGenerators.Diagnostics;

/// <summary>IMPLINTF001: adds <c>partial</c> to the type and to every containing type that lacks it.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakePartialCodeFixProvider)), Shared]
public class MakePartialCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["IMPLINTF001"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        if (root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault() is not { } declaration)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Make partial",
                createChangedDocument: ct => Task.FromResult(context.Document.WithSyntaxRoot(ImplementInterfaceSyntax.MakePartial(root, declaration))),
                equivalenceKey: "MakePartial"),
            diagnostic);
    }
}

/// <summary>
/// CS0535 on an interface in a base list: implements it with <c>[ImplementInterface]</c> instead. Adds the interface
/// to an existing non-explicit attribute, or adds one, makes the type partial, and keeps the base-list entry.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImplementWithAttributeCodeFixProvider)), Shared]
public class ImplementWithAttributeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["CS0535"];

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null) return;

        var diagnostic = context.Diagnostics.First();
        var baseType = root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<BaseTypeSyntax>().FirstOrDefault();
        if (baseType?.Parent?.Parent is not TypeDeclarationSyntax declaration) return;
        if (semanticModel.GetTypeInfo(baseType.Type, context.CancellationToken).Type is not INamedTypeSymbol { TypeKind: TypeKind.Interface } @interface) return;
        if (semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } type) return;

        // Offered only when the attribute would implement something: a missing read/write property, or anything at
        // all on an abstract class (which gets abstract members).
        var missing = new[] { @interface }.Concat(@interface.AllInterfaces)
            .SelectMany(i => i.GetMembers())
            .Where(m => m.IsAbstract && m is IPropertySymbol or IMethodSymbol { MethodKind: MethodKind.Ordinary } or IEventSymbol)
            .Where(m => type.FindImplementationForInterfaceMember(m) is null)
            .ToArray();
        var helps = type.IsAbstract && type.TypeKind == TypeKind.Class
            ? missing.Any(m => !m.IsStatic)
            : missing.Any(m => m is IPropertySymbol { IsIndexer: false, GetMethod: not null, SetMethod: not null });
        if (!helps) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Implement '{@interface.Name}' with [ImplementInterface]",
                createChangedDocument: ct => Task.FromResult(context.Document.WithSyntaxRoot(Apply(root, declaration, baseType.Type))),
                equivalenceKey: "ImplementWithAttribute"),
            diagnostic);
    }

    private static SyntaxNode Apply(SyntaxNode root, TypeDeclarationSyntax declaration, TypeSyntax interfaceType)
    {
        var newLine = ImplementInterfaceSyntax.NewLine(root);
        var typeOf = SyntaxFactory.AttributeArgument(SyntaxFactory.TypeOfExpression(interfaceType.WithoutTrivia()));

        var existing = declaration.AttributeLists
            .SelectMany(l => l.Attributes)
            .FirstOrDefault(a => IsPlainImplementInterface(a) && a.ArgumentList?.Arguments.All(arg => arg.NameEquals is null || arg.NameEquals.Name.Identifier.ValueText != "Explicit") != false);

        TypeDeclarationSyntax updated;
        if (existing is not null)
        {
            // Appended among the typeof arguments, before any named argument (CS1016 otherwise).
            var arguments = existing.ArgumentList?.Arguments ?? default;
            var firstNamed = arguments.IndexOf(a => a.NameEquals is not null);
            arguments = firstNamed < 0 ? arguments.Add(typeOf) : arguments.Insert(firstNamed, typeOf);
            updated = declaration.ReplaceNode(existing, existing.WithArgumentList(SyntaxFactory.AttributeArgumentList(arguments)));
        }
        else
        {
            var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("ImplementInterface"),
                SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(typeOf)));
            updated = ImplementInterfaceSyntax.AddAttribute(declaration, attribute, newLine);
        }

        // A replacement node carries no tracking annotation of the original, so the updated declaration is found
        // again by an annotation of its own.
        var marker = new SyntaxAnnotation();
        var edited = root.ReplaceNode(declaration, updated.WithAdditionalAnnotations(marker));
        edited = ImplementInterfaceSyntax.MakePartial(edited, edited.GetAnnotatedNodes(marker).OfType<TypeDeclarationSyntax>().Single());
        return ImplementInterfaceSyntax.EnsureUsing(edited, "MintPlayer.SourceGenerators.Attributes", newLine);
    }

    private static bool IsPlainImplementInterface(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            QualifiedNameSyntax q => q.Right,
            AliasQualifiedNameSyntax a => a.Name,
            var n => n,
        };
        return name is IdentifierNameSyntax { Identifier.ValueText: "ImplementInterface" or "ImplementInterfaceAttribute" };
    }
}

/// <summary>IMPLINTF008: removes a hand-written property that <c>[ImplementInterface]</c> generates identically.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveRedundantPropertyCodeFixProvider)), Shared]
public class RemoveRedundantPropertyCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticRules.RedundantImplementInterfaceProperty.Id];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        if (root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault() is not { } property)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Remove '{property.Identifier.ValueText}'; [ImplementInterface] generates it",
                createChangedDocument: ct => Task.FromResult(context.Document.WithSyntaxRoot(root.RemoveNode(property, SyntaxRemoveOptions.KeepNoTrivia)!)),
                equivalenceKey: "RemoveRedundantProperty"),
            diagnostic);
    }
}

/// <summary>IMPLINTF009: deletes a hand-written member that has an interface member's name but does not implement it.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DeleteMismatchedMemberCodeFixProvider)), Shared]
public class DeleteMismatchedMemberCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["IMPLINTF009"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var node = root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        if (root is null || node is null) return;

        // One variable of a multi-variable field goes on its own; anything else takes its whole declaration.
        SyntaxNode? target = node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault() is { Parent: VariableDeclarationSyntax { Variables.Count: > 1 } } variable
            ? variable
            : node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault(m => m is not BaseTypeDeclarationSyntax);
        if (target is null) return;

        var name = diagnostic.GetMessage().Split('\'') is { Length: > 1 } parts ? parts[1] : "member";
        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Delete '{name}'; [ImplementInterface] implements it",
                createChangedDocument: ct => Task.FromResult(context.Document.WithSyntaxRoot(root.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia)!)),
                equivalenceKey: "DeleteMismatchedMember"),
            diagnostic);
    }
}

/// <summary>Syntax edits shared by the <c>[ImplementInterface]</c> code fixes.</summary>
internal static class ImplementInterfaceSyntax
{
    /// <summary>The document's own line break, so an edit does not mix CRLF into an LF file.</summary>
    public static string NewLine(SyntaxNode root)
        => root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia)) is { } eol && eol.ToString() is { Length: > 0 } text
            ? text : "\r\n";

    /// <summary>Adds <c>partial</c> to <paramref name="declaration"/> and every containing type that lacks it.</summary>
    public static SyntaxNode MakePartial(SyntaxNode root, TypeDeclarationSyntax declaration)
    {
        var targets = declaration.AncestorsAndSelf().OfType<TypeDeclarationSyntax>()
            .Where(t => !t.Modifiers.Any(SyntaxKind.PartialKeyword))
            .ToArray();
        return targets.Length == 0 ? root : root.ReplaceNodes(targets, static (_, rewritten) => AddPartial(rewritten));
    }

    private static TypeDeclarationSyntax AddPartial(TypeDeclarationSyntax declaration)
    {
        // `partial` goes last, directly before the keyword. With no modifiers the keyword carries the line's
        // indentation, which moves to `partial` so the result is not "partial\n    class".
        if (declaration.Modifiers.Count == 0)
        {
            var partial = SyntaxFactory.Token(declaration.Keyword.LeadingTrivia, SyntaxKind.PartialKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space));
            return declaration
                .WithKeyword(declaration.Keyword.WithLeadingTrivia())
                .WithModifiers(SyntaxFactory.TokenList(partial));
        }
        return declaration.WithModifiers(declaration.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space)));
    }

    /// <summary>Adds <paramref name="attribute"/> as its own list on its own line, above the declaration.</summary>
    public static TypeDeclarationSyntax AddAttribute(TypeDeclarationSyntax declaration, AttributeSyntax attribute, string newLine)
    {
        var list = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));
        var leading = declaration.GetLeadingTrivia();
        var indentation = SyntaxFactory.TriviaList(leading.Reverse().TakeWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse());

        if (declaration.AttributeLists.Count > 0)
            return declaration.WithAttributeLists(declaration.AttributeLists.Add(list.WithLeadingTrivia(indentation).WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine))));

        // The declaration's leading trivia (comments, blank lines) moves to the attribute, and the line after it is
        // indented like the original.
        var stripped = declaration.WithLeadingTrivia(indentation);
        return stripped.WithAttributeLists(SyntaxFactory.SingletonList(list.WithLeadingTrivia(leading).WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine))));
    }

    public static SyntaxNode EnsureUsing(SyntaxNode root, string @namespace, string newLine)
    {
        if (root is not CompilationUnitSyntax unit) return root;
        var hasUsing = unit.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(u => u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None) && u.Name?.ToString() == @namespace);
        if (hasUsing) return root;

        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(@namespace))
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine));
        if (unit.Usings.Count == 0)
            directive = directive.WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine), SyntaxFactory.EndOfLine(newLine));
        return unit.WithUsings(unit.Usings.Add(directive));
    }
}
