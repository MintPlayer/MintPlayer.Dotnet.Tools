using System.Reflection;
using System.Text;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Reflection;

/// <summary>
/// Assertions over every type a <see cref="TypeSelector"/> picked: the architecture-test shape,
/// where one rule has to hold for a whole family of types.
/// </summary>
/// <remarks>
/// Every assertion here reports <em>all</em> the offending types, not just the first. That is the
/// difference between a rule you can fix in one pass and a rule you fix one recompile at a time, and
/// it costs nothing: the list is only built once the assertion has already failed.
/// </remarks>
public class TypeSelectorAssertions
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public TypeSelectorAssertions(TypeSelector? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "the selected types" : subjectExpression!;
    }

    /// <summary>The selector under test.</summary>
    public TypeSelector? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts every selected type is sealed.</summary>
    public AndConstraint<TypeSelectorAssertions> BeSealed(string? because = null, params object?[] becauseArgs)
        => All(t => t.IsSealed, "be sealed", because, becauseArgs);

    /// <summary>Asserts every selected type is abstract.</summary>
    public AndConstraint<TypeSelectorAssertions> BeAbstract(string? because = null, params object?[] becauseArgs)
        => All(t => t.IsAbstract && !t.IsSealed, "be abstract", because, becauseArgs);

    /// <summary>Asserts every selected type is public.</summary>
    public AndConstraint<TypeSelectorAssertions> BePublic(string? because = null, params object?[] becauseArgs)
        => All(t => t.IsPublic || t.IsNestedPublic, "be public", because, becauseArgs);

    /// <summary>Asserts no selected type is public.</summary>
    public AndConstraint<TypeSelectorAssertions> NotBePublic(string? because = null, params object?[] becauseArgs)
        => All(t => !t.IsPublic && !t.IsNestedPublic, "not be public", because, becauseArgs);

    /// <summary>Asserts every selected type is decorated with <typeparamref name="TAttribute"/>.</summary>
    public AndConstraint<TypeSelectorAssertions> BeDecoratedWith<TAttribute>(string? because = null, params object?[] becauseArgs)
        where TAttribute : Attribute
        => All(t => t.GetCustomAttribute<TAttribute>(inherit: false) is not null,
            $"be decorated with {typeof(TAttribute).Name}", because, becauseArgs);

    /// <summary>Asserts every selected type implements <typeparamref name="TInterface"/>.</summary>
    public AndConstraint<TypeSelectorAssertions> Implement<TInterface>(string? because = null, params object?[] becauseArgs)
        => All(t => typeof(TInterface).IsAssignableFrom(t) && t != typeof(TInterface),
            $"implement {typeof(TInterface).Name}", because, becauseArgs);

    /// <summary>Asserts every selected type lives under <paramref name="namespace"/>.</summary>
    public AndConstraint<TypeSelectorAssertions> BeUnderNamespace(string @namespace, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(@namespace);
        return All(
            t => t.Namespace is { } ns && (ns == @namespace || ns.StartsWith(@namespace + ".", StringComparison.Ordinal)),
            $"be under namespace {@namespace}", because, becauseArgs);
    }

    /// <summary>Asserts every selected type satisfies <paramref name="predicate"/>.</summary>
    public AndConstraint<TypeSelectorAssertions> AllSatisfy(Func<Type, bool> predicate, string expectation,
        string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrEmpty(expectation);
        return All(predicate, expectation, because, becauseArgs);
    }

    /// <summary>
    /// Asserts the selector matched at least one type.
    /// </summary>
    /// <remarks>
    /// The guard an architecture test needs and usually forgets: a rule over an empty set holds
    /// vacuously, so a selector whose filter no longer matches anything — because the types were
    /// renamed, moved or deleted — passes every assertion in the suite while checking nothing. This
    /// is the same concern as the equivalency engine's vacuity guard, at the level of a type set.
    /// </remarks>
    public AndConstraint<TypeSelectorAssertions> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { Types.Count: > 0 }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to match at least one type{reason}, but {0} matched none — every rule over an empty set holds vacuously.",
                Subject?.ToString());
        return new(this);
    }

    private AndConstraint<TypeSelectorAssertions> All(Func<Type, bool> predicate, string expectation,
        string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to " + expectation + "{reason}, but the selector was <null>.");
        if (Subject is null) return new(this);

        var offenders = new List<Type>();
        var types = Subject.Types;
        for (var i = 0; i < types.Count; i++)
        {
            if (!predicate(types[i])) offenders.Add(types[i]);
        }

        if (offenders.Count == 0) return new(this);

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected all of {0} to " + expectation + "{reason}, but {1} did not:" + Environment.NewLine + "{2}",
                Subject.ToString(), offenders.Count, Names(offenders));
        return new(this);
    }

    private static string Names(List<Type> types)
    {
        var sb = new StringBuilder();
        foreach (var type in types)
        {
            if (sb.Length > 0) sb.Append(Environment.NewLine);
            sb.Append("  - ").Append(type.FullName ?? type.Name);
        }
        return sb.ToString();
    }
}
