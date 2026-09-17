using System.Linq.Expressions;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Fluent configuration for <c>BeEquivalentTo</c>. Every method returns the same instance so
/// calls chain naturally inside the <c>config</c> lambda. Selector expressions are only
/// <em>parsed</em> for member names (never compiled), which keeps the options AOT-safe.
/// </summary>
/// <typeparam name="TExpectation">The static type of the expectation object.</typeparam>
public sealed class EquivalencyOptions<TExpectation> : IEquivalencyOptions
{
    // ⚠️ UNADDRESSED: these seven collections are built EAGERLY, in field initialisers, so every
    // BeEquivalentTo call allocates all of them whether or not a single option is used — and the
    // overwhelming majority of calls use none. Measured cost is not large next to the walk itself,
    // which is why it was not fixed during the passing-path work, but it is pure waste.
    //
    // The fix is to make each one nullable and lazily created by the method that populates it, with
    // the IEquivalencyOptions properties returning an empty sentinel when null. That is mechanical,
    // but it touches every option method, so it wants its own change and its own measurement rather
    // than being tacked onto something else.
    //
    // Note the engine already tests Count == 0 before consulting any of them, so making them lazy
    // is invisible to the walker — no consumer of IEquivalencyOptions needs to change.
    private readonly HashSet<string> excludedPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, IReadOnlyCollection<string>> nestedExclusions = [];
    private readonly HashSet<string> excludedWildcardPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> includedMembers = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Action<object?, object?>> customComparers = [];
    private readonly HashSet<Type> comparedByValue = [];
    private readonly HashSet<Type> comparedByMembers = [];
    private bool useStrictOrdering;
    private int maxDepth = 10;
    private bool useRuntimeTypes;
    private bool allowVacuousComparison;
    private MemberTraits includedMemberTraits;

    IReadOnlyCollection<string> IEquivalencyOptions.ExcludedPaths => excludedPaths;
    IReadOnlyDictionary<Type, IReadOnlyCollection<string>> IEquivalencyOptions.NestedExclusions => nestedExclusions;
    IReadOnlyCollection<string> IEquivalencyOptions.ExcludedWildcardPaths => excludedWildcardPaths;
    IReadOnlyCollection<string> IEquivalencyOptions.IncludedMembers => includedMembers;
    IReadOnlyDictionary<Type, Action<object?, object?>> IEquivalencyOptions.CustomComparers => customComparers;
    IReadOnlyCollection<Type> IEquivalencyOptions.ComparedByValue => comparedByValue;
    IReadOnlyCollection<Type> IEquivalencyOptions.ComparedByMembers => comparedByMembers;
    bool IEquivalencyOptions.UseStrictOrdering => useStrictOrdering;
    int IEquivalencyOptions.MaxDepth => maxDepth;
    bool IEquivalencyOptions.UseRuntimeTypes => useRuntimeTypes;
    bool IEquivalencyOptions.AllowVacuousComparison => allowVacuousComparison;
    MemberTraits IEquivalencyOptions.IncludedMemberTraits => includedMemberTraits;

    /// <summary>
    /// Excludes the member selected by <paramref name="selector"/> from the comparison. Chained
    /// accesses are supported and map to a dotted path: <c>x =&gt; x.Address.City</c> excludes
    /// the path <c>"Address.City"</c>.
    /// </summary>
    public EquivalencyOptions<TExpectation> Excluding(Expression<Func<TExpectation, object?>> selector)
    {
        excludedPaths.Add(ParseMemberPath(selector));
        return this;
    }

    /// <summary>
    /// Excludes the selected member on <em>every</em> node of type <typeparamref name="TNested"/>
    /// anywhere in the graph — including items inside collections. The selector must access a
    /// single member (no chains).
    /// </summary>
    public EquivalencyOptions<TExpectation> ExcludingNested<TNested>(Expression<Func<TNested, object?>> selector)
    {
        var name = ParseSingleMemberName(selector);
        if (nestedExclusions.TryGetValue(typeof(TNested), out var existing))
            nestedExclusions[typeof(TNested)] = [.. existing, name];
        else
            nestedExclusions[typeof(TNested)] = [name];
        return this;
    }

    /// <summary>
    /// Excludes every node whose difference path matches the given wildcard pattern
    /// (<c>*</c> matches any sequence, <c>?</c> matches one character), e.g. <c>"Items[*].Id"</c>
    /// or <c>"*.Timestamp"</c>.
    /// </summary>
    public EquivalencyOptions<TExpectation> ExcludingPath(string wildcardPath)
    {
        ArgumentNullException.ThrowIfNull(wildcardPath);
        excludedWildcardPaths.Add(wildcardPath);
        return this;
    }

    /// <summary>
    /// Restricts the comparison to the selected top-level member. Once any member is included,
    /// only included members are compared at the root; nested comparison below them is unaffected.
    /// The selector must access a single member (no chains).
    /// </summary>
    public EquivalencyOptions<TExpectation> Including(Expression<Func<TExpectation, object?>> selector)
    {
        includedMembers.Add(ParseSingleMemberName(selector));
        return this;
    }

