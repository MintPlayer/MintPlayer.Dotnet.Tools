using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MintPlayer.Assertions.Reflection;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// The member half of <see cref="TypeAssertions"/>: does this type declare that property, method,
/// constructor or indexer, and what does it look like.
/// </summary>
/// <remarks>
/// <para>
/// This is hot-path reflection, and it is admissible for exactly one reason (Phase 2 PRD §0, second
/// corollary): it lives on an assertion type nobody else touches. <c>HaveMethod</c> costs a
/// <c>GetMethod</c> call to the person who called <c>HaveMethod</c>, and nothing at all to every
/// other assertion in the suite.
/// </para>
/// <para>
/// ⚠️ Trimming and AOT: these read members by name, which a trimmer has no way to see. A type whose
/// members were trimmed will make these assertions fail rather than misbehave, but they are not
/// trim-safe and are annotated accordingly, following the <c>[RequiresUnreferencedCode]</c>
/// precedent set by <c>EventMonitor</c>. Assertions about a type's shape belong in a test project,
/// which is not published trimmed.
/// </para>
/// <para>
/// A source-generated variant was considered and rejected, and the reason is worth recording: a
/// generator needs a compile-time target, and the whole point of this family is a <see cref="Type"/>
/// chosen at run time — usually from an assembly scan. There is nothing for it to key on.
/// </para>
/// </remarks>
public partial class TypeAssertions
{
    private const BindingFlags AnyInstanceOrStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Asserts the type declares a readable/writable property named <paramref name="name"/>, and exposes it via <c>Which</c>.</summary>
    [RequiresUnreferencedCode("Looks a property up by name; trimming can remove it.")]
    public AndWhichConstraint<TypeAssertions, PropertyInfo> HaveProperty(string name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (Subject is null)
        {
            FailNull($"to have a property named \"{name}\"", because, becauseArgs);
            return new(this, null!);
        }

        var property = Subject.GetProperty(name, AnyInstanceOrStatic);
        Assert().ForCondition(property is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a property named {0}{reason}, but it does not.", name);
        return new(this, property!);
    }

    /// <summary>Asserts the type declares a property named <paramref name="name"/> of type <typeparamref name="TProperty"/>.</summary>
    [RequiresUnreferencedCode("Looks a property up by name; trimming can remove it.")]
    public AndWhichConstraint<TypeAssertions, PropertyInfo> HaveProperty<TProperty>(string name, string? because = null, params object?[] becauseArgs)
    {
        var constraint = HaveProperty(name, because, becauseArgs);
        if (constraint.Which is null) return constraint;

        Assert().ForCondition(constraint.Which.PropertyType == typeof(TProperty)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a property {0} of type {1}{reason}, but found {2}.",
                name, typeof(TProperty), constraint.Which.PropertyType);
        return constraint;
    }

    /// <summary>Asserts the type does not declare a property named <paramref name="name"/>.</summary>
    [RequiresUnreferencedCode("Looks a property up by name; trimming can remove it.")]
    public AndConstraint<TypeAssertions> NotHaveProperty(string name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Assert().ForCondition(Subject is null || Subject.GetProperty(name, AnyInstanceOrStatic) is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a property named {0}{reason}.", name);
        return new(this);
    }

    /// <summary>
    /// Asserts the type declares a method named <paramref name="name"/> taking
    /// <paramref name="parameterTypes"/>, and exposes it via <c>Which</c>.
    /// </summary>
    /// <remarks>
    /// The parameter types are required rather than optional: a name alone is ambiguous the moment a
    /// method is overloaded, and <c>GetMethod(name)</c> throws
    /// <see cref="AmbiguousMatchException"/> for one — turning an assertion into a crash. Pass an
    /// empty array for a parameterless method.
    /// </remarks>
    [RequiresUnreferencedCode("Looks a method up by name; trimming can remove it.")]
    public AndWhichConstraint<TypeAssertions, MethodInfo> HaveMethod(string name, Type[] parameterTypes, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(parameterTypes);
        if (Subject is null)
        {
            FailNull($"to have a method named \"{name}\"", because, becauseArgs);
            return new(this, null!);
        }

        var method = Subject.GetMethod(name, AnyInstanceOrStatic, binder: null, parameterTypes, modifiers: null);
        Assert().ForCondition(method is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a method {0} taking {1}{reason}, but it does not.", name, parameterTypes);
        return new(this, method!);
    }

    /// <summary>Asserts the type does not declare a method named <paramref name="name"/> taking <paramref name="parameterTypes"/>.</summary>
    [RequiresUnreferencedCode("Looks a method up by name; trimming can remove it.")]
    public AndConstraint<TypeAssertions> NotHaveMethod(string name, Type[] parameterTypes, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(parameterTypes);

        var found = Subject?.GetMethod(name, AnyInstanceOrStatic, binder: null, parameterTypes, modifiers: null);
        Assert().ForCondition(found is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a method {0} taking {1}{reason}.", name, parameterTypes);
        return new(this);
    }

    /// <summary>Asserts the type declares a constructor taking <paramref name="parameterTypes"/>, and exposes it via <c>Which</c>.</summary>
    [RequiresUnreferencedCode("Looks a constructor up by signature; trimming can remove it.")]
    public AndWhichConstraint<TypeAssertions, ConstructorInfo> HaveConstructor(Type[] parameterTypes, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(parameterTypes);
        if (Subject is null)
        {
            FailNull("to have a constructor with the given parameters", because, becauseArgs);
            return new(this, null!);
        }

        var constructor = Subject.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, binder: null, parameterTypes, modifiers: null);
        Assert().ForCondition(constructor is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a constructor taking {0}{reason}, but it does not.", parameterTypes);
        return new(this, constructor!);
    }

    /// <summary>Asserts the type declares a parameterless constructor.</summary>
    [RequiresUnreferencedCode("Looks a constructor up by signature; trimming can remove it.")]
    public AndWhichConstraint<TypeAssertions, ConstructorInfo> HaveDefaultConstructor(string? because = null, params object?[] becauseArgs)
        => HaveConstructor([], because, becauseArgs);

    /// <summary>Asserts the type declares an indexer taking <paramref name="parameterTypes"/>.</summary>
    /// <remarks>
    /// An indexer is a property whose name comes from <c>[DefaultMember]</c> — <c>Item</c> for C#,
    /// but not for every language, so the attribute is read rather than the name assumed.
    /// </remarks>
    [RequiresUnreferencedCode("Enumerates properties; trimming can remove them.")]
    public AndWhichConstraint<TypeAssertions, PropertyInfo> HaveIndexer(Type[] parameterTypes, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(parameterTypes);
        if (Subject is null)
        {
            FailNull("to have an indexer with the given parameters", because, becauseArgs);
            return new(this, null!);
        }

        PropertyInfo? found = null;
        foreach (var property in Subject.GetProperties(AnyInstanceOrStatic))
        {
            var parameters = property.GetIndexParameters();
            if (parameters.Length != parameterTypes.Length) continue;

            var matches = true;
            for (var i = 0; matches && i < parameters.Length; i++)
            {
                matches = parameters[i].ParameterType == parameterTypes[i];
            }
            if (matches) { found = property; break; }
        }

        Assert().ForCondition(found is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an indexer taking {0}{reason}, but it does not.", parameterTypes);
        return new(this, found!);
    }

    /// <summary>Asserts the type is decorated with <typeparamref name="TAttribute"/>, on itself or on a base type.</summary>
    public AndWhichConstraint<TypeAssertions, TAttribute> BeDecoratedWithOrInherit<TAttribute>(string? because = null, params object?[] becauseArgs)
        where TAttribute : Attribute
    {
        var attribute = Subject?.GetCustomAttribute<TAttribute>(inherit: true);
        Assert().ForCondition(attribute is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be decorated with or inherit {0}{reason}, but it is not.", typeof(TAttribute));
        return new(this, attribute!);
    }

    /// <summary>Asserts the type lives in namespace <paramref name="expected"/>.</summary>
    public AndConstraint<TypeAssertions> BeInNamespace(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);
        Assert().ForCondition(Subject is not null && string.Equals(Subject.Namespace, expected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be in namespace {0}{reason}, but found {1}.", expected, Subject?.Namespace);
        return new(this);
    }

    /// <summary>Asserts the type lives under namespace <paramref name="expected"/>, or a nested one.</summary>
    public AndConstraint<TypeAssertions> BeUnderNamespace(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);
        var actual = Subject?.Namespace;
        var under = actual is not null
            && (string.Equals(actual, expected, StringComparison.Ordinal) || actual.StartsWith(expected + ".", StringComparison.Ordinal));

        Assert().ForCondition(under).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be under namespace {0}{reason}, but found {1}.", expected, actual);
        return new(this);
    }

    private void FailNull(string expectation, string? because, object?[] becauseArgs)
        => Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} " + expectation + "{reason}, but it was <null>.");
}
