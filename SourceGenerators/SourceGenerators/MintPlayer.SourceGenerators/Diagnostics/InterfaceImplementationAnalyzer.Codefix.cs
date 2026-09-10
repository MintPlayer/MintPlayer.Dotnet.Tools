using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;

namespace MintPlayer.SourceGenerators.Diagnostics;

/// <summary>
/// Declares the member INTF001 reported on one of the interfaces its class implements.
/// </summary>
/// <remarks>
/// <para>
/// The fix reaches across projects: it reports through <c>createChangedSolution</c> because the
/// interface routinely lives in a project the class only references. That direction is the only one
/// that works — Roslyn's analyzer driver silently discards a diagnostic whose location lies in a
/// syntax tree the analyzed compilation does not contain, so the diagnostic must be raised on the
/// class and only the fix may travel.
/// </para>
/// <para>
/// One action is registered per interface the class could sensibly declare the member on. The
/// analyzer reports the member once, against no particular interface, precisely so the choice
/// surfaces here rather than being guessed.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InterfaceCodeFixProvider)), Shared]
public class InterfaceCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticRules.MissingInterfaceMemberRule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();

        // TypeDeclarationSyntax, not ClassDeclarationSyntax: the analyzer gates on TypeKind.Class,
        // which includes records, and RecordDeclarationSyntax is a SIBLING of ClassDeclarationSyntax
        // rather than a subtype. FirstOrDefault, not First: a span left stale by an edit has no
        // type ancestor, and First() threw "Sequence contains no elements" out of the light-bulb
        // path — which the `is null` check below was written expecting not to happen.
        var typeDecl = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (typeDecl is null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return;

        if (semanticModel.GetDeclaredSymbol(typeDecl, context.CancellationToken) is not { } typeSymbol)
            return;

        // The member the diagnostic was reported for, not every member that looks missing. The
        // previous implementation recomputed the whole set and rewrote the interface in one edit,
        // which is what turned "targets the wrong interface" into "moves several members to the
        // wrong interface".
        if (!diagnostic.Properties.TryGetValue(InterfaceMemberCandidates.MemberNameProperty, out var memberName)
            || string.IsNullOrEmpty(memberName))
            return;

        var member = InterfaceMemberCandidates
            .In(typeSymbol, IgnoreAttributeSymbol(semanticModel.Compilation))
            .FirstOrDefault(m => m.Name == memberName);

        if (member is null || CreateInterfaceMember(member) is null)
            return;

        foreach (var iface in InterfaceMemberCandidates.EditableInterfaces(typeSymbol))
        {
            // Captured per iteration; a closure over the loop variable would give every action the
            // last interface.
            var target = iface;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Add '{memberName}' to {target.Name}",
                    createChangedSolution: ct => AddMemberToInterface(context.Document, typeDecl, target, memberName!, ct),
                    // Fully qualified, so two same-named interfaces in different namespaces do not
                    // collide and FixAll groups by the interface actually meant.
                    equivalenceKey: $"AddMissingMember:{target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"),
                diagnostic);
        }
    }

    private static INamedTypeSymbol? IgnoreAttributeSymbol(Compilation compilation)
        => compilation.GetTypeByMetadataName(typeof(Attributes.NoInterfaceMemberAttribute).FullName);

    private async Task<Solution> AddMemberToInterface(
        Document classDocument,
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol interfaceSymbol,
        string memberName,
        CancellationToken cancellationToken)
    {
        var solution = classDocument.Project.Solution;

        var semanticModel = await classDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return solution;

        if (semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is not { } typeSymbol)
            return solution;

        // Already satisfied through a base interface: the analyzer honours the whole hierarchy, and
        // re-declaring an inherited member on the derived interface is CS0108 member hiding in code
        // the analyzer had deliberately accepted.
        if (InterfaceMemberCandidates.MembersOf(interfaceSymbol).Any(m => m.Name == memberName))
            return solution;

        var member = InterfaceMemberCandidates
            .In(typeSymbol, IgnoreAttributeSymbol(semanticModel.Compilation))
            .FirstOrDefault(m => m.Name == memberName);

        if (member is null || CreateInterfaceMember(member) is not { } declaration)
            return solution;

        // Through the symbol, not through a file-path string. The path comparison this replaces was
        // O(every document in the solution), case- and separator-sensitive, and failed SILENTLY —
        // a non-match returned the solution unchanged, which Roslyn wraps in a valid
        // ApplyChangesOperation and presents as success.
        foreach (var reference in interfaceSymbol.DeclaringSyntaxReferences)
        {
            if (solution.GetDocumentId(reference.SyntaxTree) is not { } interfaceDocumentId)
                continue;

            var interfaceDocument = solution.GetDocument(interfaceDocumentId);
            if (interfaceDocument is null)
                continue;

            var interfaceRoot = await interfaceDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (interfaceRoot is null)
                continue;

            if (await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false) is not InterfaceDeclarationSyntax interfaceNode)
                continue;

            var updated = interfaceRoot.ReplaceNode(interfaceNode, interfaceNode.AddMembers(declaration));
            return solution.WithDocumentSyntaxRoot(interfaceDocumentId, updated);
        }

        return solution;
    }

    /// <summary>
    /// The interface declaration for a class member, or <see langword="null"/> for a kind that
    /// cannot be expressed as one.
    /// </summary>
    /// <remarks>
    /// Returns null rather than throwing. This used to end in
    /// <c>throw new NotImplementedException("Member type not supported")</c>, and because the fix's
    /// own filter had drifted from the analyzer's, a public field, event or nested type on the class
    /// reached it — so clicking the light bulb threw out of a public extension point. A code fix
    /// must never throw at the user; declining is always available.
    /// </remarks>
    private static MemberDeclarationSyntax? CreateInterfaceMember(ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol methodSymbol:
                return SyntaxFactory.MethodDeclaration(ReturnType(methodSymbol), methodSymbol.Name)
                    .WithParameterList(SyntaxFactory.ParameterList(
                        SyntaxFactory.SeparatedList(
                            methodSymbol.Parameters.Select(p => SyntaxFactory.Parameter(
                                    SyntaxFactory.Identifier(p.Name))
                                .WithType(SyntaxFactory.ParseTypeName(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))))))
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

            case IPropertySymbol propertySymbol:
                var accessors = Accessors(propertySymbol).ToArray();

                // A property the interface cannot usefully declare — set-only, or somehow neither.
                // Declaring a getter the class does not have is CS0535 against the class being
                // fixed, so decline instead of guessing.
                return accessors.Length == 0
                    ? null
                    : SyntaxFactory.PropertyDeclaration(
                            SyntaxFactory.ParseTypeName(propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                            propertySymbol.Name)
                        .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));

            default:
                return null;
        }
    }

    /// <summary>
    /// The return type of a generated interface method.
    /// </summary>
    /// <remarks>
    /// <c>void</c> needs <see cref="PredefinedTypeSyntax"/>, not
    /// <c>ParseTypeName(returnType.ToDisplayString(...))</c>. The parsed form renders back as the
    /// text "void" — so the fixed source LOOKS right and a <c>Contain("void Extra();")</c>
    /// assertion passes — while the node itself is not a valid return type, and the interface no
    /// longer compiles: <c>CS1547: Keyword 'void' cannot be used in this context</c>. The defect
    /// survived because no test compiled the code the fix produced; the harness now does.
    /// </remarks>
    private static TypeSyntax ReturnType(IMethodSymbol method)
        => method.ReturnsVoid
            ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
            : SyntaxFactory.ParseTypeName(method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    /// <summary>
    /// The accessors the class property actually has.
    /// </summary>
    /// <remarks>
    /// These used to be hardcoded to <c>{ get; set; }</c>. For a get-only class property that
    /// declares an interface member the class does not implement — CS0535 — and for an init-only
    /// one, CS8854. It is the only defect in this rule that turns compiling code into broken code,
    /// and neither the class nor the interface offers a hint that it happened.
    /// </remarks>
    private static IEnumerable<AccessorDeclarationSyntax> Accessors(IPropertySymbol property)
    {
        if (property.GetMethod is not null && property.GetMethod.DeclaredAccessibility == Accessibility.Public)
            yield return Accessor(SyntaxKind.GetAccessorDeclaration);

        if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public } setMethod)
            yield return Accessor(setMethod.IsInitOnly
                ? SyntaxKind.InitAccessorDeclaration
                : SyntaxKind.SetAccessorDeclaration);

        static AccessorDeclarationSyntax Accessor(SyntaxKind kind)
            => SyntaxFactory.AccessorDeclaration(kind)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
    }
}
