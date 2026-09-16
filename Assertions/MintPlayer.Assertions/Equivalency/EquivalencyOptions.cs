using System.Linq.Expressions;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Fluent configuration for <c>BeEquivalentTo</c>. Every method returns the same instance so
/// calls chain naturally inside the <c>config</c> lambda. Selector expressions are only
/// <em>parsed</em> for member names (never compiled), which keeps the options AOT-safe.
/// </summary>
/// <typeparam name="TExpectation">The static type of the expectation object.</typeparam>
/// <remarks>
/// Everything here is read once, when the comparison starts. Nothing an option adds runs per
/// assertion unless that option was actually set: the engine tests emptiness of each collection
/// before consulting it, so a default comparison walks the same code it did before any of this
/// existed.
/// </remarks>
public sealed class EquivalencyOptions<TExpectation> : IEquivalencyOptions
{
    private readonly HashSet<string> excludedPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, IReadOnlyCollection<string>> nestedExclusions = [];
    private readonly HashSet<string> excludedWildcardPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> excludedMemberNames = new(StringComparer.Ordinal);
    private readonly HashSet<Type> excludedMemberTypes = [];
    private readonly HashSet<string> includedPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> memberNameMappings = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Action<object?, object?>> customComparers = [];
    private readonly HashSet<Type> comparedByValue = [];
    private readonly HashSet<Type> comparedByMembers = [];
    private readonly HashSet<string> strictOrderingPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> looseOrderingPaths = new(StringComparer.Ordinal);
    private MemberInclusion inclusion = MemberInclusion.Default;
    private bool useStrictOrdering;
    private int maxDepth = 10;
    private bool useRuntimeTypes;
    private bool ignoreMissingMembers;
    private EnumComparison enumComparison = EnumComparison.ByValue;
    private RecordComparison recordComparison = RecordComparison.ByMembers;
    private StringMatchOptions stringOptions = StringMatchOptions.None;
    private bool treatNullAsEmptyString;
    private CyclicReferenceHandling cyclicReferenceHandling = CyclicReferenceHandling.Ignore;
    private bool allowVacuousComparison;

    IReadOnlyCollection<string> IEquivalencyOptions.ExcludedPaths => excludedPaths;
    IReadOnlyDictionary<Type, IReadOnlyCollection<string>> IEquivalencyOptions.NestedExclusions => nestedExclusions;
    IReadOnlyCollection<string> IEquivalencyOptions.ExcludedWildcardPaths => excludedWildcardPaths;
    IReadOnlyCollection<string> IEquivalencyOptions.ExcludedMemberNames => excludedMemberNames;
    IReadOnlyCollection<Type> IEquivalencyOptions.ExcludedMemberTypes => excludedMemberTypes;
    IReadOnlyCollection<string> IEquivalencyOptions.IncludedPaths => includedPaths;
    IReadOnlyDictionary<string, string> IEquivalencyOptions.MemberNameMappings => memberNameMappings;
    IReadOnlyDictionary<Type, Action<object?, object?>> IEquivalencyOptions.CustomComparers => customComparers;
    IReadOnlyCollection<Type> IEquivalencyOptions.ComparedByValue => comparedByValue;
    IReadOnlyCollection<Type> IEquivalencyOptions.ComparedByMembers => comparedByMembers;
    MemberInclusion IEquivalencyOptions.Inclusion => inclusion;
    bool IEquivalencyOptions.UseStrictOrdering => useStrictOrdering;
    IReadOnlyCollection<string> IEquivalencyOptions.StrictOrderingPaths => strictOrderingPaths;
    IReadOnlyCollection<string> IEquivalencyOptions.LooseOrderingPaths => looseOrderingPaths;
    int IEquivalencyOptions.MaxDepth => maxDepth;
    bool IEquivalencyOptions.UseRuntimeTypes => useRuntimeTypes;
    bool IEquivalencyOptions.IgnoreMissingMembers => ignoreMissingMembers;
    EnumComparison IEquivalencyOptions.EnumComparison => enumComparison;
    RecordComparison IEquivalencyOptions.RecordComparison => recordComparison;
    StringMatchOptions IEquivalencyOptions.StringOptions => stringOptions;
    bool IEquivalencyOptions.TreatNullAsEmptyString => treatNullAsEmptyString;
    CyclicReferenceHandling IEquivalencyOptions.CyclicReferenceHandling => cyclicReferenceHandling;
    bool IEquivalencyOptions.AllowVacuousComparison => allowVacuousComparison;

