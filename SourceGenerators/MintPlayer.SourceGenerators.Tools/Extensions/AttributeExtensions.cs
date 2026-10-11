using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Globalization;

namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// Finding attributes by metadata name and reading their arguments as constants.
/// </summary>
/// <remarks>
/// Read attribute arguments from <see cref="AttributeData"/>, never from the attribute's syntax: the compiler has
/// already folded <c>const</c> references, arithmetic, casts and <c>nameof</c> into a constant, filled in the
/// defaults of optional parameters and bound named arguments, none of which a syntax reading gets right.
/// </remarks>
public static class AttributeExtensions
{
    /// <summary>
    /// True when <paramref name="attributeClass"/> is the type with the given metadata name, e.g.
    /// <c>"MintPlayer.SourceGenerators.Attributes.InjectAttribute"</c>. Nested types use <c>+</c>
    /// (<c>"N.Outer+InnerAttribute"</c>), generic ones their arity (<c>"N.MyAttribute`1"</c>).
    /// </summary>
    /// <remarks>
    /// Compares the namespace as well as the name, so a same-named attribute in another namespace never matches.
    /// A constructed generic attribute matches through its definition. Null and error types never match.
    /// </remarks>
    public static bool IsAttribute(this INamedTypeSymbol? attributeClass, string metadataName)
    {
        if (attributeClass is null || attributeClass.TypeKind == TypeKind.Error || string.IsNullOrEmpty(metadataName))
            return false;

        // Walk the name from the end: type, containing types, then namespaces. No string is built.
        var type = attributeClass.OriginalDefinition;
        var end = metadataName.Length;
        ISymbol current = type;
        while (true)
        {
            var name = current.MetadataName;
            var start = end - name.Length;
            if (start < 0 || string.CompareOrdinal(metadataName, start, name, 0, name.Length) != 0)
                return false;

            var parent = (ISymbol?)current.ContainingType ?? current.ContainingNamespace;
            if (parent is null || parent is INamespaceSymbol { IsGlobalNamespace: true })
                return start == 0;

            var separator = parent is INamedTypeSymbol ? '+' : '.';
            if (start == 0 || metadataName[start - 1] != separator)
                return false;

            end = start - 1;
            current = parent;
        }
    }

    /// <summary>
    /// The first attribute on <paramref name="symbol"/> whose class has the metadata name <paramref name="metadataName"/>
    /// (see <see cref="IsAttribute"/>), or null.
    /// </summary>
    public static AttributeData? FindAttribute(this ISymbol? symbol, string metadataName)
    {
        if (symbol is null) return null;
        foreach (var attribute in symbol.GetAttributes())
            if (attribute.AttributeClass.IsAttribute(metadataName))
                return attribute;
        return null;
    }

    /// <summary>
    /// Every attribute on <paramref name="symbol"/> whose class has the metadata name <paramref name="metadataName"/>
    /// (see <see cref="IsAttribute"/>), in declaration order. Empty for a null symbol.
    /// </summary>
    public static IEnumerable<AttributeData> FindAttributes(this ISymbol? symbol, string metadataName)
    {
        if (symbol is null) return [];
        return symbol.GetAttributes().Where(a => a.AttributeClass.IsAttribute(metadataName));
    }

    /// <summary>True when <paramref name="symbol"/> carries the attribute (see <see cref="IsAttribute"/>).</summary>
    public static bool HasAttribute(this ISymbol? symbol, string metadataName)
        => symbol.FindAttribute(metadataName) is not null;

