using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace MintPlayer.Assertions.Reflection;

/// <summary>
/// Picks a set of types out of one or more assemblies, so an assertion can be made about all of
/// them at once: <c>AllTypes.From(assembly).ThatImplement&lt;IHandler&gt;().Should().BeSealed()</c>.
/// </summary>
/// <remarks>
/// <para>
/// The architecture-test entry point. Everything here is eager — the types are selected when the
/// filter is called, not when the assertion runs — because a lazily evaluated selector that is
/// enumerated once per assertion in a chain would do the scan again for each one.
/// </para>
/// <para>
/// ⚠️ Scanning an assembly's types is the opposite of trim-friendly, and this is annotated as such.
/// It belongs in a test project.
/// </para>
/// </remarks>
public sealed class TypeSelector
{
    private readonly List<Type> types;
    private readonly string description;

    internal TypeSelector(List<Type> types, string description)
    {
        this.types = types;
        this.description = description;
    }

    /// <summary>The selected types.</summary>
    public IReadOnlyList<Type> Types => types;

    /// <summary>A human-readable account of how this set was selected, used in failure messages.</summary>
    public override string ToString() => description;

    /// <summary>Keeps only the types deriving from <typeparamref name="TBase"/>.</summary>
    public TypeSelector ThatDeriveFrom<TBase>() where TBase : class
        => Where(t => t.IsSubclassOf(typeof(TBase)), $"deriving from {typeof(TBase).Name}");

    /// <summary>Keeps only the types implementing <typeparamref name="TInterface"/>.</summary>
    public TypeSelector ThatImplement<TInterface>()
        => Where(t => typeof(TInterface).IsAssignableFrom(t) && t != typeof(TInterface),
            $"implementing {typeof(TInterface).Name}");

    /// <summary>Keeps only the types decorated with <typeparamref name="TAttribute"/>.</summary>
    public TypeSelector ThatAreDecoratedWith<TAttribute>() where TAttribute : Attribute
        => Where(t => t.GetCustomAttribute<TAttribute>(inherit: false) is not null,
            $"decorated with {typeof(TAttribute).Name}");

    /// <summary>Keeps only the types in namespace <paramref name="namespace"/> or a nested one.</summary>
    public TypeSelector ThatAreUnderNamespace(string @namespace)
    {
        ArgumentException.ThrowIfNullOrEmpty(@namespace);
        return Where(
            t => t.Namespace is { } ns && (ns == @namespace || ns.StartsWith(@namespace + ".", StringComparison.Ordinal)),
            $"under namespace {@namespace}");
    }

    /// <summary>Keeps only the public types.</summary>
    public TypeSelector ThatArePublic() => Where(t => t.IsPublic || t.IsNestedPublic, "public");

    /// <summary>Keeps only the classes, dropping interfaces, enums, delegates and structs.</summary>
    public TypeSelector ThatAreClasses() => Where(t => t.IsClass && !typeof(Delegate).IsAssignableFrom(t), "classes");

    /// <summary>Keeps only the types matching <paramref name="predicate"/>.</summary>
    public TypeSelector Where(Func<Type, bool> predicate, string? describedAs = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var kept = new List<Type>();
        foreach (var type in types)
        {
            if (predicate(type)) kept.Add(type);
        }
        return new(kept, describedAs is null ? description : $"{description}, {describedAs}");
    }
}

/// <summary>
/// The entry point for selecting types out of assemblies.
/// </summary>
public static class AllTypes
{
    /// <summary>Every type defined in <paramref name="assembly"/>, including non-public ones.</summary>
    /// <remarks>
    /// A partially loadable assembly yields the types it could load rather than throwing: a
    /// <see cref="ReflectionTypeLoadException"/> in the middle of a selector would replace an
    /// architecture-test failure with a crash, and the types that <em>did</em> load are still worth
    /// asserting on.
    /// </remarks>
    [RequiresUnreferencedCode("Enumerates an assembly's types; trimming removes them.")]
    public static TypeSelector From(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new(Load(assembly), $"the types in {assembly.GetName().Name}");
    }

    /// <summary>Every type defined in the assembly containing <typeparamref name="T"/>.</summary>
    [RequiresUnreferencedCode("Enumerates an assembly's types; trimming removes them.")]
    public static TypeSelector FromAssemblyContaining<T>() => From(typeof(T).Assembly);

    /// <summary>Every type defined across <paramref name="assemblies"/>.</summary>
    [RequiresUnreferencedCode("Enumerates an assembly's types; trimming removes them.")]
    public static TypeSelector From(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var all = new List<Type>();
        var names = new StringBuilder();
        foreach (var assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            all.AddRange(Load(assembly));
            if (names.Length > 0) names.Append(", ");
            names.Append(assembly.GetName().Name);
        }
        return new(all, $"the types in {names}");
    }

    [RequiresUnreferencedCode("Enumerates an assembly's types; trimming removes them.")]
    private static List<Type> Load(Assembly assembly)
    {
        try
        {
            return [.. assembly.GetTypes()];
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaded = new List<Type>();
            foreach (var type in ex.Types)
            {
                if (type is not null) loaded.Add(type);
            }
            return loaded;
        }
    }
}
