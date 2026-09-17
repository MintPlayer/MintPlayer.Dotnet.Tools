namespace MintPlayer.Assertions.Collections;

public partial class GenericCollectionAssertions<T>
{
    /// <summary>
    /// Asserts the items are ordered by <paramref name="selector"/>, and allows the ordering to be
    /// continued with <c>ThenBeInAscendingOrder</c> / <c>ThenBeInDescendingOrder</c> for ties.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Named <c>BeOrderedBy</c> rather than being another <c>BeInAscendingOrder</c> overload,
    /// on purpose.</b> Adding a <c>Then</c> to the existing method would mean changing its return
    /// type, and that is a breaking change to a shipped surface for the sake of one feature. It is
    /// also the milestone's naming trap in a second form: a new overload differing only in return
    /// type cannot be selected by the caller, and one differing by an added parameter risks being
    /// swallowed by the <c>because</c>/<c>becauseArgs</c> tail that ends every assertion here.
    /// <para>
    /// <c>BeInAscendingOrder(selector)</c> stays exactly as it was and remains the right call when
    /// there is only one key.
    /// </para>
    /// </remarks>
    public OrderedCollectionConstraint<T> BeOrderedBy<TKey>(Func<T, TKey> selector, string? because = null, params object?[] becauseArgs)
        where TKey : IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(selector);
        return AssertOrderedBy(new KeyComparer<TKey>(selector), descending: false, because, becauseArgs);
    }

    /// <summary>
    /// Asserts the items are ordered by <paramref name="selector"/> descending, continuable with
    /// <c>Then…</c> for ties. See <see cref="BeOrderedBy"/> for why this is not an overload of
    /// <c>BeInDescendingOrder</c>.
    /// </summary>
    public OrderedCollectionConstraint<T> BeOrderedByDescending<TKey>(Func<T, TKey> selector, string? because = null, params object?[] becauseArgs)
        where TKey : IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(selector);
        return AssertOrderedBy(new KeyComparer<TKey>(selector), descending: true, because, becauseArgs);
    }

    internal OrderedCollectionConstraint<T> AssertOrderedBy(IComparer<T> comparer, bool descending, string? because, object?[] becauseArgs)
    {
        AssertOrder(comparer, descending, because, becauseArgs);
        return new(this, comparer, descending);
    }

    /// <summary>Exposes the private key comparer to the continuation constraint.</summary>
    internal static IComparer<T> CreateKeyComparer<TKey>(Func<T, TKey> selector) where TKey : IComparable<TKey>
        => new KeyComparer<TKey>(selector);
}

/// <summary>
/// The result of <c>BeOrderedBy</c>: chains further assertions through <see cref="And"/>, or extends
/// the ordering key with <c>ThenBeInAscendingOrder</c> / <c>ThenBeInDescendingOrder</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>A readonly struct, for the same reason <c>AndConstraint</c> is one.</b> It is returned on
/// the passing path of an assertion; as a class it would be a heap allocation per call. It carries
/// three fields rather than one, which is still cheaper than allocating — but if it ever needs to
/// grow further, re-measure rather than assume.
/// </para>
/// <para>
/// ⚠️ <b><c>Then…</c> re-checks the WHOLE collection with a composite comparer; it does not check
/// only the tied runs.</b> Those are the same answer — a sequence is ordered by (a, b) exactly when
/// every adjacent pair is — and the composite form is the one that cannot get the tie boundaries
/// wrong. It costs one more linear pass per <c>Then</c>, on an assertion the caller opted into.
/// </para>
/// </remarks>
public readonly struct OrderedCollectionConstraint<T>
{
    private readonly GenericCollectionAssertions<T> parent;
    private readonly IComparer<T> comparer;
    private readonly bool descending;

    internal OrderedCollectionConstraint(GenericCollectionAssertions<T> parent, IComparer<T> comparer, bool descending)
    {
        this.parent = parent;
        this.comparer = comparer;
        this.descending = descending;
    }

    /// <summary>Continues asserting on the same subject.</summary>
    public GenericCollectionAssertions<T> And => parent;

    /// <summary>Breaks ties in the ordering so far with <paramref name="selector"/>, ascending.</summary>
    public OrderedCollectionConstraint<T> ThenBeInAscendingOrder<TKey>(Func<T, TKey> selector, string? because = null, params object?[] becauseArgs)
        where TKey : IComparable<TKey>
        => Then(selector, thenDescending: false, because, becauseArgs);

    /// <summary>Breaks ties in the ordering so far with <paramref name="selector"/>, descending.</summary>
    public OrderedCollectionConstraint<T> ThenBeInDescendingOrder<TKey>(Func<T, TKey> selector, string? because = null, params object?[] becauseArgs)
        where TKey : IComparable<TKey>
        => Then(selector, thenDescending: true, because, becauseArgs);

    private OrderedCollectionConstraint<T> Then<TKey>(Func<T, TKey> selector, bool thenDescending, string? because, object?[] becauseArgs)
        where TKey : IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(selector);

        // The composite compares on the earlier key first and only consults the new one on a tie,
        // which is what "then by" means. Each level carries its own direction, so
        // BeOrderedByDescending(...).ThenBeInAscendingOrder(...) is expressible.
        var composite = new CompositeComparer(
            comparer, descending,
            GenericCollectionAssertions<T>.CreateKeyComparer(selector), thenDescending);

        return parent.AssertOrderedBy(composite, descending: false, because, becauseArgs);
    }

    /// <summary>
    /// Applies the outer comparison first and the inner one only on a tie, each with its own
    /// direction folded in — so the result is always "ascending" to the caller.
    /// </summary>
    private sealed class CompositeComparer(IComparer<T> outer, bool outerDescending, IComparer<T> inner, bool innerDescending)
        : IComparer<T>
    {
        public int Compare(T? x, T? y)
        {
            var first = outer.Compare(x, y);
            if (outerDescending) first = -first;
            if (first != 0) return first;

            var second = inner.Compare(x, y);
            return innerDescending ? -second : second;
        }
    }
}