    /// <summary>
    /// Reads one argument of an attribute as a constant.
    /// </summary>
    /// <param name="attribute">The attribute; null gives false.</param>
    /// <param name="name">
    /// A named argument (a settable property or field), or a constructor parameter's name, matched ignoring case so
    /// that a parameter <c>lifetime</c> and a property <c>Lifetime</c> are both found by either spelling. A named
    /// argument wins over a constructor parameter of the same name, because it is assigned after the constructor ran.
    /// </param>
    /// <param name="value">The argument converted to <typeparamref name="T"/>; <c>default</c> when the result is false.</param>
    /// <returns>
    /// False when the attribute, its constructor or the argument is missing or has an error (the attribute class or a
    /// <c>typeof</c> did not bind), or when the value does not convert to <typeparamref name="T"/>.
    /// </returns>
    /// <remarks>
    /// <typeparamref name="T"/> may be:
    /// <list type="bullet">
    ///   <item>a primitive or <see cref="string"/>; numbers convert between numeric types when the value fits;</item>
    ///   <item>an enum (or a nullable one), from an enum or integer argument;</item>
    ///   <item><see cref="INamedTypeSymbol"/> or <see cref="ITypeSymbol"/>, for a <c>typeof</c> argument;</item>
    ///   <item><see cref="ImmutableArray{T}"/> of <see cref="TypedConstant"/>, or <c>U[]</c> with every element converted as above,
    ///   for an array or <c>params</c> argument;</item>
    ///   <item><see cref="TypedConstant"/> itself, or <see cref="object"/> for the raw value.</item>
    /// </list>
    /// An explicit <c>null</c> argument gives true and a null value for a reference or nullable <typeparamref name="T"/>.
    /// An optional constructor parameter that was not passed gives its default value: the compiler fills it in.
    /// </remarks>
    public static bool TryGetArgument<T>(this AttributeData? attribute, string name, out T value)
    {
        value = default!;
        if (attribute is null || string.IsNullOrEmpty(name)) return false;
        if (attribute.AttributeClass is null or { TypeKind: TypeKind.Error }) return false;

        if (TryFindArgument(attribute, name, out var constant) && TryConvert(constant, typeof(T), out var converted))
        {
            value = (T)converted!;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reads one argument of an attribute as a constant, or returns <paramref name="fallback"/> when
    /// <see cref="TryGetArgument{T}"/> would return false. See there for the lookup and conversion rules.
    /// </summary>
    public static T GetArgument<T>(this AttributeData? attribute, string name, T fallback)
        => attribute.TryGetArgument<T>(name, out var value) ? value : fallback;

    private static bool TryFindArgument(AttributeData attribute, string name, out TypedConstant constant)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                constant = named.Value;
                return true;
            }
        }

        var parameters = attribute.AttributeConstructor?.Parameters ?? ImmutableArray<IParameterSymbol>.Empty;
        var arguments = attribute.ConstructorArguments;
        for (var i = 0; i < parameters.Length && i < arguments.Length; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                constant = arguments[i];
                return true;
            }
        }

        constant = default;
        return false;
    }

    private static bool TryConvert(TypedConstant constant, Type target, out object? result)
    {
        result = null;
        if (target == typeof(TypedConstant))
        {
            result = constant;
            return true;
        }
        if (constant.Kind == TypedConstantKind.Error) return false;

        var underlying = Nullable.GetUnderlyingType(target);
        var nullable = underlying is not null || !target.IsValueType;

        if (constant.IsNull)
            return nullable;

        if (constant.Kind == TypedConstantKind.Array)
        {
            if (target == typeof(ImmutableArray<TypedConstant>) || target == typeof(ImmutableArray<TypedConstant>?))
            {
                result = constant.Values;
                return true;
            }
            if (target == typeof(object))
            {
                result = constant.Values;
                return true;
            }
            if (!target.IsArray) return false;

            var elementType = target.GetElementType()!;
            var array = Array.CreateInstance(elementType, constant.Values.Length);
            for (var i = 0; i < constant.Values.Length; i++)
            {
                if (!TryConvert(constant.Values[i], elementType, out var element)) return false;
                array.SetValue(element, i);
            }
            result = array;
            return true;
        }

        var raw = constant.Value;
        if (raw is null) return nullable;

        if (constant.Kind == TypedConstantKind.Type)
        {
            if (raw is IErrorTypeSymbol or ITypeSymbol { TypeKind: TypeKind.Error }) return false;
            if (!target.IsInstanceOfType(raw)) return false;
            result = raw;
            return true;
        }

        var valueType = underlying ?? target;
        if (valueType.IsInstanceOfType(raw))
        {
            result = raw;
            return true;
        }

        // Enum and integer arguments: TypedConstant holds an enum as its underlying integer.
        if (valueType.IsEnum && IsInteger(raw))
        {
            result = Enum.ToObject(valueType, raw);
            return true;
        }

        // Numbers between numeric types (an int argument read as long, a byte as int), only when the value fits.
        // A fractional value never silently becomes an integer.
        if ((IsInteger(raw) && IsNumericType(valueType)) || (IsFractional(raw) && IsFractionalType(valueType)))
        {
            try
            {
                result = Convert.ChangeType(raw, valueType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (OverflowException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        return false;
    }

    private static bool IsInteger(object value)
        => value is sbyte or byte or short or ushort or int or uint or long or ulong;

    private static bool IsFractional(object value)
        => value is float or double or decimal;

    private static bool IsFractionalType(Type type)
        => type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsNumericType(Type type)
        => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
        || IsFractionalType(type);
}
