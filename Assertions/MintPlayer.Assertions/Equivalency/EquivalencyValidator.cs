using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Formatting;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// The recursive object-graph comparison engine behind <c>BeEquivalentTo</c>. Walks subject and
/// expectation side by side, driven by the expectation's members, and collects every structural
/// difference with its path. Member access goes through <see cref="RegistryMemberProvider"/>
/// (source-generated accessors first, reflection fallback), so the engine itself is
/// reflection-free and AOT-safe.
/// </summary>
/// <remarks>
/// Behavioral notes:
/// <list type="bullet">
/// <item>A (subject, expectation) reference pair already on the current descent stack is treated as
/// equal, or reported, per <see cref="IEquivalencyOptions.CyclicReferenceHandling"/>.</item>
/// <item>A node deeper than <see cref="IEquivalencyOptions.MaxDepth"/> is reported as a difference.
/// It used to be treated as equal and not reported, which made a too-deep graph pass silently.</item>
/// <item>Unordered collection matching is a maximum bipartite matching, not greedy first-fit. When
/// every item on both sides is value-like, a hash-based multiset comparison is used instead of the
/// O(n²) matching.</item>
/// <item>Every option below is tested for emptiness before it is consulted, so a default comparison
/// walks the same code it did before the options existed.</item>
/// </list>
/// </remarks>
internal static class EquivalencyValidator
{
    /// <summary>
    /// Compares <paramref name="subject"/> against <paramref name="expectation"/> and returns all
    /// differences found (empty when equivalent), together with the first vacuous node detected.
    /// <paramref name="rootDeclaredType"/> is the static type of the expectation at the call site,
    /// used for member resolution and custom comparer lookup at the root.
    /// </summary>
    public static ValidationResult Validate(object? subject, object? expectation, IEquivalencyOptions options, Type? rootDeclaredType = null)
    {
        var differences = new List<Difference>();
        var context = new Context(options);
        CompareNode(context, differences, string.Empty, subject, expectation, rootDeclaredType, 0);
        return new(differences, context.Vacuity);
    }

    /// <summary>Per-validation state: the options, the cycle-detection descent stack and the first vacuous node.</summary>
    private sealed class Context(IEquivalencyOptions options)
    {
        public IEquivalencyOptions Options { get; } = options;
        public IMemberProvider MemberProvider { get; } = RegistryMemberProvider.Instance;

        /// <summary>
        /// The first structural node at which nothing was compared, or null when every structural
        /// node asserted something. Only the first is kept: it is enough to explain the mistake,
        /// and reporting every node would bury the cause under its consequences.
        /// </summary>
        public VacuousNode? Vacuity { get; private set; }

        public void ReportVacuous(VacuousNode node) => Vacuity ??= node;

        private readonly HashSet<(object Subject, object Expectation)> descentStack = new(ReferencePairComparer.Instance);

        /// <summary>False when this reference pair is already being compared higher up the stack.</summary>
        public bool TryPush(object subject, object expectation) => descentStack.Add((subject, expectation));

        public void Pop(object subject, object expectation) => descentStack.Remove((subject, expectation));

        private sealed class ReferencePairComparer : IEqualityComparer<(object Subject, object Expectation)>
        {
            public static ReferencePairComparer Instance { get; } = new();

            public bool Equals((object Subject, object Expectation) x, (object Subject, object Expectation) y)
                => ReferenceEquals(x.Subject, y.Subject) && ReferenceEquals(x.Expectation, y.Expectation);

            public int GetHashCode((object Subject, object Expectation) pair)
                => HashCode.Combine(RuntimeHelpers.GetHashCode(pair.Subject), RuntimeHelpers.GetHashCode(pair.Expectation));
        }
    }

    private static void CompareNode(Context context, List<Difference> differences, string path,
        object? subject, object? expectation, Type? declaredType, int depth)
    {
        if (IsExcluded(context.Options, path)) return;
        if (!IsWithinInclusions(context.Options, path)) return;

        var comparerType = declaredType ?? expectation?.GetType() ?? subject?.GetType();
        if (comparerType is not null && TryGetCustomComparer(context.Options, comparerType, out var comparer))
        {
            try
            {
                comparer(subject, expectation);
            }
            catch (AssertionFailedException ex)
            {
                differences.Add(new(path, ex.Message));
            }
            return;
        }