    #region Exclusions

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
    /// Excludes every member with this name, whatever type declares it and however deep it sits.
    /// </summary>
    /// <remarks>
    /// The blunt instrument next to <see cref="ExcludingNested{TNested}"/>: use it for a name that
    /// means the same thing everywhere — <c>"ModifiedOn"</c>, <c>"ETag"</c> — where naming every
    /// declaring type would be a list nobody maintains.
    /// </remarks>
    public EquivalencyOptions<TExpectation> ExcludingMembersNamed(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        excludedMemberNames.Add(name);
        return this;
    }

    /// <summary>Excludes every member whose declared type is <typeparamref name="TMember"/>.</summary>
    public EquivalencyOptions<TExpectation> Excluding<TMember>() => Excluding(typeof(TMember));

    /// <summary>Excludes every member whose declared type is <paramref name="memberType"/>.</summary>
    /// <remarks>
    /// Declared type, not runtime type: the walk has the declared type in hand before it reads the
    /// member, so the exclusion costs nothing and — more importantly — never depends on the value
    /// that happens to be there.
    /// </remarks>
    public EquivalencyOptions<TExpectation> Excluding(Type memberType)
    {
        ArgumentNullException.ThrowIfNull(memberType);
        excludedMemberTypes.Add(memberType);
        return this;
    }

    #endregion

    #region Inclusions

    /// <summary>
    /// Restricts the comparison to the selected member. Once any member is included, only included
    /// paths are compared. Chained accesses select a nested path: <c>x =&gt; x.Address.City</c>
    /// compares only that one leaf.
    /// </summary>
    /// <remarks>
    /// This used to be root-only, which quietly made <c>Including(x =&gt; x.Address.City)</c> mean
    /// "compare all of Address" — a weaker assertion than the one written. Prefixes of an included
    /// path stay walkable so the leaf can be reached, but nothing else under them is compared.
    /// </remarks>
    public EquivalencyOptions<TExpectation> Including(Expression<Func<TExpectation, object?>> selector)
    {
        includedPaths.Add(ParseMemberPath(selector));
        return this;
    }

    /// <summary>
    /// Restricts the comparison to nodes whose path matches this wildcard pattern, for the cases a
    /// selector cannot express — inside collections, most of all: <c>"Items[*].Id"</c>.
    /// </summary>
    public EquivalencyOptions<TExpectation> IncludingPath(string wildcardPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(wildcardPath);
        includedPaths.Add(wildcardPath);
        return this;
    }

    #endregion

    #region Member visibility

    /// <summary>Compares fields as well as properties. On by default.</summary>
    public EquivalencyOptions<TExpectation> IncludingFields() => WithInclusion(MemberInclusion.Fields, true);

    /// <summary>Compares properties only, ignoring fields.</summary>
    public EquivalencyOptions<TExpectation> ExcludingFields() => WithInclusion(MemberInclusion.Fields, false);

    /// <summary>Compares properties as well as fields. On by default.</summary>
    public EquivalencyOptions<TExpectation> IncludingProperties() => WithInclusion(MemberInclusion.Properties, true);

    /// <summary>Compares fields only, ignoring properties.</summary>
    public EquivalencyOptions<TExpectation> ExcludingProperties() => WithInclusion(MemberInclusion.Properties, false);

    /// <summary>
    /// Compares <c>internal</c> and <c>protected internal</c> members too. Off by default.
    /// </summary>
    /// <remarks>
    /// <c>private</c> and <c>protected</c> members are never included, even though reflection could
    /// reach them: the source generator cannot emit an accessor for one, so including them would
    /// make this option mean two different things depending on whether the type was scanned. That is
    /// a deliberate narrowing of FluentAssertions' surface, and FA splits this across
    /// <c>IncludingInternalFields</c> and <c>IncludingInternalProperties</c>, which combines
    /// confusingly with <see cref="ExcludingFields"/>. One flag, one meaning.
    /// </remarks>
    public EquivalencyOptions<TExpectation> IncludingInternalMembers() => WithInclusion(MemberInclusion.Internal, true);

    /// <summary>Compares public members only. The default.</summary>
    public EquivalencyOptions<TExpectation> ExcludingInternalMembers() => WithInclusion(MemberInclusion.Internal, false);

