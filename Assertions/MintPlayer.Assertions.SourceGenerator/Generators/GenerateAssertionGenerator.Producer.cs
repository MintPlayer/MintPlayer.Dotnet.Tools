using MintPlayer.Assertions.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.Assertions.SourceGenerator.Generators;

internal sealed class GenerateAssertionProducer : Producer
{
    private const string PrimitivesNamespace = "global::MintPlayer.Assertions.Primitives";
    private const string AndConstraint = "global::MintPlayer.Assertions.AndConstraint";

    private readonly EquatableArray<AssertionMethodDeclaration> declarations;

    public GenerateAssertionProducer(EquatableArray<AssertionMethodDeclaration> declarations, string? rootNamespace)
        : base(string.IsNullOrWhiteSpace(rootNamespace) ? "MintPlayer.Assertions.Generated" : rootNamespace!, "GeneratedAssertions.g.cs")
    {
        this.declarations = declarations;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        if (declarations.Count == 0) return;

        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine(Header);
        writer.WriteLine();

        foreach (var namespaceGroup in declarations.GroupBy(d => d.Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(namespaceGroup.Key))
            {
                WriteClasses(writer, namespaceGroup, cancellationToken);
            }
            else
            {
                using (writer.OpenBlock($"namespace {namespaceGroup.Key}"))
                    WriteClasses(writer, namespaceGroup, cancellationToken);
            }
        }
    }

    private static void WriteClasses(IndentedTextWriter writer, IEnumerable<AssertionMethodDeclaration> group, CancellationToken cancellationToken)
    {
        foreach (var classGroup in group.GroupBy(d => d.ContainingTypeName).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var accessibility = classGroup.All(d => d.IsPublic) ? "public" : "internal";

            writer.WriteLine("/// <summary>Fluent assertions generated from the [GenerateAssertion] predicates on this class.</summary>");
            writer.WriteLine("[global::System.CodeDom.Compiler.GeneratedCode(\"MintPlayer.Assertions.SourceGenerator\", \"1.0.0\")]");
            using (writer.OpenBlock($"{accessibility} static class {classGroup.Key}"))
            {
                var first = true;
                foreach (var declaration in classGroup)
                {
                    if (!first) writer.WriteLine();
                    first = false;
                    WriteMethod(writer, declaration);
                }
            }
        }
    }

    private static void WriteMethod(IndentedTextWriter writer, AssertionMethodDeclaration declaration)
    {
        var assertionsType = GetAssertionsType(declaration);
        var parameters = new List<string> { $"this {assertionsType} assertions" };
        parameters.AddRange(declaration.ExtraParameters.Select(p => $"{p.TypeFullName} {Escape(p.Name)}"));
        parameters.Add("string? because = null");
        parameters.Add("params object?[]? becauseArgs");

        var arguments = new List<string> { GetSubjectValue(declaration) };
        arguments.AddRange(declaration.ExtraParameters.Select(p => Escape(p.Name)));

        writer.WriteLine($"/// <summary>Asserts that the subject satisfies {declaration.ContainingTypeFullName.Replace("global::", string.Empty)}.{declaration.MethodName}.</summary>");
        writer.WriteLine($"public static {AndConstraint}<{assertionsType}> {declaration.GeneratedName}({string.Join(", ", parameters)})");
        using (writer.OpenBlock(string.Empty))
        {
            writer.WriteLine($"assertions.Assert().ForCondition({GetNullGuard(declaration)} && {declaration.ContainingTypeFullName}.{declaration.MethodName}({string.Join(", ", arguments)})).BecauseOf(because, becauseArgs)");
            writer.IndentSingleLine($".FailWith(\"Expected {{subject}} to {declaration.Phrase}{{reason}}, but found {{0}}.\", assertions.Subject);");
            writer.WriteLine("return new(assertions);");
        }
    }

    private static string GetAssertionsType(AssertionMethodDeclaration declaration) => declaration.SubjectKind switch
    {
        SubjectKind.String => $"{PrimitivesNamespace}.StringAssertions",
        SubjectKind.Boolean => $"{PrimitivesNamespace}.BooleanAssertions",
        SubjectKind.Numeric => $"{PrimitivesNamespace}.NumericAssertions<{declaration.SubjectTypeFullName}>",
        _ => $"{PrimitivesNamespace}.ObjectAssertions",
    };

    /// <summary>A null subject can never satisfy the predicate, so it short-circuits into the failure.</summary>
    private static string GetNullGuard(AssertionMethodDeclaration declaration) => declaration.SubjectKind switch
    {
        SubjectKind.Boolean or SubjectKind.Numeric => "assertions.Subject.HasValue",
        _ => "assertions.Subject is not null",
    };

    private static string GetSubjectValue(AssertionMethodDeclaration declaration) => declaration.SubjectKind switch
    {
        SubjectKind.Boolean or SubjectKind.Numeric => "assertions.Subject!.Value",
        SubjectKind.String => "assertions.Subject!",
        _ => $"(({declaration.SubjectTypeFullName})assertions.Subject!)",
    };

    private static string Escape(string identifier)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(identifier) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? identifier
            : "@" + identifier;
}
