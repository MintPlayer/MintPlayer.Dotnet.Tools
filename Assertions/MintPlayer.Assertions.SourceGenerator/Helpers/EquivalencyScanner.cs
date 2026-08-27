using Microsoft.CodeAnalysis;
using MintPlayer.Assertions.SourceGenerator.Models;

namespace MintPlayer.Assertions.SourceGenerator.Helpers;

/// <summary>
/// Walks an object graph starting from a candidate type and produces the flat set of types for
/// which member accessors can be emitted.
/// </summary>
/// <remarks>
/// The walk deliberately runs inside the syntax/attribute transform: every call site (or
/// <c>[AssertEquivalency]</c> type) yields a self-contained, structurally comparable result, so
/// the incremental pipeline never has to carry symbols across steps.
/// Anything the generator cannot faithfully emit an accessor for — a scalar, an anonymous type,
/// an inaccessible or open generic type, a collection — is skipped here; the runtime falls back
/// to reflection for those, so skipping is always safe.
/// </remarks>
internal static class EquivalencyScanner
{
    private const int MaxDepth = 12;

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly HashSet<string> ExcludedTypeNames =
    [
        "System.String", "System.Decimal", "System.DateTime", "System.DateTimeOffset",
        "System.DateOnly", "System.TimeOnly", "System.TimeSpan", "System.Guid",
        "System.Uri", "System.Type", "System.Object", "System.Version",
    ];

    public static void Collect(ITypeSymbol? type, Compilation compilation, HashSet<string> visited, List<EquivalencyTypeDeclaration> result, CancellationToken cancellationToken)
        => Collect(type, compilation, visited, result, 0, cancellationToken);

    private static void Collect(ITypeSymbol? type, Compilation compilation, HashSet<string> visited, List<EquivalencyTypeDeclaration> result, int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type is null || depth > MaxDepth) return;

        switch (type)
        {
            case IArrayTypeSymbol array:
                Collect(array.ElementType, compilation, visited, result, depth + 1, cancellationToken);
                return;
            case IPointerTypeSymbol:
            case ITypeParameterSymbol:
            case IFunctionPointerTypeSymbol:
                return;
        }

        if (type is not INamedTypeSymbol named) return;
        if (type.TypeKind is TypeKind.Dynamic or TypeKind.Error or TypeKind.Pointer or TypeKind.Enum or TypeKind.Delegate) return;
        if (named.IsRefLikeType || named.IsStatic || named.IsUnboundGenericType) return;

        // A file-local type ("file class X") is visible only inside its own source file, so the
        // generated registration file cannot name it — and its fully-qualified display string
        // looks deceptively like an ordinary namespace-level type. The same goes for a private or
        // protected nested type. Both keep working through the runtime's reflection fallback.
        if (!IsReferenceableFromGeneratedCode(compilation, named)) return;

