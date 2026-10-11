using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    /// Creates a path specification representing the namespace, the containing types and the type itself for the
    /// specified named type symbol.
    /// </summary>
    /// <remarks>The returned path specification holds the namespace (empty for the global namespace), every
    /// containing type (class, struct, record, record struct or interface) and the type itself as
    /// <see cref="PathSpec.Target"/>, each with its kind, type parameters (with variance) and whether it is partial or
    /// file-local. <see cref="PathSpec.UnsupportedReason"/> says why a generated partial cannot re-declare the type
    /// (not partial, file-local, or an enum, delegate or union). Re-declare the type with
    /// <c>OpenPathSpec(spec, includeTarget: true)</c>. This method throws an <see cref="OperationCanceledException"/>
    /// if the operation is canceled via the provided <paramref name="cancellationToken"/>.</remarks>
    /// <param name="typeSymbol">The named type symbol for which to generate the path specification. Must not be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="PathSpec"/> for <paramref name="typeSymbol"/>. Never null in practice: the nullable return
    /// type is kept for source compatibility.</returns>
    public static PathSpec? GetPathSpec(this INamedTypeSymbol typeSymbol, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Parents are stored innermost first (the historical order); OpenPathSpec reverses them.
        var parents = new List<PathSpecElement>();
        for (var parent = typeSymbol.ContainingType; parent is not null; parent = parent.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parents.Add(CreateElement(parent, cancellationToken));
        }

        var target = CreateElement(typeSymbol, cancellationToken);
        var ns = typeSymbol.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted))
            : string.Empty;

        // The first reason wins, in the order a user fixes them: the type itself, then its parents inside out.
        var reason = EPathSpecUnsupportedReason.None;
        string? offender = null;
        foreach (var (element, isTarget) in new[] { (target, true) }.Concat(parents.Select(p => (p, false))))
        {
            var r = element.KindUnsupported ? EPathSpecUnsupportedReason.UnsupportedKind
                : element.IsFileLocal ? EPathSpecUnsupportedReason.FileLocal
                : !element.IsPartial ? (isTarget ? EPathSpecUnsupportedReason.TargetNotPartial : EPathSpecUnsupportedReason.ParentNotPartial)
                : EPathSpecUnsupportedReason.None;
            if (r != EPathSpecUnsupportedReason.None)
            {
                reason = r;
                offender = element.Name?.TrimStart('@'); // for messages: the name as the user reads it
                break;
            }
        }

        return new PathSpec
        {
            ContainingNamespace = ns,
            Parents = parents.ToArray(),
            Target = target,
            UnsupportedReason = reason,
            UnsupportedTypeName = offender,
        };
    }

    private static PathSpecElement CreateElement(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var declarations = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(cancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .ToArray();

        // A C# 15 `union` binds to TypeKind.Struct; only its syntax tells it apart (ITypeSymbol.IsUnion is experimental).
        var isUnion = declarations.Any(d => d.Keyword.ValueText == "union");
        var kind = type switch
        {
            _ when isUnion => (EPathSpecType?)null,
            { IsRecord: true, IsValueType: true } => EPathSpecType.RecordStruct,
            { IsRecord: true } => EPathSpecType.Record,
            { TypeKind: TypeKind.Struct } => EPathSpecType.Struct,
            { TypeKind: TypeKind.Interface } => EPathSpecType.Interface,
            { TypeKind: TypeKind.Class } => EPathSpecType.Class,
            _ => null, // enum, delegate, union: nothing to re-declare as a partial
        };

        return new PathSpecElement
        {
            Name = EscapeIdentifier(type.Name),
            Type = kind ?? EPathSpecType.Class,
            KindUnsupported = kind is null,
            // C# requires `partial` on every part; one part without it is already CS0260 in the user's code, so
            // "any part" avoids a second report for the same mistake and matches Equality's IsPartial.
            IsPartial = declarations.Any(d => d.Modifiers.Any(SyntaxKind.PartialKeyword)),
            IsFileLocal = type.IsFileLocal,
            GenericTypeParameters = GetGenericTypeParametersFromSymbol(type),
            TypeParameterNames = type.TypeParameters.Select(tp => tp.Name).ToEquatableArray(),
        };
    }

    /// <summary><paramref name="name"/> prefixed with <c>@</c> when it is a C# keyword, e.g. <c>@event</c>.</summary>
    public static string EscapeIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

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
    /// Gets the generic type parameters from a named type symbol, e.g., "&lt;T, TKey&gt;"
    /// </summary>
    /// <param name="typeSymbol">The type symbol to extract generic parameters from.</param>
    /// <returns>A string like "&lt;T, TKey&gt;" or null if not generic.</returns>
    public static string? GetGenericTypeParametersFromSymbol(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeParameters.Length == 0)
            return null;

        // Variance must be repeated on every part of an interface (CS1067); constraints need not (see S1).
        var typeParams = string.Join(", ", typeSymbol.TypeParameters.Select(tp => tp.Variance switch
        {
            VarianceKind.In => "in ",
            VarianceKind.Out => "out ",
            _ => string.Empty,
        } + EscapeIdentifier(tp.Name)));
        return $"<{typeParams}>";
    }

    /// <summary>
    /// Opens every containing type of a nested type, and with
    /// <paramref name="includeTarget"/> the type itself, as <c>partial</c> blocks with the right keyword
    /// (<c>class</c>, <c>struct</c>, <c>record</c>, <c>record struct</c> or <c>interface</c>), type parameters and
    /// variance. Accessibility, other modifiers and constraints are never repeated. The namespace itself is not opened.
    /// </summary>
    /// <param name="writer">Indented text writer</param>
    /// <param name="pathSpec">The result of <see cref="SymbolExtensions.GetPathSpec(INamedTypeSymbol, CancellationToken)"/></param>
    /// <returns>An object that closes code-blocks when it's being disposed.</returns>
    /// <param name="includeTarget">Also open the type itself, as the innermost block. Off by default so that existing
    /// callers, which write the type's own header, keep their output.</param>
    /// <param name="baseList">Appended to the type's own header, e.g. <c>" : global::System.IEquatable&lt;X&gt;"</c>.</param>
    /// <param name="targetLeadingLines">Lines written directly before the type's own header, e.g. an attribute.</param>
    public static IDisposablePathSpecStack OpenPathSpec(this IndentedTextWriter writer, PathSpec? pathSpec, bool includeTarget = false, string? baseList = null, IEnumerable<string>? targetLeadingLines = null)
    {
        var stack = new Stack<IDisposableWriterIndent>();
        if (pathSpec is null)
            return new PathSpecStack(stack);

        foreach (var parent in pathSpec.Parents.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Reverse())
            stack.Push(writer.OpenBlock(parent.Header));

        if (includeTarget && pathSpec.Target is { } target)
        {
            foreach (var line in targetLeadingLines ?? [])
                writer.WriteLine(line);
            stack.Push(writer.OpenBlock(target.Header + baseList));
        }

        return new PathSpecStack(stack);
    }

    /// <summary>
    /// <paramref name="preferred"/>, or <c>preferred1</c>, <c>preferred2</c>, ... when the type or a containing type
    /// already declares a type parameter of that name (a generated generic member would shadow it: CS0693).
    /// </summary>
    /// <remarks>
    /// For ONE name. Do not append an index to the result to build a numbered series: for <c>T1</c> in scope this
    /// returns <c>T11</c>, and a series <c>T11</c>, <c>T12</c>, ... would collide again. Use
    /// <see cref="UniqueTypeParameterPrefix"/> for that.
    /// </remarks>
    public static string UniqueTypeParameterName(this PathSpec pathSpec, string preferred)
    {
        var inScope = new HashSet<string>(pathSpec.TypeParameterNamesInScope, StringComparer.Ordinal);
        if (!inScope.Contains(preferred)) return preferred;
        for (var i = 1; ; i++)
            if (!inScope.Contains(preferred + i)) return preferred + i;
    }

    /// <summary>
    /// A prefix for a numbered series of type parameters (<c>{prefix}1</c>, <c>{prefix}2</c>, ...) such that
    /// <c>{prefix}{n}</c> is free for EVERY n: no type parameter in scope (the type's own or a containing type's) is
    /// the prefix followed by digits only.
    /// </summary>
    /// <param name="pathSpec">The result of <see cref="GetPathSpec"/> for the type that gets the generic member.</param>
    /// <param name="preferred">The prefix to use when it is free, e.g. <c>"T"</c> or <c>"TArg"</c>.</param>
    /// <returns><paramref name="preferred"/>, or <paramref name="preferred"/> followed by one or more underscores.</returns>
    /// <remarks>
    /// The prefix itself (without digits) is not checked; it is never used as a name on its own.
    /// </remarks>
    public static string UniqueTypeParameterPrefix(this PathSpec pathSpec, string preferred)
    {
        var inScope = pathSpec.TypeParameterNamesInScope.ToArray();
        for (var prefix = preferred; ; prefix += "_")
            if (!inScope.Any(name => IsPrefixWithDigits(name, prefix)))
                return prefix;
    }

    private static bool IsPrefixWithDigits(string name, string prefix)
    {
        if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.Ordinal)) return false;
        for (var i = prefix.Length; i < name.Length; i++)
            if (name[i] is < '0' or > '9') return false;
        return true;
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

