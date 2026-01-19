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

    /// <summary>
    /// Transforms a <see cref="TypeKind"/> to its c# plain counterpart.
    /// </summary>
    public static string? ToPlaintext(this TypeKind kind)
    {
        switch (kind)
        {
            case TypeKind.Class: return "class";
            case TypeKind.Delegate: return "delegate";
            case TypeKind.Dynamic: return "dynamic";
            case TypeKind.Enum: return "enum";
            case TypeKind.Interface: return "interface";
            case TypeKind.Struct: return "struct";
            default:
                return null;
        }
    }

    /// <summary>
    /// Recreates the entire hierarchy of a nested class/struct
    /// </summary>
    /// <param name="writer">Indented text writer</param>
    /// <param name="pathSpec">The result of <see cref="SymbolExtensions.GetPathSpec(INamedTypeSymbol, CancellationToken)"/></param>
    /// <returns>An object that closes code-blocks when it's being disposed.</returns>
    public static IDisposablePathSpecStack OpenPathSpec(this IndentedTextWriter writer, PathSpec? pathSpec)
    {
        var stack = new Stack<IDisposableWriterIndent>();
        if (pathSpec?.Parents is not { Length: > 0 })
            return new PathSpecStack(stack);

        foreach (var parent in pathSpec.Parents.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Reverse())
        {
            var keyword = parent.Type == EPathSpecType.Struct ? "struct" : "class";
            stack.Push(writer.OpenBlock($"partial {keyword} {parent.Name}"));
        }

        return new PathSpecStack(stack);
    }

    /// <summary>
    /// Checks if the type already has a constructor with the specified parameter types.
    /// Used to avoid generating duplicate constructors.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to check for existing constructors.</param>
    /// <param name="parameterTypes">The fully qualified parameter types to match against.</param>
    /// <returns>True if a constructor with matching parameter types exists; otherwise false.</returns>
    public static bool HasMatchingConstructor(this ISymbol? typeSymbol, params IList<string> parameterTypes)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        foreach (var constructor in namedType.Constructors)
        {
            if (constructor.IsStatic)
                continue;

            if (constructor.Parameters.Length != parameterTypes.Count)
                continue;

            // Check if all parameter types match (in order)
            var matches = true;
            for (int i = 0; i < constructor.Parameters.Length; i++)
            {
                var existingType = constructor.Parameters[i].Type.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters));

                if (existingType != parameterTypes[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }
}

public interface IDisposablePathSpecStack : IDisposable { }

internal class PathSpecStack : IDisposablePathSpecStack
{
    private readonly Stack<IDisposableWriterIndent> stack;
    public PathSpecStack(Stack<IDisposableWriterIndent> stack)
    {
        this.stack = stack;
    }

    public void Dispose()
    {
        while (stack.Count > 0)
            stack.Pop().Dispose();
    }
}

[ValueComparer(typeof(PathSpecValueComparer))]
public class PathSpec
{
    public string? ContainingNamespace { get; set; }
    public PathSpecElement[] Parents
    {
        get;
        set
        {
            field = value;
            AllPartial = Parents.All(p => !new[] { EPathSpecType.Class, EPathSpecType.Struct }.Contains(p.Type) || p.IsPartial);
        }
    } = [];
    public bool AllPartial { get; private set; }
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

    // TODO: interface, enum
}