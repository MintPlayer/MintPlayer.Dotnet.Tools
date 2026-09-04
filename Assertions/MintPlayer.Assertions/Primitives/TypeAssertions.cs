namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on <see cref="Type"/> subjects: identity, inheritance, interface implementation,
/// attribute decoration and type-kind checks (abstract/sealed/static/interface/class).
/// </summary>
public class TypeAssertions : ReferenceTypeAssertions<Type, TypeAssertions>
{
    public TypeAssertions(Type? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the type is exactly <typeparamref name="TExpected"/>.</summary>
    public AndConstraint<TypeAssertions> Be<TExpected>(string? because = null, params object?[] becauseArgs)
        => Be(typeof(TExpected), because, becauseArgs);

    /// <summary>Asserts the type is exactly the expected type.</summary>
    public AndConstraint<TypeAssertions> Be(Type expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the type is not <typeparamref name="TUnexpected"/>.</summary>
    public AndConstraint<TypeAssertions> NotBe<TUnexpected>(string? because = null, params object?[] becauseArgs)
        => NotBe(typeof(TUnexpected), because, becauseArgs);

    /// <summary>Asserts the type is not the unexpected type.</summary>
    public AndConstraint<TypeAssertions> NotBe(Type unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        Assert().ForCondition(Subject != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>
    /// Asserts that instances of the subject type are assignable to <typeparamref name="T"/>.
    /// Hides the base overload, which would test the <see cref="Type"/> object itself.
    /// </summary>
    public new AndConstraint<TypeAssertions> BeAssignableTo<T>(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null && typeof(T).IsAssignableFrom(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be assignable to {0}{reason}, but found {1}.", typeof(T), Subject);
        return new(this);
    }

    /// <summary>Asserts the type derives from (is a subclass of) <typeparamref name="TBase"/>.</summary>
    public AndConstraint<TypeAssertions> BeDerivedFrom<TBase>(string? because = null, params object?[] becauseArgs)
        where TBase : class
    {
        Assert().ForCondition(Subject is not null && Subject.IsSubclassOf(typeof(TBase))).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be derived from {0}{reason}, but found {1}.", typeof(TBase), Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the type does not derive from <typeparamref name="TBase"/>. Mirrors
    /// <see cref="BeDerivedFrom{TBase}"/>, so the type itself is not "derived from" itself and passes;
    /// a null subject passes as well.
    /// </summary>
    public AndConstraint<TypeAssertions> NotBeDerivedFrom<TBase>(string? because = null, params object?[] becauseArgs)
        where TBase : class
    {
        Assert().ForCondition(Subject is null || !Subject.IsSubclassOf(typeof(TBase))).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be derived from {0}{reason}.", typeof(TBase));
        return new(this);
    }

    /// <summary>Asserts the type implements the interface <typeparamref name="TInterface"/>.</summary>
    public AndConstraint<TypeAssertions> Implement<TInterface>(string? because = null, params object?[] becauseArgs)
    {
        if (!typeof(TInterface).IsInterface)
            throw new ArgumentException($"{typeof(TInterface)} must be an interface.", nameof(TInterface));

        Assert().ForCondition(Subject is not null && Subject != typeof(TInterface) && typeof(TInterface).IsAssignableFrom(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to implement {0}{reason}, but found {1}.", typeof(TInterface), Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the type does not implement the interface <typeparamref name="TInterface"/>. Mirrors
    /// <see cref="Implement{TInterface}"/>: the interface itself does not "implement" itself and passes,
    /// as does a null subject. A non-interface <typeparamref name="TInterface"/> is a caller mistake and
    /// still throws rather than quietly passing.
    /// </summary>
    public AndConstraint<TypeAssertions> NotImplement<TInterface>(string? because = null, params object?[] becauseArgs)
    {
        if (!typeof(TInterface).IsInterface)
            throw new ArgumentException($"{typeof(TInterface)} must be an interface.", nameof(TInterface));

        Assert().ForCondition(Subject is null || Subject == typeof(TInterface) || !typeof(TInterface).IsAssignableFrom(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to implement {0}{reason}.", typeof(TInterface));
        return new(this);
    }

    /// <summary>
    /// Asserts the type is decorated with the attribute <typeparamref name="TAttribute"/>;
    /// <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/> exposes the first matching attribute.
    /// </summary>
    public AndWhichConstraint<TypeAssertions, TAttribute> BeDecoratedWith<TAttribute>(string? because = null, params object?[] becauseArgs)
        where TAttribute : Attribute
    {
        var attribute = FirstAttributeOrDefault<TAttribute>();
        Assert().ForCondition(attribute is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be decorated with {0}{reason}, but found {1}.", typeof(TAttribute), Subject);
        return new(this, attribute!);
    }

    /// <summary>
    /// Asserts the type is decorated with an attribute <typeparamref name="TAttribute"/> matching the predicate;
    /// <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/> exposes the first matching attribute.
    /// </summary>
    public AndWhichConstraint<TypeAssertions, TAttribute> BeDecoratedWith<TAttribute>(Func<TAttribute, bool> predicate, string? because = null, params object?[] becauseArgs)
        where TAttribute : Attribute
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var attribute = FirstAttributeOrDefault<TAttribute>();
        var match = attribute is null ? null : GetAttributes<TAttribute>().FirstOrDefault(predicate);
        Assert().ForCondition(attribute is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be decorated with {0}{reason}, but found {1}.", typeof(TAttribute), Subject)
            .ForCondition(attribute is null || match is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be decorated with {0} matching the given predicate{reason}, but no matching attribute was found.", typeof(TAttribute));
        return new(this, match!);
    }

    /// <summary>Asserts the type is not decorated with the attribute <typeparamref name="TAttribute"/>.</summary>
    public AndConstraint<TypeAssertions> NotBeDecoratedWith<TAttribute>(string? because = null, params object?[] becauseArgs)
        where TAttribute : Attribute
    {
        Assert().ForCondition(FirstAttributeOrDefault<TAttribute>() is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be decorated with {0}{reason}.", typeof(TAttribute));
        return new(this);
    }

    /// <summary>Asserts the type is abstract (and not static, i.e. not also sealed).</summary>
    public AndConstraint<TypeAssertions> BeAbstract(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { IsAbstract: true, IsSealed: false }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be abstract{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the type is not abstract in the sense <see cref="BeAbstract"/> means it: a static class is
    /// abstract-and-sealed at the IL level but is reported as static, not abstract, so it passes here.
    /// A null subject passes.
    /// </summary>
    public AndConstraint<TypeAssertions> NotBeAbstract(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { IsAbstract: true, IsSealed: false }).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be abstract{reason}.");
        return new(this);
    }

    /// <summary>Asserts the type is sealed (and not static, i.e. not also abstract).</summary>
    public AndConstraint<TypeAssertions> BeSealed(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { IsSealed: true, IsAbstract: false }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be sealed{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the type is not sealed in the sense <see cref="BeSealed"/> means it, so a static class
    /// passes here for the same reason it passes <see cref="NotBeAbstract"/>. A null subject passes.
    /// </summary>
    public AndConstraint<TypeAssertions> NotBeSealed(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { IsSealed: true, IsAbstract: false }).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be sealed{reason}.");
        return new(this);
    }

    /// <summary>Asserts the type is static (abstract and sealed).</summary>
    public AndConstraint<TypeAssertions> BeStatic(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { IsAbstract: true, IsSealed: true }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be static{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the type is not static, i.e. not both abstract and sealed (a null subject passes).</summary>
    public AndConstraint<TypeAssertions> NotBeStatic(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { IsAbstract: true, IsSealed: true }).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be static{reason}.");
        return new(this);
    }

    /// <summary>Asserts the type is an interface.</summary>
    public AndConstraint<TypeAssertions> BeAnInterface(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { IsInterface: true }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be an interface{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the type is not an interface — a struct, enum or class all pass, and so does a null subject.</summary>
    public AndConstraint<TypeAssertions> NotBeAnInterface(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { IsInterface: true }).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be an interface{reason}.");
        return new(this);
    }

    /// <summary>Asserts the type is a class.</summary>
    public AndConstraint<TypeAssertions> BeAClass(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { IsClass: true }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be a class{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the type is not a class. <see cref="Type.IsClass"/> counts delegates as classes and
    /// interfaces as not classes, so an interface passes here while a delegate type fails.
    /// A null subject passes.
    /// </summary>
    public AndConstraint<TypeAssertions> NotBeAClass(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { IsClass: true }).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be a class{reason}.");
        return new(this);
    }

    private TAttribute? FirstAttributeOrDefault<TAttribute>() where TAttribute : Attribute
        => GetAttributes<TAttribute>().FirstOrDefault();

    // Attribute lookup is inherent to this assertion. Attributes are kept alongside the types the
    // trimmer preserves, so a type that is testable at all carries its attribute metadata.
    private IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute
        => Subject is null ? [] : Subject.GetCustomAttributes(typeof(TAttribute), inherit: false).OfType<TAttribute>();
}
