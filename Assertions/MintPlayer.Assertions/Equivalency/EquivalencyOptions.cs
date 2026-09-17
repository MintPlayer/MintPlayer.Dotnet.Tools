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
    // ⚠️ EVERY collection here is lazily created, and that is measured rather than tidy.
    //
    // They used to be created eagerly in the field initialisers, so every BeEquivalentTo call
    // allocated nine collections whether or not a single option was used -- and the overwhelming
    // majority of calls use none. Adding two more for the M5c options is what finally made it show:
    // the walk went 6,808 -> 6,984 B/op and the gate refused it. Making all nine lazy took it to
    // BELOW where it started.
    //
    // The pattern is `(field ??= new(...)).Add(x)` in the option method, and a shared empty sentinel
    // in the IEquivalencyOptions property. The engine already tests Count == 0 before consulting any
    // of them, so nothing downstream changes.
    private static readonly string[] NoStrings = [];
    private static readonly Dictionary<Type, IReadOnlyCollection<string>> NoMembersByType = [];
    private static readonly Dictionary<Type, Action<object?, object?>> NoComparers = [];
    private static readonly Type[] NoTypes = [];

    private HashSet<string>? excludedPaths;
    private Dictionary<Type, IReadOnlyCollection<string>>? nestedExclusions;
    private HashSet<string>? excludedWildcardPaths;
    private HashSet<string>? includedMembers;
    private Dictionary<Type, Action<object?, object?>>? customComparers;
    private HashSet<Type>? comparedByValue;
    private HashSet<Type>? comparedByMembers;
    private Dictionary<Type, IReadOnlyCollection<string>>? nestedInclusions;
    private HashSet<string>? strictOrderingPaths;

    private bool useStrictOrdering;
    private int maxDepth = 10;
    private bool useRuntimeTypes;
    private bool allowVacuousComparison;
    private MemberTraits includedMemberTraits;
    private bool includeDiagnostics;
    private MemberTraits excludedMemberKinds;
    private bool ignoreMissingMembers;
    private bool compareEnumsByName;
    private bool compareEnumsByValue;
    private bool ignoreStringCase;
    private bool useStrictTyping;
    private bool useAutoConversion;

    IReadOnlyCollection<string> IEquivalencyOptions.ExcludedPaths => excludedPaths ?? (IReadOnlyCollection<string>)NoStrings;
    IReadOnlyDictionary<Type, IReadOnlyCollection<string>> IEquivalencyOptions.NestedExclusions => nestedExclusions ?? NoMembersByType;
    IReadOnlyCollection<string> IEquivalencyOptions.ExcludedWildcardPaths => excludedWildcardPaths ?? (IReadOnlyCollection<string>)NoStrings;
    IReadOnlyCollection<string> IEquivalencyOptions.IncludedMembers => includedMembers ?? (IReadOnlyCollection<string>)NoStrings;
    IReadOnlyDictionary<Type, Action<object?, object?>> IEquivalencyOptions.CustomComparers => customComparers ?? NoComparers;
    IReadOnlyCollection<Type> IEquivalencyOptions.ComparedByValue => comparedByValue ?? (IReadOnlyCollection<Type>)NoTypes;
    IReadOnlyCollection<Type> IEquivalencyOptions.ComparedByMembers => comparedByMembers ?? (IReadOnlyCollection<Type>)NoTypes;
    bool IEquivalencyOptions.UseStrictOrdering => useStrictOrdering;
    int IEquivalencyOptions.MaxDepth => maxDepth;
    bool IEquivalencyOptions.UseRuntimeTypes => useRuntimeTypes;
    bool IEquivalencyOptions.AllowVacuousComparison => allowVacuousComparison;
    MemberTraits IEquivalencyOptions.IncludedMemberTraits => includedMemberTraits;
    bool IEquivalencyOptions.IncludeDiagnostics => includeDiagnostics;
    MemberTraits IEquivalencyOptions.ExcludedMemberKinds => excludedMemberKinds;
    IReadOnlyDictionary<Type, IReadOnlyCollection<string>> IEquivalencyOptions.NestedInclusions => nestedInclusions ?? NoMembersByType;
    IReadOnlyCollection<string> IEquivalencyOptions.StrictOrderingPaths => strictOrderingPaths ?? (IReadOnlyCollection<string>)NoStrings;
    bool IEquivalencyOptions.IgnoreMissingMembers => ignoreMissingMembers;
    bool IEquivalencyOptions.CompareEnumsByName => compareEnumsByName;
    bool IEquivalencyOptions.CompareEnumsByValue => compareEnumsByValue;
    bool IEquivalencyOptions.IgnoreStringCase => ignoreStringCase;
    bool IEquivalencyOptions.UseStrictTyping => useStrictTyping;
    bool IEquivalencyOptions.UseAutoConversion => useAutoConversion;

    /// <summary>
    /// Excludes the member selected by <paramref name="selector"/> from the comparison. Chained
    /// accesses are supported and map to a dotted path: <c>x =&gt; x.Address.City</c> excludes
    /// the path <c>"Address.City"</c>.
    /// </summary>
    public EquivalencyOptions<TExpectation> Excluding(Expression<Func<TExpectation, object?>> selector)
    {
        (excludedPaths ??= new(StringComparer.Ordinal)).Add(ParseMemberPath(selector));
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
        if ((nestedExclusions ??= []).TryGetValue(typeof(TNested), out var existing))
            (nestedExclusions ??= [])[typeof(TNested)] = [.. existing, name];
        else
            (nestedExclusions ??= [])[typeof(TNested)] = [name];
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
        (excludedWildcardPaths ??= new(StringComparer.Ordinal)).Add(wildcardPath);
        return this;
    }

    /// <summary>
    /// Restricts the comparison to the selected top-level member. Once any member is included,
    /// only included members are compared at the root; nested comparison below them is unaffected.
    /// The selector must access a single member (no chains).
    /// </summary>
    public EquivalencyOptions<TExpectation> Including(Expression<Func<TExpectation, object?>> selector)
    {
        (includedMembers ??= new(StringComparer.Ordinal)).Add(ParseSingleMemberName(selector));
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
        (customComparers ??= [])[typeof(TMember)] = (subject, expectation) => memberAssertion(
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
        (comparedByValue ??= []).Add(typeof(TType));
        // Null-conditional: the opposite set may never have been created, and removing from a set
        // that does not exist is a no-op rather than an error.
        comparedByMembers?.Remove(typeof(TType));
        return this;
    }

    /// <summary>
    /// Forces <typeparamref name="TType"/> to be compared member-by-member even when it would be
    /// treated as value-like by default (e.g. a type in a previous <see cref="ComparingByValue{TType}"/>).
    /// </summary>
    public EquivalencyOptions<TExpectation> ComparingByMembers<TType>()
    {
        (comparedByMembers ??= []).Add(typeof(TType));
        comparedByValue?.Remove(typeof(TType));
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
    /// Appends a summary of what the comparison actually did — nodes visited, members looked up,
    /// collection match probes — to the failure message.
    /// </summary>
    /// <remarks>
    /// For the two questions a bare difference list does not answer: <i>did it even look at the
    /// member I think it did</i>, and <i>why is this comparison slow</i>. A node count far larger
    /// than the graph means the walk is revisiting; a probe count near n² on an ordered collection
    /// means the matcher is not taking its fast path.
    /// <para>
    /// Costs nothing when unset — the counters are behind a thread-static flag this option is the
    /// only production caller of, and the summary is built only after a failure. Note it reports the
    /// walk, not the assertion: <c>NotBeEquivalentTo</c> fails when it finds NO differences, and the
    /// counts describe the search that found none.
    /// </para>
    /// </remarks>
    public EquivalencyOptions<TExpectation> WithDiagnostics()
    {
        includeDiagnostics = true;
        return this;
    }

    /// <summary>Compares properties only, leaving fields out of the comparison.</summary>
    /// <remarks>
    /// Answered from a generator-emitted flag, so the member list is filtered once per type and
    /// cached — the walk itself does no per-member testing and a comparison that does not use this
    /// is unaffected.
    /// <para>
    /// Excluding both kinds leaves nothing to compare, which the engine already rejects as a vacuous
    /// comparison rather than passing unconditionally. That is the right error and it needs no extra
    /// check here.
    /// </para>
    /// </remarks>
    public EquivalencyOptions<TExpectation> ExcludingFields()
    {
        excludedMemberKinds |= MemberTraits.Field;
        return this;
    }

    /// <summary>Compares fields only, leaving properties out of the comparison.</summary>
    /// <remarks>See <see cref="ExcludingFields"/>.</remarks>
    public EquivalencyOptions<TExpectation> ExcludingProperties()
    {
        excludedMemberKinds |= MemberTraits.Property;
        return this;
    }

    /// <summary>
    /// Also compares members marked <c>[EditorBrowsable(EditorBrowsableState.Never)]</c>, which are
    /// skipped by default.
    /// </summary>
    /// <remarks>
    /// The attribute hides a member from IntelliSense, which usually means "not part of the intended
    /// surface" — so comparing it by default would make an assertion fail over something the author
    /// deliberately hid. It is still sometimes exactly what you want to check, hence the opt-in.
    /// </remarks>
    public EquivalencyOptions<TExpectation> IncludingNonBrowsableMembers()
    {
        includedMemberTraits |= MemberTraits.NonBrowsable;
        return this;
    }

    /// <summary>
    /// Compares collections by matching items in any order. This is the default; the method exists so
    /// a shared options builder can be overridden, and so a call site can say so out loud.
    /// </summary>
    public EquivalencyOptions<TExpectation> WithoutStrictOrdering()
    {
        useStrictOrdering = false;
        return this;
    }

    /// <summary>
    /// Compares collections in order only at difference paths matching <paramref name="wildcardPath"/>
    /// (<c>*</c> and <c>?</c>), leaving the rest unordered.
    /// </summary>
    /// <remarks>
    /// The per-path counterpart of <see cref="WithStrictOrdering"/>, for the common shape where one
    /// collection in a graph is genuinely ordered and the others are not. Ordering the whole graph to
    /// express that makes every other collection assert something the code does not guarantee.
    /// </remarks>
    public EquivalencyOptions<TExpectation> WithStrictOrderingFor(string wildcardPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wildcardPath);
        (strictOrderingPaths ??= new(StringComparer.Ordinal)).Add(wildcardPath);
        return this;
    }

    /// <summary>
    /// Ignores expectation members the subject does not have, instead of reporting each as a
    /// difference.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This makes a whole class of mistake invisible</b> — a renamed or misspelled member on
    /// the expectation stops being reported and starts being skipped. It is here for the case it is
    /// genuinely for: comparing against a type that is a superset by design, such as a DTO checked
    /// against a richer domain model. If a comparison ends up with NO members left, the vacuity check
    /// still refuses it.
    /// </remarks>
    public EquivalencyOptions<TExpectation> ExcludingMissingMembers()
    {
        ignoreMissingMembers = true;
        return this;
    }

    /// <summary>Compares only the top level: members of members are treated as equal.</summary>
    /// <remarks>Equivalent to <c>WithMaxDepth(0)</c>, named for what it does at the call site.</remarks>
    public EquivalencyOptions<TExpectation> WithoutRecursing()
    {
        maxDepth = 0;
        return this;
    }

    /// <summary>
    /// Restricts the comparison of <typeparamref name="TNested"/> nodes to the selected member,
    /// wherever they appear in the graph. Call more than once to include several.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="ExcludingNested{TNested}"/>, and the one to reach for when a type has
    /// many members and two matter. Inclusion wins over the type's other members but not over an
    /// explicit exclusion — excluding something you also included is a contradiction, and the
    /// exclusion is the more specific statement of intent.
    /// </remarks>
    public EquivalencyOptions<TExpectation> IncludingNested<TNested>(Expression<Func<TNested, object?>> selector)
    {
        var name = ParseSingleMemberName(selector);
        if ((nestedInclusions ??= []).TryGetValue(typeof(TNested), out var existing))
            (nestedInclusions ??= [])[typeof(TNested)] = [.. existing, name];
        else
            (nestedInclusions ??= [])[typeof(TNested)] = [name];
        return this;
    }

    /// <summary>Compares enum values by their name rather than their numeric value.</summary>
    /// <remarks>
    /// The right choice when the two sides are different enum types that share names — a domain enum
    /// against a contract enum, say — where the numbers are an implementation detail nobody intended
    /// to line up.
    /// </remarks>
    public EquivalencyOptions<TExpectation> ComparingEnumsByName()
    {
        compareEnumsByName = true;
        compareEnumsByValue = false;
        return this;
    }

    /// <summary>Compares enum values by their numeric value, even across different enum types.</summary>
    /// <remarks>
    /// Note the default is neither of these: by default two enums compare with
    /// <see cref="object.Equals(object?)"/>, which is false for different enum types no matter what
    /// they contain. Pick one deliberately when the sides are not the same type.
    /// </remarks>
    public EquivalencyOptions<TExpectation> ComparingEnumsByValue()
    {
        compareEnumsByValue = true;
        compareEnumsByName = false;
        return this;
    }

    /// <summary>Compares string members ignoring casing.</summary>
    public EquivalencyOptions<TExpectation> ComparingStringsIgnoringCase()
    {
        ignoreStringCase = true;
        return this;
    }

    /// <summary>
    /// Requires the subject and the expectation to be the same runtime type at every structural node.
    /// </summary>
    /// <remarks>
    /// By default this library compares structurally and ignores the types entirely, which is what
    /// makes comparing a DTO against an anonymous object work. That is usually the point — and
    /// occasionally exactly what you need to rule out.
    /// </remarks>
    public EquivalencyOptions<TExpectation> WithStrictTyping()
    {
        useStrictTyping = true;
        return this;
    }

    /// <summary>
    /// Converts a value-like subject to the expectation's type before comparing, so <c>"1"</c>
    /// matches <c>1</c> and <c>1</c> matches <c>1.0m</c>.
    /// </summary>
    /// <remarks>
    /// For data that arrived untyped — a CSV row, a JSON document read as strings, a database
    /// reader — where insisting on the type is asserting something about the transport rather than
    /// about the value.
    /// <para>
    /// ⚠️ <b>It converts the SUBJECT to the expectation's type, never the other way round, and it
    /// never converts anything structural.</b> A conversion that throws or returns something unequal
    /// is simply a failed comparison, so this can only ever make a comparison pass that would
    /// otherwise fail on a representation difference — it cannot make one fail that would have
    /// passed. That one-directional guarantee is what makes it safe to reach for; losing it would
    /// turn a convenience into a source of false greens.
    /// </para>
    /// <para>
    /// It is also the exact opposite of <see cref="WithStrictTyping"/>. Setting both is a
    /// contradiction the engine does not police: strict typing is checked at structural nodes and
    /// conversion at value-like ones, so they mostly do not meet — but a call site asking for both is
    /// saying two things and should pick one.
    /// </para>
    /// </remarks>
    public EquivalencyOptions<TExpectation> WithAutoConversion()
    {
        useAutoConversion = true;
        return this;
    }

    /// <summary>Compares values without converting them. This is the default.</summary>
    /// <remarks>Here so a shared options builder can be overridden, and so a call site can say so.</remarks>
    public EquivalencyOptions<TExpectation> WithoutAutoConversion()
    {
        useAutoConversion = false;
        return this;
    }

    /// <summary>Compares members of type <typeparamref name="TMember"/> with the given comparer.</summary>
    /// <remarks>
    /// The comparer form of <see cref="Using{TMember}(Action{TMember, TMember})"/>, for when one
    /// already exists. It is turned into the same custom-comparison machinery, so the two cannot
    /// behave differently.
    /// </remarks>
    public EquivalencyOptions<TExpectation> Using<TMember>(IEqualityComparer<TMember> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        (customComparers ??= [])[typeof(TMember)] = (subject, expectation) =>
        {
            if (comparer.Equals((TMember)subject!, (TMember)expectation!)) return;
            throw new AssertionFailedException(
                $"expected {Formatting.Formatter.Format(expectation)}, but found {Formatting.Formatter.Format(subject)}");
        };
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