/// <summary>
/// The namespace, the containing types and the type itself (<see cref="PathSpec.Target"/>) of a type symbol. Compares by value, so a pipeline model holding a
/// <see cref="PathSpec"/> stays equal to its previous run under <see cref="EqualityComparer{T}.Default"/>.
/// </summary>
public class PathSpec : IEquatable<PathSpec>
{
    public string? ContainingNamespace { get; set; }
    public PathSpecElement[] Parents
    {
        get;
        set
        {
            field = value;
            AllPartial = Parents.All(p => p.IsPartial);
        }
    } = [];
    public bool AllPartial { get; private set; }

    /// <summary>The type itself: re-declared as the innermost block by <c>OpenPathSpec(includeTarget: true)</c>.</summary>
    public PathSpecElement? Target { get; set; }

    /// <summary>Why the type cannot be re-declared as a partial, or <see cref="EPathSpecUnsupportedReason.None"/>.</summary>
    public EPathSpecUnsupportedReason UnsupportedReason { get; set; }

    /// <summary>The unescaped name of the type <see cref="UnsupportedReason"/> is about: the target or one parent.</summary>
    public string? UnsupportedTypeName { get; set; }

    public bool IsSupported => UnsupportedReason == EPathSpecUnsupportedReason.None;

    /// <summary>Every type-parameter name declared by the type and its containing types.</summary>
    public IEnumerable<string> TypeParameterNamesInScope
        => Parents.SelectMany(p => p.TypeParameterNames).Concat(Target?.TypeParameterNames ?? EquatableArray<string>.Empty);

