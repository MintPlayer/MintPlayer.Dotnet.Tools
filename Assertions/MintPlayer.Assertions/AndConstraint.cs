namespace MintPlayer.Assertions;

/// <summary>Enables chaining further assertions on the same subject via <see cref="And"/>.</summary>
public class AndConstraint<TAssertions>
{
    public AndConstraint(TAssertions parent) => And = parent;

    /// <summary>Continues asserting on the same subject.</summary>
    public TAssertions And { get; }
}

/// <summary>
/// An <see cref="AndConstraint{TAssertions}"/> that additionally exposes a value produced by the
/// assertion (e.g. the single item matched by ContainSingle) via <see cref="Which"/>.
/// </summary>
public class AndWhichConstraint<TAssertions, TWhich> : AndConstraint<TAssertions>
{
    public AndWhichConstraint(TAssertions parent, TWhich which) : base(parent) => Which = which;

    /// <summary>The value the assertion drilled into; assert further on it directly.</summary>
    public TWhich Which { get; }
}
