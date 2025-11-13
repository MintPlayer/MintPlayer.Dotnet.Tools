using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Tools;

public static class SymbolExtensions
{
    public static IEnumerable<IPropertySymbol> GetAllProperties(this INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol is null) return [];

        return typeSymbol.GetAllBaseTypes()
            .SelectMany(t => t.GetMembers().OfType<IPropertySymbol>());
    }

    public static IEnumerable<INamedTypeSymbol> GetAllBaseTypes(this INamedTypeSymbol? typeSymbol)
    {
        while (typeSymbol is { })
        {
            yield return typeSymbol;
            typeSymbol = typeSymbol.BaseType;
        }
    }

    /// <summary>
    /// Creates a path specification representing the namespace and parent type hierarchy for the specified named type
    /// symbol.
    /// </summary>
    /// <remarks>The returned path specification includes the fully qualified namespace and all containing
    /// parent types that are classes or structs. Partial type information is included for parent types declared as
    /// partial. This method throws an <see cref="OperationCanceledException"/> if the operation is canceled via the
    /// provided <paramref name="cancellationToken"/>.</remarks>
    /// <param name="typeSymbol">The named type symbol for which to generate the path specification. Must not be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="PathSpec"/> instance containing the namespace and parent type information for <paramref
    /// name="typeSymbol"/>; or <see langword="null"/> if the path specification cannot be determined.</returns>
    public static PathSpec? GetPathSpec(this INamedTypeSymbol typeSymbol, CancellationToken cancellationToken = default)
    {
        var parents = new List<PathSpecElement>();
        var ns = string.Empty;
        while (typeSymbol is { })
        {
            cancellationToken.ThrowIfCancellationRequested();

            ns = typeSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
            if (typeSymbol.ContainingSymbol is INamedTypeSymbol namedTypeSymbol && new[] { TypeKind.Class, TypeKind.Struct }.Contains(namedTypeSymbol.TypeKind))
            {
                parents.Add(new PathSpecElement
                {
                    Name = namedTypeSymbol.Name,
                    IsPartial = namedTypeSymbol.DeclaringSyntaxReferences.Select(r => r.GetSyntax(cancellationToken)).All(s => s switch
                    {
                        ClassDeclarationSyntax cds => cds.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)),
                        StructDeclarationSyntax sds => sds.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)),
                        _ => false
                    }),
                    Type = namedTypeSymbol.TypeKind switch { TypeKind.Class => EPathSpecType.Class, _ => EPathSpecType.Struct },
                });
            }

            typeSymbol = typeSymbol.ContainingType;
        }

        return new PathSpec
        {
            ContainingNamespace = ns,
            Parents = parents.ToArray(),
        };
    }

    public static Stack<IDisposableWriterIndent> OpenPathSpec(this IndentedTextWriter writer, PathSpec? pathSpec)
    {
        var stack = new Stack<IDisposableWriterIndent>();
        if (pathSpec?.Parents is not { Length: > 0 })
            return stack;

        foreach (var parent in pathSpec.Parents.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Reverse())
        {
            var keyword = parent.Type == EPathSpecType.Struct ? "struct" : "class";
            stack.Push(writer.OpenBlock($"partial {keyword} {parent.Name}"));
        }

        return stack;
    }

    public static void ClosePathSpec(this Stack<IDisposableWriterIndent> stack)
    {
        while (stack.Count > 0)
        {
            stack.Pop().Dispose();
        }
    }
}

[ValueComparer(typeof(PathSpecValueComparer))]
public class PathSpec
{
    public string? ContainingNamespace { get; set; }
    public PathSpecElement[] Parents { get; set; }
}

[ValueComparer(typeof(PathSpecElementValueComparer))]
public class PathSpecElement
{
    public string? Name { get; set; }
    public EPathSpecType Type { get; set; }
    public bool IsPartial { get; set; }
}

public enum EPathSpecType
{
    Class,
    Struct,
}