    public bool Equals(PathSpec? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return other.GetType() == GetType()
            && string.Equals(ContainingNamespace, other.ContainingNamespace, StringComparison.Ordinal)
            && ValueEquality.Array(Parents, other.Parents)
            && AllPartial == other.AllPartial
            && Equals(Target, other.Target)
            && UnsupportedReason == other.UnsupportedReason
            && string.Equals(UnsupportedTypeName, other.UnsupportedTypeName, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as PathSpec);

    public override int GetHashCode()
    {
        var h = 17;
        h = ValueEquality.Combine(h, ContainingNamespace is null ? 0 : StringComparer.Ordinal.GetHashCode(ContainingNamespace));
        h = ValueEquality.Combine(h, ValueEquality.ArrayHash(Parents));
        h = ValueEquality.Combine(h, AllPartial ? 1 : 0);
        h = ValueEquality.Combine(h, Target?.GetHashCode() ?? 0);
        h = ValueEquality.Combine(h, (int)UnsupportedReason);
        h = ValueEquality.Combine(h, UnsupportedTypeName is null ? 0 : StringComparer.Ordinal.GetHashCode(UnsupportedTypeName));
        return h;
    }
}

public enum EPathSpecUnsupportedReason
{
    None,
    /// <summary>The type itself lacks <c>partial</c>.</summary>
    TargetNotPartial,
    /// <summary>A containing type lacks <c>partial</c>; <see cref="PathSpec.UnsupportedTypeName"/> names it.</summary>
    ParentNotPartial,
    /// <summary>The type or a containing type is <c>file</c>-local: a partial in a generated file is a different type.</summary>
    FileLocal,
    /// <summary>An enum, delegate or union: nothing a generator can re-declare as a partial of this kind.</summary>
    UnsupportedKind,
}

/// <summary>One type of a <see cref="PathSpec"/>: a containing type, or the <see cref="PathSpec.Target"/>. Compares by value.</summary>
public class PathSpecElement : IEquatable<PathSpecElement>
{
    public string? Name { get; set; }
    public EPathSpecType Type { get; set; }
    public bool IsPartial { get; set; }