    /// <summary>Compares <c>[EditorBrowsable(Never)]</c> members. On by default — see <see cref="MemberInclusion"/>.</summary>
    public EquivalencyOptions<TExpectation> IncludingNonBrowsableMembers() => WithInclusion(MemberInclusion.NonBrowsable, true);

    /// <summary>Skips <c>[EditorBrowsable(Never)]</c> members, which is FluentAssertions' default.</summary>
    public EquivalencyOptions<TExpectation> ExcludingNonBrowsableMembers() => WithInclusion(MemberInclusion.NonBrowsable, false);

    /// <summary>Compares members reachable only through an explicitly implemented interface. Off by default.</summary>
    public EquivalencyOptions<TExpectation> IncludingExplicitInterfaceMembers() => WithInclusion(MemberInclusion.ExplicitInterface, true);

    /// <summary>Ignores explicitly implemented interface members. The default.</summary>
    public EquivalencyOptions<TExpectation> ExcludingExplicitInterfaceMembers() => WithInclusion(MemberInclusion.ExplicitInterface, false);

    private EquivalencyOptions<TExpectation> WithInclusion(MemberInclusion flag, bool on)
    {
        inclusion = on ? inclusion | flag : inclusion & ~flag;
        return this;
    }

    #endregion

    #region Name mapping

