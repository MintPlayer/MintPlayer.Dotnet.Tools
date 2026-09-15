namespace MintPlayer.Assertions;

/// <summary>Enables chaining further assertions on the same subject via <see cref="And"/>.</summary>
/// <remarks>
/// A <c>readonly struct</c>, deliberately. Every one of the ~360 assertion methods in this library
/// returns one of these, so as a class it was a heap allocation on the PASSING path of every
/// assertion ever executed — the single most-repeated allocation in the library, paid to carry one
/// reference the caller usually discards.
///
/// The cost of that choice is that <see cref="AndWhichConstraint{TAssertions, TWhich}"/> can no
/// longer derive from this type (structs do not inherit), so it duplicates <see cref="And"/>. That
/// duplication is the price of the allocation, and it is worth it: nothing declares a variable of
/// either type, so the lost substitutability is theoretical.
/// </remarks>
public readonly struct AndConstraint<TAssertions>
{
    public AndConstraint(TAssertions parent) => And = parent;

    /// <summary>Continues asserting on the same subject.</summary>
    public TAssertions And { get; }
}

/// <summary>
/// An <see cref="AndConstraint{TAssertions}"/>-shaped result that additionally exposes a value
/// produced by the assertion (e.g. the single item matched by ContainSingle) via <see cref="Which"/>.
/// </summary>
/// <remarks>
/// Does not derive from <see cref="AndConstraint{TAssertions}"/> — see the remarks there for why.
/// It repeats <see cref="And"/> so both shapes chain identically.
/// </remarks>
public readonly struct AndWhichConstraint<TAssertions, TWhich>
{
    public AndWhichConstraint(TAssertions parent, TWhich which)
    {
        And = parent;
        Which = which;
    }

    /// <summary>Continues asserting on the same subject.</summary>
    public TAssertions And { get; }

    /// <summary>The value the assertion drilled into; assert further on it directly.</summary>
    public TWhich Which { get; }
}
