using System.Reflection;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Reflection;

/// <summary>
/// Assertions shared by every kind of member: its name, its declaring type and its attributes.
/// </summary>
public class MemberInfoAssertions : MemberInfoAssertions<MemberInfo, MemberInfoAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public MemberInfoAssertions(MemberInfo? subject, string? subjectExpression) : base(subject, subjectExpression) { }
}

/// <summary>
/// The shared member assertions, generic over the concrete member and assertions types so a derived
/// family keeps its own type through a chain.
/// </summary>
public abstract class MemberInfoAssertions<TSubject, TSelf> : ReferenceTypeAssertions<TSubject, TSelf>
    where TSubject : MemberInfo
    where TSelf : MemberInfoAssertions<TSubject, TSelf>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    protected MemberInfoAssertions(TSubject? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the member is named <paramref name="expected"/>.</summary>
    public AndConstraint<TSelf> HaveName(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);
        Assert().ForCondition(Subject is not null && string.Equals(Subject.Name, expected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be named {0}{reason}, but found {1}.", expected, Subject?.Name);
        return new((TSelf)this);
    }

    /// <summary>Asserts the member is decorated with <typeparamref name="TAttribute"/>, and exposes it via <c>Which</c>.</summary>
    public AndWhichConstraint<TSelf, TAttribute> BeDecoratedWith<TAttribute>(string? because = null, params object?[] becauseArgs)
        where TAttribute : Attribute
    {
        var attribute = Subject?.GetCustomAttribute<TAttribute>(inherit: false);
        Assert().ForCondition(attribute is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be decorated with {0}{reason}, but it is not.", typeof(TAttribute));
        return new((TSelf)this, attribute!);
    }

    /// <summary>Asserts the member is not decorated with <typeparamref name="TAttribute"/>.</summary>
    public AndConstraint<TSelf> NotBeDecoratedWith<TAttribute>(string? because = null, params object?[] becauseArgs)
        where TAttribute : Attribute
    {
        Assert().ForCondition(Subject is null || Subject.GetCustomAttribute<TAttribute>(inherit: false) is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be decorated with {0}{reason}.", typeof(TAttribute));
        return new((TSelf)this);
    }

    /// <summary>Asserts the member is declared on <typeparamref name="TDeclaring"/>.</summary>
    public AndConstraint<TSelf> BeDeclaredOn<TDeclaring>(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null && Subject.DeclaringType == typeof(TDeclaring)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be declared on {0}{reason}, but found {1}.", typeof(TDeclaring), Subject?.DeclaringType);
        return new((TSelf)this);
    }
}

/// <summary>Assertions on a <see cref="MethodInfo"/>.</summary>
public class MethodInfoAssertions : MemberInfoAssertions<MethodInfo, MethodInfoAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public MethodInfoAssertions(MethodInfo? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the method is virtual — overridable, and not sealed against it.</summary>
    /// <remarks>
    /// <c>IsVirtual</c> alone is not the question a caller means: an <c>override sealed</c> method is
    /// still <c>IsVirtual</c> but cannot be overridden again, and an interface implementation is
    /// marked virtual by the compiler whether or not the source said so.
    /// </remarks>
    public AndConstraint<MethodInfoAssertions> BeVirtual(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { IsVirtual: true, IsFinal: false }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be virtual{reason}, but it is not.");
        return new(this);
    }

    /// <summary>Asserts the method cannot be overridden.</summary>
    public AndConstraint<MethodInfoAssertions> NotBeVirtual(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || !Subject.IsVirtual || Subject.IsFinal).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be virtual{reason}.");
        return new(this);
    }

    /// <summary>Asserts the method is static.</summary>
    public AndConstraint<MethodInfoAssertions> BeStatic(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { IsStatic: true }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be static{reason}, but it is not.");
        return new(this);
    }

    /// <summary>Asserts the method returns <typeparamref name="TReturn"/>.</summary>
    public AndConstraint<MethodInfoAssertions> Return<TReturn>(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null && Subject.ReturnType == typeof(TReturn)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to return {0}{reason}, but found {1}.", typeof(TReturn), Subject?.ReturnType);
        return new(this);
    }

    /// <summary>Asserts the method returns <c>void</c>.</summary>
    /// <remarks><c>void</c> cannot be a type argument, so this cannot be <c>Return&lt;void&gt;()</c>.</remarks>
    public AndConstraint<MethodInfoAssertions> ReturnVoid(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null && Subject.ReturnType == typeof(void)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to return void{reason}, but found {0}.", Subject?.ReturnType);
        return new(this);
    }

    /// <summary>Asserts the method is <c>async</c>.</summary>
    /// <remarks>
    /// Read from <c>[AsyncStateMachine]</c>, which the compiler emits and nothing else does. Note
    /// that this is about the <em>implementation</em>, not the signature: a method returning a
    /// <c>Task</c> it got from somewhere else is not async, and asserting on its return type is
    /// usually what a caller actually wants.
    /// </remarks>
    public AndConstraint<MethodInfoAssertions> BeAsync(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null
            && Subject.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() is not null)
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be async{reason}, but it is not.");
        return new(this);
    }
}

/// <summary>Assertions on a <see cref="PropertyInfo"/>.</summary>
public class PropertyInfoAssertions : MemberInfoAssertions<PropertyInfo, PropertyInfoAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public PropertyInfoAssertions(PropertyInfo? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the property is of type <typeparamref name="TProperty"/>.</summary>
    public AndConstraint<PropertyInfoAssertions> BeOfType<TProperty>(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null && Subject.PropertyType == typeof(TProperty)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be of type {0}{reason}, but found {1}.", typeof(TProperty), Subject?.PropertyType);
        return new(this);
    }

    /// <summary>Asserts the property has a public getter and no public setter.</summary>
    /// <remarks>
    /// <c>init</c> counts as writable: it is a setter, and the difference between <c>init</c> and
    /// <c>set</c> is enforced by the compiler rather than by metadata this can read.
    /// </remarks>
    public AndConstraint<PropertyInfoAssertions> BeReadOnly(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { CanRead: true } and { SetMethod: null or { IsPublic: false } }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be read-only{reason}, but it has a public setter.");
        return new(this);
    }

    /// <summary>Asserts the property has both a public getter and a public setter.</summary>
    public AndConstraint<PropertyInfoAssertions> BeWritable(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { CanRead: true, SetMethod.IsPublic: true }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be writable{reason}, but it is not.");
        return new(this);
    }

    /// <summary>Asserts the property is virtual and not sealed against further overriding.</summary>
    public AndConstraint<PropertyInfoAssertions> BeVirtual(string? because = null, params object?[] becauseArgs)
    {
        var getter = Subject?.GetMethod;
        Assert().ForCondition(getter is { IsVirtual: true, IsFinal: false }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be virtual{reason}, but it is not.");
        return new(this);
    }
}