        if (subject is null && expectation is null) return;

        // Null-as-empty is decided before the null branches below, because that is exactly the pair
        // it is about: one side null, the other an empty string.
        if (context.Options.TreatNullAsEmptyString && IsNullOrEmptyString(subject) && IsNullOrEmptyString(expectation)) return;

        if (expectation is null)
        {
            differences.Add(new(path, $"expected <null>, but found {Formatter.Format(subject)}"));
            return;
        }
        if (subject is null)
        {
            differences.Add(new(path, $"expected {Formatter.Format(expectation)}, but found <null>"));
            return;
        }

        if (IsValueLike(expectation.GetType(), context.Options))
        {
            if (!ValuesEqual(context.Options, subject, expectation))
                differences.Add(new(path, $"expected {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
            return;
        }

        if (context.Options.RecordComparison == RecordComparison.ByValue && IsRecord(expectation.GetType()))
        {
            if (!Equals(subject, expectation))
                differences.Add(new(path, $"expected {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
            return;
        }

        // Depth limit. Reported rather than silently treated as equal: a graph one level deeper
        // than the limit used to pass without comparing anything below the cut, and say nothing.
        if (depth > context.Options.MaxDepth)
        {
            differences.Add(new(path,
                $"the graph is deeper than the configured maximum depth of {context.Options.MaxDepth}, so nothing below this point was compared — raise WithMaxDepth(...) or call AllowingInfiniteRecursion()"));
            return;
        }

        // Cycle guard: a pair already on the descent stack is being compared higher up.
        if (!context.TryPush(subject, expectation))
        {
            if (context.Options.CyclicReferenceHandling == CyclicReferenceHandling.ThrowException)
                differences.Add(new(path, "expected an acyclic graph, but this node refers back to one already being compared"));
            return;
        }

        try
        {
            if (expectation is IDictionary expectationDictionary)
            {
                // Kept as the primary path even though the pair-based one below could serve it:
                // IDictionary.Contains and the indexer go through the subject's OWN key comparer, so
                // a Dictionary<string, T>(StringComparer.OrdinalIgnoreCase) still matches its keys
                // the way it would anywhere else. Rebuilding the lookup with default equality would
                // silently take that away.
                if (subject is IDictionary subjectDictionary)
                    CompareDictionaries(context, differences, path, subjectDictionary, expectationDictionary, depth);
                else
                    differences.Add(new(path, $"expected a dictionary {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
            }
            else if (TryGetPairs(expectation, out var expectationPairs))
            {
                if (TryGetPairs(subject, out var subjectPairs))
                    ComparePairs(context, differences, path, subjectPairs, expectationPairs, depth);
                else
                    differences.Add(new(path, $"expected a dictionary {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
            }
            else if (expectation is IEnumerable expectationEnumerable and not string)
            {
                if (subject is IEnumerable subjectEnumerable and not string)
                    CompareCollections(context, differences, path, subjectEnumerable, expectationEnumerable, declaredType, depth);
                else
                    differences.Add(new(path, $"expected a collection {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
            }
            else
            {
                CompareMembers(context, differences, path, subject, expectation, declaredType, depth);
            }
        }
        finally
        {
            context.Pop(subject, expectation);
        }
    }

    private static void CompareMembers(Context context, List<Difference> differences, string path,
        object subject, object expectation, Type? declaredType, int depth)
    {
        var options = context.Options;
        var expectationType = ResolveNodeType(options, declaredType, expectation);
        var expectationMembers = context.MemberProvider.GetMembers(expectationType);
        var subjectMembers = context.MemberProvider.GetMembers(subject.GetType());
        var excludedNames = GetNestedExclusions(options, expectationType, subject.GetType());

        // Counts the members that actually took part in the comparison. Zero of them means this
        // node asserted nothing and therefore cannot fail — see the ValidationResult docs.
        var comparedMembers = 0;
        var visibleSubjectMembers = 0;

        for (var i = 0; i < subjectMembers.Count; i++)
        {
            if (IsMemberVisible(options, subjectMembers[i])) visibleSubjectMembers++;
        }

        for (var i = 0; i < expectationMembers.Count; i++)
        {
            var expectationMember = expectationMembers[i];
            if (!IsMemberVisible(options, expectationMember)) continue;
            if (excludedNames is not null && excludedNames.Contains(expectationMember.Name)) continue;
            if (options.ExcludedMemberNames.Count > 0 && options.ExcludedMemberNames.Contains(expectationMember.Name)) continue;
            if (options.ExcludedMemberTypes.Count > 0 && options.ExcludedMemberTypes.Contains(expectationMember.Type)) continue;

            var childPath = path.Length == 0 ? expectationMember.Name : $"{path}.{expectationMember.Name}";

            var subjectMember = FindByName(options, subjectMembers, MapToSubjectName(options, expectationMember.Name));
            if (subjectMember is null)
            {
                if (!options.IgnoreMissingMembers && !IsExcluded(options, childPath) && IsWithinInclusions(options, childPath))
                {
                    differences.Add(new(childPath, $"expectation has member {expectationMember.Name} but subject does not"));
                    comparedMembers++;
                }
                continue;
            }

            if (IsExcluded(options, childPath)) continue;
            if (!IsWithinInclusions(options, childPath)) continue;
            comparedMembers++;

            CompareNode(context, differences, childPath,
                subjectMember.Getter(subject), expectationMember.Getter(expectation),
                expectationMember.Type, depth + 1);
        }

        // A structural node that compared nothing can never fail. Two memberless values really
        // are equivalent, so the subject must have members for this to count as vacuous at all.
        if (comparedMembers > 0 || visibleSubjectMembers == 0) return;

        // Which of the two causes it is decides where it counts as a mistake.
        //
        // An expectation type with no comparable members is never a way to express intent: an
        // expectation erased to `object`, or a type whose members are all non-public, is a
        // mistake wherever it appears — including on a collection element or a nested member,
        // where it would otherwise hide inside an assertion that looks meaningful overall.
        //
        // Options that removed every member are different. At the root the whole assertion is
        // left asserting nothing, so it is still a mistake. Deeper down, excluding every member
        // of a subtree is the normal way to say "do not compare this subtree" —
        // ExcludingNested<AuditInfo>(a => a.ModifiedOn) on a type whose only member is
        // ModifiedOn means exactly that, and refusing it would reject correct, idiomatic use.
        var visibleExpectationMembers = 0;
        for (var i = 0; i < expectationMembers.Count; i++)
        {
            if (IsMemberVisible(options, expectationMembers[i])) visibleExpectationMembers++;
        }

        if (visibleExpectationMembers == 0 || path.Length == 0)
        {
            context.ReportVacuous(new(path, expectationType, subject.GetType(),
                ExpectationHasNoMembers: visibleExpectationMembers == 0));
        }
    }

    private static void CompareDictionaries(Context context, List<Difference> differences, string path,
        IDictionary subject, IDictionary expectation, int depth)
    {
        foreach (var key in expectation.Keys)
        {
            var keyText = Convert.ToString(key, CultureInfo.InvariantCulture);
            var childPath = $"{path}[{keyText}]";
            if (!subject.Contains(key!))
            {
                if (!IsExcluded(context.Options, childPath))
                    differences.Add(new(path, $"expected dictionary to contain key {Formatter.Format(key)}, but it was not found"));
                continue;
            }
            CompareNode(context, differences, childPath, subject[key!], expectation[key!], null, depth + 1);
        }

        var extraKeys = new List<object?>();
        foreach (var key in subject.Keys)
        {
            if (!expectation.Contains(key!)) extraKeys.Add(key);
        }
        if (extraKeys.Count > 0)
            differences.Add(new(path, $"found unexpected key(s) {Formatter.Format(extraKeys)}"));
    }

    /// <summary>
    /// The same key-based comparison for a dictionary that only implements the generic interfaces,
    /// where there is no untyped <see cref="IDictionary.Contains"/> to lean on.
    /// </summary>
    /// <remarks>
    /// Keys are matched with default equality here, not the source dictionary's comparer — there is
    /// no way to reach it through <c>IReadOnlyDictionary&lt;K,V&gt;</c>. That is still strictly
    /// better than what these types used to get, which was the collection path: a bag of pairs, and
    /// a value difference on a matching key reported as "no equivalent item was found".
    /// </remarks>
    private static void ComparePairs(Context context, List<Difference> differences, string path,
        List<KeyValuePair<object?, object?>> subject, List<KeyValuePair<object?, object?>> expectation, int depth)
    {
        var subjectByKey = new Dictionary<object, KeyValuePair<object?, object?>>();
        KeyValuePair<object?, object?>? subjectNullKey = null;
        foreach (var pair in subject)
        {
            if (pair.Key is null) subjectNullKey = pair;
            else subjectByKey[pair.Key] = pair;
        }

        var matchedKeys = new HashSet<object>();
        var matchedNullKey = false;

        foreach (var pair in expectation)
        {
            var keyText = Convert.ToString(pair.Key, CultureInfo.InvariantCulture);
            var childPath = $"{path}[{keyText}]";

            KeyValuePair<object?, object?> found;
            if (pair.Key is null)
            {
                if (subjectNullKey is null)
                {
                    if (!IsExcluded(context.Options, childPath))
                        differences.Add(new(path, "expected dictionary to contain key <null>, but it was not found"));
                    continue;
                }
                found = subjectNullKey.Value;
                matchedNullKey = true;
            }
            else if (!subjectByKey.TryGetValue(pair.Key, out found))
            {
                if (!IsExcluded(context.Options, childPath))
                    differences.Add(new(path, $"expected dictionary to contain key {Formatter.Format(pair.Key)}, but it was not found"));
                continue;
            }
            else
            {
                matchedKeys.Add(pair.Key);
            }

            CompareNode(context, differences, childPath, found.Value, pair.Value, null, depth + 1);
        }

        var extraKeys = new List<object?>();
        if (subjectNullKey is not null && !matchedNullKey) extraKeys.Add(null);
        foreach (var key in subjectByKey.Keys)
        {
            if (!matchedKeys.Contains(key)) extraKeys.Add(key);
        }
        if (extraKeys.Count > 0)
            differences.Add(new(path, $"found unexpected key(s) {Formatter.Format(extraKeys)}"));
    }

    private static void CompareCollections(Context context, List<Difference> differences, string path,
        IEnumerable subject, IEnumerable expectation, Type? declaredType, int depth)
    {
        var subjectItems = Materialize(subject);
        var expectationItems = Materialize(expectation);
        var itemDeclaredType = GetElementType(declaredType);

        if (subjectItems.Count != expectationItems.Count)
            differences.Add(new(path, $"expected {expectationItems.Count} item(s), but found {subjectItems.Count}"));

        if (UseStrictOrderingAt(context.Options, path))
        {
            var count = Math.Min(subjectItems.Count, expectationItems.Count);
            for (var i = 0; i < count; i++)
            {
                CompareNode(context, differences, $"{path}[{i}]", subjectItems[i], expectationItems[i], itemDeclaredType, depth + 1);
            }
            return;
        }

        if (AllItemsValueLike(context.Options, subjectItems) && AllItemsValueLike(context.Options, expectationItems))
        {
            CompareMultisets(context.Options, differences, path, subjectItems, expectationItems);
            return;
        }

        // Maximum bipartite matching, not greedy first-fit.
        //
        // Greedy was WRONG, not merely slow: each expectation claimed the first unmatched subject
        // it was equivalent to, so it could strand a later expectation that had only one candidate
        // left. Expectations [A, B] against subjects [X, Y], where A is equivalent to both and B
        // only to X: greedy gives X to A, leaves B with nothing, and reports a difference even
        // though the perfect matching A-Y, B-X exists. Augmenting paths find that matching.
        //
        // It also allocated a List<Difference> per (expectation x subject) probe — a failing 20x20
        // comparison built roughly 400 lists to throw them all away. One reusable collector now
        // serves every probe.
        var candidates = BuildCandidateMatrix(context, path, subjectItems, expectationItems, itemDeclaredType, depth);

        // subjectMatchedTo[s] = index of the expectation currently holding subject item s, or -1.
        var subjectMatchedTo = new int[subjectItems.Count];
        Array.Fill(subjectMatchedTo, -1);

        var visited = new bool[subjectItems.Count];
        for (var e = 0; e < expectationItems.Count; e++)
        {
            Array.Clear(visited);
            if (!TryAssign(e, candidates, subjectMatchedTo, visited))
            {
                differences.Add(new(path, $"expected collection to contain {Formatter.Format(expectationItems[e])}, but no equivalent item was found"));
            }
        }

        var extras = new List<object?>();
        for (var i = 0; i < subjectItems.Count; i++)
        {
            if (subjectMatchedTo[i] < 0) extras.Add(subjectItems[i]);
        }
        if (extras.Count > 0)
            differences.Add(new(path, $"found unexpected item(s) {Formatter.Format(extras)}"));
    }

    /// <summary>
    /// candidates[e * subjectCount + s] — whether expectation item <c>e</c> is equivalent to
    /// subject item <c>s</c>.
    /// </summary>
    /// <remarks>
    /// Flattened into one array rather than a jagged one: a single allocation instead of one per
    /// row. The comparisons themselves share a single collector that is cleared between probes,
    /// because their only purpose here is the yes/no answer — the differences they produce are
    /// never reported, since a failure to match is described in terms of the whole item.
    /// </remarks>
    private static bool[] BuildCandidateMatrix(Context context, string path,
        List<object?> subjectItems, List<object?> expectationItems, Type? itemDeclaredType, int depth)
    {
        var candidates = new bool[expectationItems.Count * subjectItems.Count];
        var probe = new List<Difference>();

        for (var e = 0; e < expectationItems.Count; e++)
        {
            for (var sIndex = 0; sIndex < subjectItems.Count; sIndex++)
            {
                probe.Clear();
                CompareNode(context, probe, $"{path}[?]", subjectItems[sIndex], expectationItems[e], itemDeclaredType, depth + 1);
                candidates[(e * subjectItems.Count) + sIndex] = probe.Count == 0;
            }
        }

        return candidates;
    }

    /// <summary>
    /// Kuhn's augmenting-path step: try to find a subject item for expectation <paramref name="e"/>,
    /// displacing earlier assignments when they have an alternative of their own.
    /// </summary>
    private static bool TryAssign(int e, bool[] candidates, int[] subjectMatchedTo, bool[] visited)
    {
        var subjectCount = subjectMatchedTo.Length;
        for (var s = 0; s < subjectCount; s++)
        {
            if (visited[s] || !candidates[(e * subjectCount) + s]) continue;

            visited[s] = true;
            if (subjectMatchedTo[s] < 0 || TryAssign(subjectMatchedTo[s], candidates, subjectMatchedTo, visited))
            {
                subjectMatchedTo[s] = e;
                return true;
            }
        }

        return false;
    }

    /// <summary>Hash-based multiset comparison for collections of value-like items (avoids O(n²)).</summary>
    private static void CompareMultisets(IEquivalencyOptions options, List<Difference> differences, string path,
        List<object?> subjectItems, List<object?> expectationItems)
    {
        // Any option that changes what "equal" means for a value also changes what belongs in the
        // same hash bucket, so the shortcut is only sound while values compare by Equals.
        if (NeedsCustomValueEquality(options))
        {
            CompareMultisetsPairwise(options, differences, path, subjectItems, expectationItems);
            return;
        }

        var counts = new Dictionary<object, int>();
        var nullBalance = 0;
        foreach (var item in subjectItems)
        {
            if (item is null) nullBalance++;
            else counts[item] = counts.TryGetValue(item, out var n) ? n + 1 : 1;
        }
        foreach (var item in expectationItems)
        {
            if (item is null)
            {
                if (nullBalance > 0) nullBalance--;
                else differences.Add(new(path, "expected collection to contain <null>, but no equivalent item was found"));
                continue;
            }
            if (counts.TryGetValue(item, out var n) && n > 0)
                counts[item] = n - 1;
            else
                differences.Add(new(path, $"expected collection to contain {Formatter.Format(item)}, but no equivalent item was found"));
        }

        var extras = new List<object?>();
        for (var i = 0; i < nullBalance; i++) extras.Add(null);
        foreach (var (item, remaining) in counts)
        {
            for (var i = 0; i < remaining; i++) extras.Add(item);
        }
        if (extras.Count > 0)
            differences.Add(new(path, $"found unexpected item(s) {Formatter.Format(extras)}"));
    }

    /// <summary>
    /// The multiset comparison without the hash shortcut, for when equality is the options' notion
    /// rather than <see cref="object.Equals(object?, object?)"/>.
    /// </summary>
    private static void CompareMultisetsPairwise(IEquivalencyOptions options, List<Difference> differences, string path,
        List<object?> subjectItems, List<object?> expectationItems)
    {
        var claimed = new bool[subjectItems.Count];
        foreach (var expected in expectationItems)
        {
            var matched = false;
            for (var s = 0; s < subjectItems.Count; s++)
            {
                if (claimed[s]) continue;
                var candidate = subjectItems[s];
                if (candidate is null || expected is null)
                {
                    if (candidate is null && expected is null) { claimed[s] = matched = true; break; }
                    if (options.TreatNullAsEmptyString && IsNullOrEmptyString(candidate) && IsNullOrEmptyString(expected))
                    {
                        claimed[s] = matched = true;
                        break;
                    }
                    continue;
                }
                if (ValuesEqual(options, candidate, expected)) { claimed[s] = matched = true; break; }
            }

            if (!matched)
            {
                differences.Add(new(path, expected is null
                    ? "expected collection to contain <null>, but no equivalent item was found"
                    : $"expected collection to contain {Formatter.Format(expected)}, but no equivalent item was found"));
            }
        }

        var extras = new List<object?>();
        for (var s = 0; s < subjectItems.Count; s++)
        {
            if (!claimed[s]) extras.Add(subjectItems[s]);
        }
        if (extras.Count > 0)
            differences.Add(new(path, $"found unexpected item(s) {Formatter.Format(extras)}"));
    }

    private static bool NeedsCustomValueEquality(IEquivalencyOptions options)
        => options.StringOptions != StringMatchOptions.None
            || options.EnumComparison != EnumComparison.ByValue
            || options.TreatNullAsEmptyString;

    /// <summary>Equality for a value-like pair, under the options' notion of equal.</summary>
    private static bool ValuesEqual(IEquivalencyOptions options, object subject, object expectation)
    {
        if (options.StringOptions != StringMatchOptions.None && subject is string subjectText && expectation is string expectedText)
        {
            return string.Equals(
                StringMatch.Normalize(subjectText, options.StringOptions),
                StringMatch.Normalize(expectedText, options.StringOptions),
                StringMatch.Comparison(options.StringOptions));
        }

        if (options.EnumComparison == EnumComparison.ByName && subject is Enum && expectation is Enum)
        {
            return string.Equals(subject.ToString(), expectation.ToString(), StringComparison.Ordinal);
        }

        return Equals(subject, expectation);
    }

    private static bool IsNullOrEmptyString(object? value)
        => value is null || (value is string text && text.Length == 0);

    private static bool AllItemsValueLike(IEquivalencyOptions options, List<object?> items)
    {
        foreach (var item in items)
        {
            if (item is not null && !IsValueLike(item.GetType(), options)) return false;
        }
        return true;
    }

    private static List<object?> Materialize(IEnumerable enumerable)
    {
        var items = new List<object?>();
        foreach (var item in enumerable) items.Add(item);
        return items;
    }

    private static bool IsExcluded(IEquivalencyOptions options, string path)
    {
        if (path.Length == 0) return false;
        if (options.ExcludedPaths.Contains(path)) return true;
        if (options.ExcludedWildcardPaths.Count == 0) return false;
        foreach (var pattern in options.ExcludedWildcardPaths)
        {
            if (WildcardPattern.IsMatch(path, pattern)) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a node at <paramref name="path"/> may be compared or descended into, given the
    /// <c>Including</c> patterns.
    /// </summary>
    /// <remarks>
    /// Three ways to qualify, and all three are needed: the path matches a pattern; the path sits
    /// <em>under</em> a matched pattern (<c>Including(x =&gt; x.Address)</c> must compare
    /// <c>Address.City</c>); or the path is a segment prefix of a pattern, so the walk can reach the
    /// leaf the caller actually named. The last one is what makes a nested <c>Including</c> mean the
    /// leaf rather than its whole parent — the bug this replaced, where inclusions were matched at
    /// the root only.
    /// </remarks>
    private static bool IsWithinInclusions(IEquivalencyOptions options, string path)
    {
        if (options.IncludedPaths.Count == 0 || path.Length == 0) return true;

        foreach (var pattern in options.IncludedPaths)
        {
            if (WildcardPattern.IsMatch(path, pattern)) return true;
            if (WildcardPattern.IsMatch(path, pattern + ".*")) return true;
            if (WildcardPattern.IsMatch(path, pattern + "[*")) return true;
            if (IsSegmentPrefixOf(path, pattern)) return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="path"/> matches some leading segment of <paramref name="pattern"/>.</summary>
    private static bool IsSegmentPrefixOf(string path, string pattern)
    {
        for (var i = 1; i < pattern.Length; i++)
        {
            if (pattern[i] is not ('.' or '[')) continue;
            if (WildcardPattern.IsMatch(path, pattern[..i])) return true;
        }
        return false;
    }

    /// <summary>Whether this member's kind takes part in the comparison at all.</summary>
    private static bool IsMemberVisible(IEquivalencyOptions options, MemberAccessor member)
    {
        var traits = member.Traits;
        var inclusion = options.Inclusion;

        if ((traits & MemberTraits.Field) != 0)
        {
            if ((inclusion & MemberInclusion.Fields) == 0) return false;
        }
        else if ((inclusion & MemberInclusion.Properties) == 0) return false;

        if ((traits & MemberTraits.NonPublic) != 0 && (inclusion & MemberInclusion.Internal) == 0) return false;
        if ((traits & MemberTraits.NonBrowsable) != 0 && (inclusion & MemberInclusion.NonBrowsable) == 0) return false;
        if ((traits & MemberTraits.ExplicitInterface) != 0 && (inclusion & MemberInclusion.ExplicitInterface) == 0) return false;

        return true;
    }

    private static string MapToSubjectName(IEquivalencyOptions options, string expectationName)
        => options.MemberNameMappings.Count > 0 && options.MemberNameMappings.TryGetValue(expectationName, out var mapped)
            ? mapped
            : expectationName;

    private static bool UseStrictOrderingAt(IEquivalencyOptions options, string path)
    {
        if (options.StrictOrderingPaths.Count > 0)
        {
            foreach (var pattern in options.StrictOrderingPaths)
            {
                if (WildcardPattern.IsMatch(path, pattern)) return true;
            }
        }
        if (options.LooseOrderingPaths.Count > 0)
        {
            foreach (var pattern in options.LooseOrderingPaths)
            {
                if (WildcardPattern.IsMatch(path, pattern)) return false;
            }
        }
        return options.UseStrictOrdering;
    }

    private static bool TryGetCustomComparer(IEquivalencyOptions options, Type type, out Action<object?, object?> comparer)
    {
        if (options.CustomComparers.Count == 0)
        {
            comparer = null!;
            return false;
        }
        if (options.CustomComparers.TryGetValue(type, out comparer!)) return true;
        var underlying = Nullable.GetUnderlyingType(type);
        return underlying is not null && options.CustomComparers.TryGetValue(underlying, out comparer!);
    }

    /// <summary>Member names excluded via ExcludingNested for a node of these types, or null when none apply.</summary>
    private static HashSet<string>? GetNestedExclusions(IEquivalencyOptions options, Type expectationType, Type subjectType)
    {
        if (options.NestedExclusions.Count == 0) return null;

        HashSet<string>? names = null;
        foreach (var (type, memberNames) in options.NestedExclusions)
        {
            if (!type.IsAssignableFrom(expectationType) && !type.IsAssignableFrom(subjectType)) continue;
            names ??= new(StringComparer.Ordinal);
            foreach (var name in memberNames) names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// The type whose members drive the comparison at a node: the runtime expectation type when
    /// runtime types are respected or no useful declared type is known, the declared type otherwise.
    /// </summary>
    private static Type ResolveNodeType(IEquivalencyOptions options, Type? declaredType, object expectation)
        => options.UseRuntimeTypes || declaredType is null || declaredType == typeof(object)
            ? expectation.GetType()
            : declaredType;

    /// <summary>
    /// Best-effort element type of a declared collection type: the array element type, or the
    /// single generic argument of a generic enumerable (List&lt;T&gt;, IEnumerable&lt;T&gt;, ...).
    /// Null when unknown; the item's runtime type is used instead.
    /// </summary>
    private static Type? GetElementType(Type? declaredType)
    {
        if (declaredType is null) return null;
        if (declaredType.IsArray) return declaredType.GetElementType();
        if (declaredType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(declaredType))
        {
            var arguments = declaredType.GetGenericArguments();
            if (arguments.Length == 1) return arguments[0];
        }
        return null;
    }

    private static bool IsValueLike(Type type, IEquivalencyOptions options)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (options.ComparedByMembers.Contains(type)) return false;
        if (options.ComparedByValue.Contains(type)) return true;
        return type.IsPrimitive || type.IsEnum
            || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan)
            || type == typeof(Guid)
            || typeof(Uri).IsAssignableFrom(type)
            || typeof(Type).IsAssignableFrom(type);
    }

    /// <summary>
    /// Whether a type is a C# <c>record</c>.
    /// </summary>
    /// <remarks>
    /// There is no metadata flag for it. The compiler emits a synthetic <c>&lt;Clone&gt;$</c> method
    /// on every record and on nothing else, which is the check every tool in the ecosystem uses.
    /// Cached, and only ever reached when <see cref="RecordComparison.ByValue"/> was asked for.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Only reached when ComparingRecordsByValue() was called. A trimmed-away clone method means the type is compared member by member instead, which is the default behaviour.")]
    private static bool IsRecord(Type type) => RecordCache.GetOrAdd(type,
        static t => t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null);

    private static readonly ConcurrentDictionary<Type, bool> RecordCache = new();

    /// <summary>
    /// Views a value as a key/value sequence, for the dictionary comparison.
    /// </summary>
    /// <remarks>
    /// <see cref="IDictionary"/> covers <c>Dictionary&lt;K,V&gt;</c> and most of the BCL. It does not
    /// cover a type that implements only <c>IReadOnlyDictionary&lt;K,V&gt;</c> — an immutable or
    /// frozen dictionary, or a hand-rolled read-only wrapper — which used to fall through to the
    /// collection path and be compared as an unordered bag of pairs. Same verdict most of the time,
    /// but the messages named pairs instead of keys, and a value difference on a matching key was
    /// reported as "no equivalent item was found".
    /// </remarks>
    private static bool TryGetPairs(object value, out List<KeyValuePair<object?, object?>> pairs)
    {
        if (value is IDictionary dictionary)
        {
            pairs = new(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary) pairs.Add(new(entry.Key, entry.Value));
            return true;
        }

        if (value is IEnumerable sequence and not string && IsGenericDictionary(value.GetType()))
        {
            pairs = [];
            foreach (var item in sequence)
            {
                if (item is null) continue;
                var accessors = RegistryMemberProvider.Instance.GetMembers(item.GetType());
                object? key = null, itemValue = null;
                for (var i = 0; i < accessors.Count; i++)
                {
                    if (accessors[i].Name == "Key") key = accessors[i].Getter(item);
                    else if (accessors[i].Name == "Value") itemValue = accessors[i].Getter(item);
                }
                pairs.Add(new(key, itemValue));
            }
            return true;
        }

        pairs = null!;
        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Interface list only; a trimmed-away IReadOnlyDictionary interface means the value is compared as a collection of pairs, which is the previous behaviour.")]
    private static bool IsGenericDictionary(Type type) => GenericDictionaryCache.GetOrAdd(type, static t =>
    {
        foreach (var contract in t.GetInterfaces())
        {
            if (!contract.IsGenericType) continue;
            var definition = contract.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(IDictionary<,>)) return true;
        }
        return false;
    });

    private static readonly ConcurrentDictionary<Type, bool> GenericDictionaryCache = new();

    private static MemberAccessor? FindByName(IEquivalencyOptions options, IReadOnlyList<MemberAccessor> members, string name)
    {
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (string.Equals(member.Name, name, StringComparison.Ordinal) && IsMemberVisible(options, member)) return member;
        }
        return null;
    }
}
