using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Reflection;

/// <summary>
/// Assertions on an <see cref="Assembly"/>: what it references, what it defines, and whether it is
/// signed.
/// </summary>
/// <remarks>
/// The architecture-test shape — "the domain assembly must not reference the web assembly" — which
/// is the one thing in this family that genuinely cannot be expressed any other way. Like the rest
/// of the reflection family it is reflection on the hot path <em>for its own caller only</em>, and
/// nothing else in the library goes near it.
/// </remarks>
public class AssemblyAssertions : ReferenceTypeAssertions<Assembly, AssemblyAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public AssemblyAssertions(Assembly? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the assembly references <paramref name="expected"/>.</summary>
    /// <remarks>
    /// Direct references only, which is the question an architecture test is asking: a transitive
    /// reference through a third assembly is that assembly's business, not this one's.
    /// </remarks>
    [RequiresUnreferencedCode("Reads the assembly's reference table; trimming can remove references.")]
    public AndConstraint<AssemblyAssertions> Reference(Assembly expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var name = expected.GetName().Name;

        Assert().ForCondition(Subject is not null && References(Subject, name)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to reference assembly {0}{reason}, but it does not.", name);
        return new(this);
    }

    /// <summary>Asserts the assembly does not reference <paramref name="unexpected"/>.</summary>
    [RequiresUnreferencedCode("Reads the assembly's reference table; trimming can remove references.")]
    public AndConstraint<AssemblyAssertions> NotReference(Assembly unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var name = unexpected.GetName().Name;

        Assert().ForCondition(Subject is null || !References(Subject, name)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to reference assembly {0}{reason}.", name);
        return new(this);
    }

    [RequiresUnreferencedCode("Reads the assembly's reference table; trimming can remove references.")]
    private static bool References(Assembly assembly, string? name)
    {
        if (name is null) return false;
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            if (string.Equals(reference.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Asserts the assembly defines <paramref name="expected"/>, and exposes it via <c>Which</c>.</summary>
    public AndWhichConstraint<AssemblyAssertions, Type> DefineType(Type expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var defines = Subject is not null && ReferenceEquals(expected.Assembly, Subject);

        Assert().ForCondition(defines).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to define type {0}{reason}, but it does not.", expected);
        return new(this, defines ? expected : null!);
    }

    /// <summary>Asserts the assembly defines a type with this namespace and name, and exposes it via <c>Which</c>.</summary>
    [RequiresUnreferencedCode("Looks a type up by name; trimming can remove it.")]
    public AndWhichConstraint<AssemblyAssertions, Type> DefineType(string? @namespace, string name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var fullName = string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        var type = Subject?.GetType(fullName, throwOnError: false, ignoreCase: false);

        Assert().ForCondition(type is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to define type {0}{reason}, but it does not.", fullName);
        return new(this, type!);
    }

    /// <summary>Asserts the assembly carries a strong name.</summary>
    public AndConstraint<AssemblyAssertions> BeSigned(string? because = null, params object?[] becauseArgs)
    {
        var token = Subject?.GetName().GetPublicKeyToken();

        Assert().ForCondition(token is { Length: > 0 }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be signed{reason}, but it is not.");
        return new(this);
    }

    /// <summary>Asserts the assembly carries no strong name.</summary>
    public AndConstraint<AssemblyAssertions> NotBeSigned(string? because = null, params object?[] becauseArgs)
    {
        var token = Subject?.GetName().GetPublicKeyToken();

        Assert().ForCondition(token is null or { Length: 0 }).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be signed{reason}.");
        return new(this);
    }
}
