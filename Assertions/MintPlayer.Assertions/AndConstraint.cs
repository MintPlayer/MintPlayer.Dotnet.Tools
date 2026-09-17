namespace MintPlayer.Assertions;

/// <summary>Enables chaining further assertions on the same subject via <see cref="And"/>.</summary>
/// <remarks>
/// <para>
/// ⚠️ <b>A readonly struct, and it must stay one.</b> This type is returned by every one of the
/// library's ~360 assertion methods, to carry a single reference that most callers immediately
/// discard. As a class that was one heap allocation on the passing path of every assertion in every
/// test in every suite.
/// </para>
/// <para>
/// Measured on a passing <c>42.Should().Be(42)</c>, after the arity-specific <c>FailWith</c>
/// overloads removed the params array: <b>24 B/op as a class, 0 as a struct</b> — the last
/// allocation between a passing assertion and free.
/// </para>
/// <para>
/// It is safe as a value type because it is immutable and tiny — one field — so copying it is
/// cheaper than the allocation it replaces, and there is no mutable state for a defensive copy to
/// silently discard. <c>PassingPathAllocationTests</c> is what notices if it ever becomes a class
/// again.
/// </para>
/// </remarks>
public readonly struct AndConstraint<TAssertions>
{
    /// <summary>Wraps the assertions object so the caller can continue with <see cref="And"/>.</summary>
    public AndConstraint(TAssertions parent) => And = parent;

    /// <summary>Continues asserting on the same subject.</summary>
    public TAssertions And { get; }
}

/// <summary>
/// An <see cref="AndConstraint{TAssertions}"/> that additionally exposes a value produced by the
/// assertion (e.g. the single item matched by ContainSingle) via <see cref="Which"/>.
/// </summary>
/// <remarks>
/// ⚠️ This <b>duplicates</b> <see cref="AndConstraint{TAssertions}.And"/> rather than inheriting it,
/// and that is not an oversight: structs cannot inherit. Duplicating one auto-property is the price
/// of not allocating on the passing path of every assertion that drills into a value, and it is
/// worth paying. If this ever needs to grow beyond a couple of fields, re-measure before turning it
/// back into a class — the allocation is the thing being traded away.
/// </remarks>
public readonly struct AndWhichConstraint<TAssertions, TWhich>
{
    /// <summary>Wraps the assertions object and the value the assertion drilled into.</summary>
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
