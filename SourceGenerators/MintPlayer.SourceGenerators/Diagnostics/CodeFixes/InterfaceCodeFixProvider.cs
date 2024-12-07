using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
                    equivalenceKey: "AddMissingMembers"),
                diagnostic);
        }

        private Task<Document> AddMissingMembersToInterface(Document document, ClassDeclarationSyntax classDecl, CancellationToken cancellationToken)
        {
            // Implement logic to update the interface here
            // Use Roslyn's SyntaxFactory to create the new interface members
            return Task.FromResult(document);
        }
    }
}
