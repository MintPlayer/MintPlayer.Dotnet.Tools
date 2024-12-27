using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Attributes;
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
            var classSyntaxRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Locate the class declaration
            var classDecl = classSyntaxRoot?.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().First();
            if (classDecl == null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add missing members to interface",
                    createChangedSolution: ct => AddMissingMembersToInterfaceAcrossProjects(context, classDecl, ct),
                    equivalenceKey: "AddMissingMembers"),
                diagnostic);
        }

        private async Task<Solution> AddMissingMembersToInterfaceAcrossProjects(CodeFixContext cfContext, ClassDeclarationSyntax classDeclaration, CancellationToken cancellationToken)
        {
            var solution = cfContext.Document.Project.Solution;
            var classDocument = cfContext.Document;
            var classProject = classDocument.Project;

            var classSyntaxRoot = await classDocument.GetSyntaxRootAsync(cancellationToken);
            var classSemanticModel = await classDocument.GetSemanticModelAsync(cancellationToken);

            var classSymbol = classSemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
            var interfaceSymbol = classSymbol?.Interfaces.FirstOrDefault();
            if (interfaceSymbol == null) return solution;

            var interfaceFirstLocation = interfaceSymbol.Locations[0];
            var interfaceDocument = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == interfaceFirstLocation.SourceTree?.FilePath);
            if (interfaceDocument == null) return solution;

            var interfaceSyntaxRoot = await interfaceDocument.GetSyntaxRootAsync(cancellationToken);
            var interfaceSemanticModel = await interfaceDocument.GetSemanticModelAsync(cancellationToken);
            if (interfaceSyntaxRoot == null || interfaceSemanticModel == null) return solution;

            // Determine missing members
            var classMembers = classSymbol?.GetMembers().Where(m => m.DeclaredAccessibility == Accessibility.Public);
            var interfaceMembers = interfaceSymbol.GetMembers();

            var missingMembers = classMembers
                .Where(cm => !interfaceMembers.Any(im => im.Name == cm.Name) && cm.CanBeReferencedByName)
                .Where(cm => cm.GetAttributes().All(attr => attr.AttributeClass?.Name != nameof(NoInterfaceMemberAttribute)))
                .Select(cm => CreateInterfaceMember(cm));

            // Find the interface declaration in the syntax tree
            var interfaceNode = interfaceSyntaxRoot.DescendantNodes()
                .OfType<InterfaceDeclarationSyntax>()
                .FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(interfaceSemanticModel.GetDeclaredSymbol(i, cancellationToken), interfaceSymbol));
            if (interfaceNode == null) return solution;

            // Add missing members to the interface
            var updatedInterfaceNode = interfaceNode.AddMembers(missingMembers.ToArray());
            var updatedRoot = interfaceSyntaxRoot.ReplaceNode(interfaceNode, updatedInterfaceNode);

            return solution
                .WithDocumentSyntaxRoot(interfaceDocument.Id, updatedRoot);
        }

        private MemberDeclarationSyntax CreateInterfaceMember(ISymbol member)
        {
            return member switch
            {
                IMethodSymbol methodSymbol => SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.ParseTypeName(methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                    methodSymbol.Name)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),

                IPropertySymbol propertySymbol => SyntaxFactory.PropertyDeclaration(
                    SyntaxFactory.ParseTypeName(propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
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