    /// <summary>
    /// Generic type parameters for the type, with their variance, e.g., "&lt;TOuter&gt;" or "&lt;in T&gt;"
    /// </summary>
    public string? GenericTypeParameters { get; set; }

    /// <summary>The type-parameter names, unescaped and without variance.</summary>
    public EquatableArray<string> TypeParameterNames { get; set; }

    public bool IsFileLocal { get; set; }

    /// <summary>An enum, delegate or union: <see cref="Type"/> is meaningless.</summary>
    public bool KindUnsupported { get; set; }

    /// <summary>
    /// The keyword to re-declare this type with. A reference record is always <c>record</c>: it is the only spelling
    /// at C# 9, and from C# 10 it combines with parts the user spelled <c>record class</c>.
    /// </summary>
    public string Keyword => Type switch
    {
        EPathSpecType.Struct => "struct",
        EPathSpecType.Record => "record",
        EPathSpecType.RecordStruct => "record struct",
        EPathSpecType.Interface => "interface",
        _ => "class",
    };

    /// <summary><c>partial record struct Name&lt;T&gt;</c>: no accessibility, no other modifiers, no constraints.</summary>
    public string Header => $"partial {Keyword} {Name}{GenericTypeParameters}";

    public bool Equals(PathSpecElement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return other.GetType() == GetType()
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && Type == other.Type
            && IsPartial == other.IsPartial
            && string.Equals(GenericTypeParameters, other.GenericTypeParameters, StringComparison.Ordinal)
            && TypeParameterNames.Equals(other.TypeParameterNames)
            && IsFileLocal == other.IsFileLocal
            && KindUnsupported == other.KindUnsupported;
    }

    public override bool Equals(object? obj) => Equals(obj as PathSpecElement);

    public override int GetHashCode()
    {
        var h = 17;
        h = ValueEquality.Combine(h, Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name));
        h = ValueEquality.Combine(h, (int)Type);
        h = ValueEquality.Combine(h, IsPartial ? 1 : 0);
        h = ValueEquality.Combine(h, GenericTypeParameters is null ? 0 : StringComparer.Ordinal.GetHashCode(GenericTypeParameters));
        h = ValueEquality.Combine(h, TypeParameterNames.GetHashCode());
        h = ValueEquality.Combine(h, IsFileLocal ? 1 : 0);
        h = ValueEquality.Combine(h, KindUnsupported ? 1 : 0);
        return h;
    }
}

public enum EPathSpecType
{
    Class,
    Struct,
    Record,
    RecordStruct,
    Interface,
}