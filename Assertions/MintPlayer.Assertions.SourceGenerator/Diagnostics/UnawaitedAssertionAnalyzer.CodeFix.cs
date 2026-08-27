using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// Code fix for MPA0001: prepends <c>await</c> to the discarded assertion. When the containing
/// method, local function or lambda is not <c>async</c>, the fix also adds the <c>async</c>
/// modifier (best-effort) and rewrites a <c>void</c> return type to
/// <c>System.Threading.Tasks.Task</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnawaitedAssertionCodeFixProvider)), Shared]
public class UnawaitedAssertionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("MPA0001");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var statement = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (statement is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Await the assertion",
                createChangedDocument: ct => AwaitAssertionAsync(context.Document, statement, ct),
                equivalenceKey: "AwaitAssertion"),
            diagnostic);
    }

    private static async Task<Document> AwaitAssertionAsync(Document document, ExpressionStatementSyntax statement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var expression = statement.Expression;
        var awaitExpression = SyntaxFactory.AwaitExpression(
            SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
                .WithLeadingTrivia(expression.GetLeadingTrivia())
                .WithTrailingTrivia(SyntaxFactory.Space),
            expression.WithoutLeadingTrivia());
        var newStatement = statement.WithExpression(awaitExpression);

        var enclosing = statement.Ancestors().FirstOrDefault(a =>
            a is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);

        var newRoot = enclosing switch
        {
            MethodDeclarationSyntax method when !method.Modifiers.Any(SyntaxKind.AsyncKeyword) =>
                root.ReplaceNode(method, MakeAsync(method.ReplaceNode(statement, newStatement))),
            LocalFunctionStatementSyntax localFunction when !localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword) =>
                root.ReplaceNode(localFunction, MakeAsync(localFunction.ReplaceNode(statement, newStatement))),
            LambdaExpressionSyntax lambda when lambda.AsyncKeyword.IsKind(SyntaxKind.None) =>
                root.ReplaceNode(lambda, MakeAsync(lambda.ReplaceNode(statement, newStatement))),
            _ => root.ReplaceNode(statement, newStatement),
        };

        return document.WithSyntaxRoot(newRoot);
    }

    private static MethodDeclarationSyntax MakeAsync(MethodDeclarationSyntax method)
    {
        method = AddAsyncModifier(method.Modifiers, method.ReturnType,
            (modifiers, returnType) => method.WithModifiers(modifiers).WithReturnType(returnType));
        return method.WithReturnType(WidenVoidToTask(method.ReturnType));
    }

    private static LocalFunctionStatementSyntax MakeAsync(LocalFunctionStatementSyntax localFunction)
    {
        localFunction = AddAsyncModifier(localFunction.Modifiers, localFunction.ReturnType,
            (modifiers, returnType) => localFunction.WithModifiers(modifiers).WithReturnType(returnType));
        return localFunction.WithReturnType(WidenVoidToTask(localFunction.ReturnType));
    }

    private static LambdaExpressionSyntax MakeAsync(LambdaExpressionSyntax lambda)
    {
        var firstToken = lambda.GetFirstToken();
        var asyncToken = SyntaxFactory.Token(SyntaxKind.AsyncKeyword)
            .WithLeadingTrivia(firstToken.LeadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.Space);
        var stripped = lambda.ReplaceToken(firstToken, firstToken.WithLeadingTrivia());
        return (LambdaExpressionSyntax)stripped.WithModifiers(stripped.Modifiers.Insert(0, asyncToken));
    }

    /// <summary>
    /// Inserts an <c>async</c> token at the end of <paramref name="modifiers"/>. When there are no
    /// modifiers, the return type's leading trivia (indentation, doc comments) is moved onto the
    /// async token so it stays in front of the declaration.
    /// </summary>
    private static T AddAsyncModifier<T>(SyntaxTokenList modifiers, TypeSyntax returnType, Func<SyntaxTokenList, TypeSyntax, T> update)
    {
        var asyncToken = SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space);
        if (modifiers.Count == 0)
        {
            return update(
                SyntaxFactory.TokenList(asyncToken.WithLeadingTrivia(returnType.GetLeadingTrivia())),
                returnType.WithoutLeadingTrivia());
        }

        return update(modifiers.Add(asyncToken), returnType);
    }

    private static TypeSyntax WidenVoidToTask(TypeSyntax returnType)
    {
        if (returnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
            return SyntaxFactory.ParseTypeName("System.Threading.Tasks.Task").WithTriviaFrom(returnType);
        return returnType;
    }
}