        // Nullable<T> — accessors are registered for the underlying type.
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            Collect(named.TypeArguments.FirstOrDefault(), compilation, visited, result, depth + 1, cancellationToken);
            return;
        }

        if (IsScalar(named)) return;

        // Anonymous types cannot be named in generated code, but their members can still lead to
        // types that can be.
        if (named.IsAnonymousType)
        {
            foreach (var property in named.GetMembers().OfType<IPropertySymbol>())
                Collect(property.Type, compilation, visited, result, depth + 1, cancellationToken);
            return;
        }

        if (ContainsTypeParameter(named)) return;

        // An interface never matches a runtime type in the registry, but the types behind its
        // type arguments (IList<Address>) still deserve accessors.
        if (named.TypeKind == TypeKind.Interface)
        {
            foreach (var argument in EnumerateCollectionTypeArguments(named))
                Collect(argument, compilation, visited, result, depth + 1, cancellationToken);
            return;
        }

        // Collections are handled structurally by the equivalency engine; recurse into the
        // element/key/value types instead of registering the collection itself.
        if (IsEnumerable(compilation, named))
        {
            foreach (var argument in EnumerateCollectionTypeArguments(named))
                Collect(argument, compilation, visited, result, depth + 1, cancellationToken);
            return;
        }

        var typeFullName = named.ToDisplayString(FullyQualified);
        if (!visited.Add(typeFullName)) return;

        var members = new List<MemberDeclaration>();
        var memberTypes = new List<ITypeSymbol>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        for (INamedTypeSymbol? current = named; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member.IsStatic || member.IsImplicitlyDeclared) continue;
                if (member.DeclaredAccessibility != Accessibility.Public) continue;

                switch (member)
                {
                    case IPropertySymbol { IsIndexer: false, ReturnsByRef: false, ReturnsByRefReadonly: false, GetMethod: { DeclaredAccessibility: Accessibility.Public } } property
                        when IsUsableMemberType(compilation, property.Type):
                        if (!seenNames.Add(property.Name)) continue;
                        members.Add(new MemberDeclaration(property.Name, DisplayForTypeof(property.Type), true));
                        memberTypes.Add(property.Type);
                        break;
                    case IFieldSymbol { IsConst: false } field when IsUsableMemberType(compilation, field.Type):
                        if (!seenNames.Add(field.Name)) continue;
                        members.Add(new MemberDeclaration(field.Name, DisplayForTypeof(field.Type), false));
                        memberTypes.Add(field.Type);
                        break;
                }
            }
        }

        result.Add(new EquivalencyTypeDeclaration(typeFullName, members.OrderBy(m => m.Name, StringComparer.Ordinal).ToArray()));

        foreach (var memberType in memberTypes)
            Collect(memberType, compilation, visited, result, depth + 1, cancellationToken);
    }

    private static bool IsUsableMemberType(Compilation compilation, ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Dynamic or TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer) return false;
        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) return false;
        if (type.IsRefLikeType) return false;
        if (type is ITypeParameterSymbol) return false;
        if (type is INamedTypeSymbol { IsAnonymousType: true }) return false;
        if (ContainsTypeParameter(type)) return false;
        return IsReferenceableFromGeneratedCode(compilation, type);
    }

    /// <summary>
    /// True when the generated file — a separate file, at namespace scope — can actually write
    /// this type's name. Being <em>accessible</em> is not enough: a <c>file</c>-local type is
    /// invisible outside its own source file even though it displays like a namespace-level type,
    /// and a private or protected nested type is unreachable from a namespace-level static class.
    /// </summary>
    private static bool IsReferenceableFromGeneratedCode(Compilation compilation, ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array: return IsReferenceableFromGeneratedCode(compilation, array.ElementType);
            case INamedTypeSymbol named:
                for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
                {
                    if (current.IsFileLocal) return false;
                    switch (current.DeclaredAccessibility)
                    {
                        case Accessibility.Public:
                        case Accessibility.Internal:
                        case Accessibility.ProtectedOrInternal:
                        case Accessibility.NotApplicable:
                            break;
                        default:
                            return false;
                    }
                }

                foreach (var argument in named.TypeArguments)
                    if (!IsReferenceableFromGeneratedCode(compilation, argument)) return false;

                return compilation.IsSymbolAccessibleWithin(named, compilation.Assembly);
            default:
                return true;
        }
    }

    /// <summary>
    /// Renders a type so it is legal inside <c>typeof(...)</c>; tuples must be written through
    /// their ValueTuple form because element names are not allowed there.
    /// </summary>
    private static string DisplayForTypeof(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsTupleType: true } tuple && tuple.TupleUnderlyingType is { } underlying)
            return underlying.ToDisplayString(FullyQualified);
        return type.ToDisplayString(FullyQualified);
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol: return true;
            case IArrayTypeSymbol array: return ContainsTypeParameter(array.ElementType);
            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments)
                    if (ContainsTypeParameter(argument)) return true;
                return false;
            default: return false;
        }
    }

    private static bool IsScalar(INamedTypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Char:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
            case SpecialType.System_Decimal:
            case SpecialType.System_String:
            case SpecialType.System_Object:
            case SpecialType.System_DateTime:
            case SpecialType.System_Void:
                return true;
        }

        return ExcludedTypeNames.Contains(type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
    }

    private static bool IsEnumerable(Compilation compilation, INamedTypeSymbol type)
    {
        var enumerable = compilation.GetTypeByMetadataName("System.Collections.IEnumerable");
        if (enumerable is null) return false;
        if (SymbolEqualityComparer.Default.Equals(type, enumerable)) return true;
        return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, enumerable));
    }

    private static IEnumerable<ITypeSymbol> EnumerateCollectionTypeArguments(INamedTypeSymbol type)
    {
        foreach (var argument in type.TypeArguments)
            yield return argument;

        foreach (var iface in type.AllInterfaces)
        {
            switch (iface.OriginalDefinition.SpecialType)
            {
                case SpecialType.System_Collections_Generic_IEnumerable_T:
                    foreach (var argument in iface.TypeArguments) yield return argument;
                    break;
            }
        }
    }
}
