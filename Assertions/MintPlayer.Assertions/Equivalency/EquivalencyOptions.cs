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
