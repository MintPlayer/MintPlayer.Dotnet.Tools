using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MintPlayer.SourceGenerators.Diagnostics.CodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InterfaceCodeFixProvider))]
    public class InterfaceCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create("INTF001");

        public override FixAllProvider GetFixAllProvider() =>
            WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Locate the class declaration
            var classDecl = root?.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().First();
            if (classDecl == null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add missing members to interface",
                    createChangedDocument: c => AddMissingMembersToInterface(context.Document, classDecl, c),
                    //createChangedSolution: async (c) =>
                    //{
                    //    var semanticModel = await context.Document.GetSemanticModelAsync(c).ConfigureAwait(false);
                    //    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, c);
                    //    var interfaceSymbol = classSymbol.Interfaces.First(); // Adjust as necessary

                    //    // Collect missing members
                    //    var missingMembers = classSymbol.GetMembers()
                    //        .Where(m => !interfaceSymbol.GetMembers().Any(im => im.Name == m.Name));

                    //    // Update the interface in its project
                    //    var updatedDocument = await UpdateExternalInterfaceAsync(
                    //        context.Document.Project.Solution,
                    //        interfaceSymbol,
                    //        missingMembers,
                    //        c);

                    //    return updatedDocument?.Project.Solution ?? context.Document.Project.Solution;
                    //},
                    equivalenceKey: "AddMissingMembers"),
                diagnostic);
        }

        //private async Task<Document> UpdateExternalInterfaceAsync(Solution solution, INamedTypeSymbol interfaceSymbol, IEnumerable<ISymbol> missingMembers, CancellationToken cancellationToken)
        //{
        //    // Locate the document containing the interface
        //    var document = solution.GetDocument(interfaceSymbol.Locations.First().SourceTree);
        //    if (document == null)
        //        return null;

        //    // Get the syntax root and semantic model of the document
        //    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        //    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        //    // Find the interface declaration
        //    var interfaceDecl = root.DescendantNodes()
        //        .OfType<InterfaceDeclarationSyntax>()
        //        .FirstOrDefault(i => semanticModel.GetDeclaredSymbol(i, cancellationToken)?.Equals(interfaceSymbol) == true);

        //    if (interfaceDecl == null)
        //        return null;

        //    // Create new members
        //    var newMembers = missingMembers.Select(CreateInterfaceMember).ToArray();

        //    // Update the interface
        //    var updatedInterfaceDecl = interfaceDecl.AddMembers(newMembers);
        //    var updatedRoot = root.ReplaceNode(interfaceDecl, updatedInterfaceDecl);

        //    return document.WithSyntaxRoot(updatedRoot);
        //}


        private async Task<Document> AddMissingMembersToInterface(Document document, ClassDeclarationSyntax classDecl, CancellationToken cancellationToken)
        {
            // Implement logic to update the interface here
            // Use Roslyn's SyntaxFactory to create the new interface members

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            // Locate the implemented interface
            var classSymbol = semanticModel?.GetDeclaredSymbol(classDecl, cancellationToken) as INamedTypeSymbol;
            var interfaceSymbol = classSymbol?.Interfaces.FirstOrDefault();
            if (interfaceSymbol == null)
                return document;

            // Determine missing members
            var classMembers = classSymbol?.GetMembers().Where(m => m.DeclaredAccessibility == Accessibility.Public);
            var interfaceMembers = interfaceSymbol.GetMembers();

            var missingMembers = classMembers
                .Where(cm => !interfaceMembers.Any(im => im.Name == cm.Name) && cm.CanBeReferencedByName)
                .Select(cm => CreateInterfaceMember(cm));

            // Find the interface declaration in the syntax tree
            var interfaceNode = root.DescendantNodes()
                .OfType<InterfaceDeclarationSyntax>()
                .FirstOrDefault(i => semanticModel.GetDeclaredSymbol(i, cancellationToken)?.Equals(interfaceSymbol) == true);

            if (interfaceNode == null)
                return document;

            // Add missing members to the interface
            var updatedInterfaceNode = interfaceNode.AddMembers(missingMembers.ToArray());
            var updatedRoot = root.ReplaceNode(interfaceNode, updatedInterfaceNode);

            return document.WithSyntaxRoot(updatedRoot);
        }

        private MemberDeclarationSyntax CreateInterfaceMember(ISymbol member)
        {
            return member switch
            {
                IMethodSymbol methodSymbol => SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.ParseTypeName(methodSymbol.ReturnType.ToDisplayString()),
                    methodSymbol.Name)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),

                IPropertySymbol propertySymbol => SyntaxFactory.PropertyDeclaration(
                    SyntaxFactory.ParseTypeName(propertySymbol.Type.ToDisplayString()),
                    propertySymbol.Name)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .AddAccessorListAccessors(
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))),

                _ => throw new NotImplementedException("Member type not supported")
            };
        }
    }
}