    /// <summary>
    /// Maps an expectation member name onto a differently named subject member.
    /// </summary>
    /// <remarks>
    /// Applied by name wherever it appears, not by path: the case this exists for is a DTO whose
    /// members were renamed relative to the domain type, and that rename is the same rename at every
    /// node it appears on.
    /// </remarks>
    public EquivalencyOptions<TExpectation> WithMapping(string expectationMemberName, string subjectMemberName)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectationMemberName);
        ArgumentException.ThrowIfNullOrEmpty(subjectMemberName);
        memberNameMappings[expectationMemberName] = subjectMemberName;
        return this;
    }

    /// <summary>The selector form of <see cref="WithMapping(string, string)"/>; both must select a single member.</summary>
    public EquivalencyOptions<TExpectation> WithMapping<TSubject>(
        Expression<Func<TExpectation, object?>> expectationSelector,
        Expression<Func<TSubject, object?>> subjectSelector)
        => WithMapping(ParseSingleMemberName(expectationSelector), ParseSingleMemberName(subjectSelector));

    #endregion

    #region Missing members

    /// <summary>
    /// Passes silently when the expectation has a member the subject does not, instead of reporting
    /// a difference.
    /// </summary>
    /// <remarks>
    /// ⚠️ This weakens every assertion it is applied to: a typo in an anonymous expectation object
    /// stops being an error and becomes a member nobody compares. It exists for the genuine case —
    /// comparing against an expectation type that is a superset of the subject — and should be
    /// reached for only then.
    /// </remarks>
    public EquivalencyOptions<TExpectation> ExcludingMissingMembers()
    {
        ignoreMissingMembers = true;
        return this;
    }

    /// <summary>Reports a missing subject member as a difference. The default.</summary>
    public EquivalencyOptions<TExpectation> ThrowingOnMissingMembers()
    {
        ignoreMissingMembers = false;
        return this;
    }

    #endregion

    #region Comparison behaviour

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

    /// <summary>
    /// Compares every member of declared type <typeparamref name="TMember"/> with
    /// <paramref name="comparer"/>.
    /// </summary>
    /// <remarks>
    /// The shorthand for the overwhelmingly common shape of <see cref="Using{TMember}(Action{TMember, TMember})"/>:
    /// an equality comparer, already written, that just needs to be plugged in — a tolerance
    /// comparer for doubles, a culture-aware string comparer, an identity comparer for entities.
    /// </remarks>
    public EquivalencyOptions<TExpectation> Using<TMember>(IEqualityComparer<TMember> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        customComparers[typeof(TMember)] = (subject, expectation) =>
        {
            var s = subject is TMember sv ? sv : default;
            var e = expectation is TMember ev ? ev : default;
            if (!comparer.Equals(s!, e!))
            {
                throw new AssertionFailedException(
                    $"expected {Formatting.Formatter.Format(expectation)}, but found {Formatting.Formatter.Format(subject)}");
            }
        };
        return this;
    }

    /// <summary>Compares collections pairwise in order instead of the default unordered matching.</summary>
    public EquivalencyOptions<TExpectation> WithStrictOrdering()
    {
        useStrictOrdering = true;
        return this;
    }

    /// <summary>
    /// Compares the collections at paths matching <paramref name="wildcardPath"/> in order, leaving
    /// every other collection unordered.
    /// </summary>
    public EquivalencyOptions<TExpectation> WithStrictOrderingFor(string wildcardPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(wildcardPath);
        strictOrderingPaths.Add(wildcardPath);
        looseOrderingPaths.Remove(wildcardPath);
        return this;
    }

    /// <summary>
    /// Matches the collections at paths matching <paramref name="wildcardPath"/> unordered, even
    /// under <see cref="WithStrictOrdering"/>.
    /// </summary>
    public EquivalencyOptions<TExpectation> WithoutStrictOrderingFor(string wildcardPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(wildcardPath);
        looseOrderingPaths.Add(wildcardPath);
        strictOrderingPaths.Remove(wildcardPath);
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
    /// Compares enums by name, so two unrelated enum types with matching member names are equivalent.
    /// </summary>
    public EquivalencyOptions<TExpectation> ComparingEnumsByName()
    {
        enumComparison = EnumComparison.ByName;
        return this;
    }

    /// <summary>Compares enums by underlying value. The default.</summary>
    public EquivalencyOptions<TExpectation> ComparingEnumsByValue()
    {
        enumComparison = EnumComparison.ByValue;
        return this;
    }

    /// <summary>
    /// Compares <c>record</c> types with their compiler-generated equality instead of member by
    /// member.
    /// </summary>
    /// <remarks>
    /// Faster, and stricter: the generated equality compares every member including the ones an
    /// anonymous expectation object would have left out, so this is only right when both sides are
    /// the same record type.
    /// </remarks>
    public EquivalencyOptions<TExpectation> ComparingRecordsByValue()
    {
        recordComparison = RecordComparison.ByValue;
        return this;
    }

    /// <summary>Compares records member by member, like any other structural node. The default.</summary>
    public EquivalencyOptions<TExpectation> ComparingRecordsByMembers()
    {
        recordComparison = RecordComparison.ByMembers;
        return this;
    }

    /// <summary>
    /// Applies <paramref name="options"/> to every string comparison in the graph — casing,
    /// whitespace, newline style.
    /// </summary>
    /// <remarks>
    /// The same <see cref="StringMatchOptions"/> the string assertions take, so "ignoring newline
    /// style" means one thing in this library rather than two.
    /// </remarks>
    public EquivalencyOptions<TExpectation> ComparingStringsWith(StringMatchOptions options)
    {
        stringOptions = options;
        return this;
    }

    /// <summary>Treats a null string and an empty string as equal.</summary>
    public EquivalencyOptions<TExpectation> TreatingNullAsEmptyString()
    {
        treatNullAsEmptyString = true;
        return this;
    }

    #endregion

    #region Depth and recursion

    /// <summary>
    /// Limits object-graph descent to the given depth (default 10). A node deeper than the limit is
    /// reported as a difference.
    /// </summary>
    /// <remarks>
    /// It used to be treated as <em>equal</em> and not reported at all, which is the worst failure
    /// mode an assertion library has: a graph one level deeper than the default passed without
    /// comparing anything below the cut, and said nothing. Raise the limit or call
    /// <see cref="AllowingInfiniteRecursion"/> when the graph is genuinely deep.
    /// </remarks>
    public EquivalencyOptions<TExpectation> WithMaxDepth(int maxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);
        this.maxDepth = maxDepth;
        return this;
    }

    /// <summary>
    /// Removes the descent-depth limit. Cyclic graphs remain safe: a (subject, expectation) pair
    /// already on the descent stack is handled per <see cref="CyclicReferenceHandling"/>.
    /// </summary>
    public EquivalencyOptions<TExpectation> AllowingInfiniteRecursion()
    {
        maxDepth = int.MaxValue;
        return this;
    }

    /// <summary>Reports a cyclic reference as a difference instead of treating the pair as equal.</summary>
    public EquivalencyOptions<TExpectation> ThrowingOnCyclicReferences()
    {
        cyclicReferenceHandling = CyclicReferenceHandling.ThrowException;
        return this;
    }

    /// <summary>Treats a cyclic reference as equal and stops descending. The default.</summary>
    public EquivalencyOptions<TExpectation> IgnoringCyclicReferences()
    {
        cyclicReferenceHandling = CyclicReferenceHandling.Ignore;
        return this;
    }

    #endregion

    #region Types and vacuity

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

    #endregion

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