    /// <summary>
    /// Uses a custom comparison for every member whose declared type is
    /// <typeparamref name="TMember"/>. The action receives (subject, expectation); throw an
    /// <see cref="AssertionFailedException"/> to report a difference — its message becomes the
    /// difference text for that path. For non-nullable value types a null value on either side
    /// is passed as <c>default</c>.
    /// </summary>
    public EquivalencyOptions<TExpectation> Using<TMember>(Action<TMember?, TMember?> memberAssertion)
    {
        ArgumentNullException.ThrowIfNull(memberAssertion);
        customComparers[typeof(TMember)] = (subject, expectation) => memberAssertion(
            subject is TMember s ? s : default,
            expectation is TMember e ? e : default);
        return this;
    }

    /// <summary>Compares collections pairwise in order instead of the default unordered matching.</summary>
    public EquivalencyOptions<TExpectation> WithStrictOrdering()
    {
        useStrictOrdering = true;
        return this;
    }

    /// <summary>
    /// Forces <typeparamref name="TType"/> to be compared via <see cref="object.Equals(object?, object?)"/>
    /// instead of member-by-member.
    /// </summary>
    public EquivalencyOptions<TExpectation> ComparingByValue<TType>()
    {
        comparedByValue.Add(typeof(TType));
        comparedByMembers.Remove(typeof(TType));
        return this;
    }

    /// <summary>
    /// Forces <typeparamref name="TType"/> to be compared member-by-member even when it would be
    /// treated as value-like by default (e.g. a type in a previous <see cref="ComparingByValue{TType}"/>).
    /// </summary>
    public EquivalencyOptions<TExpectation> ComparingByMembers<TType>()
    {
        comparedByMembers.Add(typeof(TType));
        comparedByValue.Remove(typeof(TType));
        return this;
    }

    /// <summary>
    /// Limits object-graph descent to the given depth (default 10). Nodes deeper than the limit
    /// are treated as equal without being compared.
    /// </summary>
    public EquivalencyOptions<TExpectation> WithMaxDepth(int maxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);
        this.maxDepth = maxDepth;
        return this;
    }

    /// <summary>
    /// Removes the descent-depth limit. Cyclic graphs remain safe: a (subject, expectation) pair
    /// already on the descent stack is treated as equal instead of recursing forever.
    /// </summary>
    public EquivalencyOptions<TExpectation> AllowingInfiniteRecursion()
    {
        maxDepth = int.MaxValue;
        return this;
    }

    /// <summary>
    /// Resolves expectation members from runtime types instead of declared types, so members that
    /// only exist on a derived runtime type also take part in the comparison.
    /// </summary>
    public EquivalencyOptions<TExpectation> RespectingRuntimeTypes()
    {
        useRuntimeTypes = true;
        return this;
    }

    /// <summary>
    /// Permits a comparison in which some node compares no members at all. Such an assertion
    /// cannot fail — the positive form passes for any pair of values and the negative form fails
    /// for any pair — so it is rejected with an <see cref="InvalidOperationException"/> by
    /// default. Call this when comparing nothing is genuinely intended, as in a generic or
    /// table-driven harness where some instantiations of the expectation type legitimately expose
    /// no members. Comparing two empty collections and comparing two memberless values are both
    /// already allowed and need no opt-in.
    /// </summary>
    public EquivalencyOptions<TExpectation> AllowingVacuousComparison()
    {
        allowVacuousComparison = true;
        return this;
    }

    /// <summary>
    /// Also compares <c>internal</c>, <c>protected</c> and <c>protected internal</c> members, which
    /// are skipped by default. <c>private</c> members are never compared.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This option can cost more than a branch.</b> Non-default members live in a separate
    /// generated table, and the generator can only emit one for a member it is allowed to reference
    /// — an <c>internal</c> member of a type in another assembly, with no <c>InternalsVisibleTo</c>,
    /// is not. For such a type the comparison falls back to reflection, which can see everything, so
    /// the answer stays right and the walk gets roughly 15× slower. That is charged only to
    /// comparisons that ask for it; a suite that never calls this is unaffected, byte for byte.
    /// </remarks>
    public EquivalencyOptions<TExpectation> IncludingInternalMembers()
    {
        includedMemberTraits |= MemberTraits.NonPublic;
        return this;
    }

    /// <summary>
    /// Extracts the dotted member path from a selector by walking its member-access chain.
    /// The expression is never compiled — only its <see cref="MemberExpression"/> names are read
    /// (with boxing <c>Convert</c> nodes unwrapped), which is AOT-safe.
    /// </summary>
    private static string ParseMemberPath(LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var names = new Stack<string>();
        var body = selector.Body;
        while (true)
        {
            switch (body)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary:
                    body = unary.Operand;
                    break;
                case MemberExpression member when member.Expression is not null:
                    names.Push(member.Member.Name);
                    body = member.Expression;
                    break;
                case ParameterExpression when names.Count > 0:
                    return string.Join(".", names);
                default:
                    throw new ArgumentException(
                        $"Only simple member access chains are supported (e.g. x => x.Address.City), but got '{selector}'.",
                        nameof(selector));
            }
        }
    }

    /// <summary>Like <see cref="ParseMemberPath"/> but requires exactly one member (no chain).</summary>
    private static string ParseSingleMemberName(LambdaExpression selector)
    {
        var path = ParseMemberPath(selector);
        return path.Contains('.')
            ? throw new ArgumentException(
                $"A single member access is required here (e.g. x => x.City), but got a chain: '{path}'.",
                nameof(selector))
            : path;
    }
}
