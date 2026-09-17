using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Formatting;

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
/// <item>Cycles are safe: a (subject, expectation) reference pair already on the current descent
/// stack is treated as equal instead of recursing.</item>
/// <item>Nodes deeper than <see cref="IEquivalencyOptions.MaxDepth"/> are silently treated as
/// equal; no difference and no warning is produced for them.</item>
/// <item>Unordered collection matching finds a MAXIMUM matching: a greedy first-fit pre-pass
/// handles the aligned case in ~n comparisons, and anything it strands is resolved by augmenting
/// paths, so two equivalent collections never report a difference because greedy chose badly.
/// When every item on both sides is value-like, a hash-based multiset comparison is used instead
/// of the O(n²) matching.</item>
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
        CompareNode(context, differences, PathSegment.None, subject, expectation, rootDeclaredType, 0);
        return new(differences, context.Vacuity);
    }

    /// <summary>Per-validation state: the options, the cycle-detection descent stack and the first vacuous node.</summary>
    private sealed class Context(IEquivalencyOptions options)
    {
        public IEquivalencyOptions Options { get; } = options;
        public IMemberProvider MemberProvider { get; } = RegistryMemberProvider.Instance;

        /// <summary>
        /// Which members this comparison wants, read once instead of per node.
        /// </summary>
        /// <remarks>
        /// ⚠️ Read from the options HERE and nowhere else, and note that the FILTERING happens in the
        /// provider, not in <c>CompareMembers</c>. For essentially every comparison this is
        /// <see cref="MemberSelection.Default"/> and the provider hands back the very same array it
        /// did before selections existed — no filtering, and no extra members for <c>FindByName</c>
        /// (which is O(members²) per node) to scan past. When it is not the default, the filtered
        /// array is built once per type and cached. Moving any of this into <c>CompareMembers</c>
        /// would put it on the hottest loop in the library to serve options that are off by default.
        /// </remarks>
        public MemberSelection MemberSelection { get; } = new(options.IncludedMemberTraits, options.ExcludedMemberKinds);

        /// <summary>
        /// Where the walk currently is. Lives on the context rather than being threaded as a string
        /// parameter because building that string per member was 49% of the whole comparison — see
        /// <see cref="PathStack"/>.
        /// </summary>
        public PathStack Path { get; } = new();

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

    private static void CompareNode(Context context, List<Difference> differences, in PathSegment segment,
        object? subject, object? expectation, Type? declaredType, int depth)
    {
        if (EquivalencyDiagnostics.Enabled) EquivalencyDiagnostics.Nodes++;

        // ⚠️ Every return below this point must stay inside the try, or the path stack unbalances —
        // and the symptom is not a crash, it is silently wrong paths in later failure messages.
        context.Path.Push(segment);
        try
        {
            CompareNodeCore(context, differences, subject, expectation, declaredType, depth);
        }
        finally
        {
            context.Path.Pop();
        }
    }

    private static void CompareNodeCore(Context context, List<Difference> differences,
        object? subject, object? expectation, Type? declaredType, int depth)
    {
        if (IsExcluded(context.Options, context.Path)) return;

        var comparerType = declaredType ?? expectation?.GetType() ?? subject?.GetType();
        if (comparerType is not null && TryGetCustomComparer(context.Options, comparerType, out var comparer))
        {
            try
            {
                comparer(subject, expectation);
            }
            catch (AssertionFailedException ex)
            {
                differences.Add(new(context.Path.ToPathString(), ex.Message));
            }
            return;
        }

        if (subject is null && expectation is null) return;
        if (expectation is null)
        {
            differences.Add(new(context.Path.ToPathString(), $"expected <null>, but found {Formatter.Format(subject)}"));
            return;
        }
        if (subject is null)
        {
            differences.Add(new(context.Path.ToPathString(), $"expected {Formatter.Format(expectation)}, but found <null>"));
            return;
        }

        if (IsValueLike(expectation.GetType(), context.Options))
        {
            if (!Equals(subject, expectation))
                differences.Add(new(context.Path.ToPathString(), $"expected {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
            return;
        }

        // Depth limit: silently treat deeper structural nodes as equal (documented behavior).
        if (depth > context.Options.MaxDepth) return;

        // Cycle guard: a pair already on the descent stack is being compared higher up; treat it
        // as equal here to terminate the recursion.
        if (!context.TryPush(subject, expectation)) return;
        try
        {
            if (expectation is IDictionary expectationDictionary)
            {
                if (subject is IDictionary subjectDictionary)
                    CompareDictionaries(context, differences, subjectDictionary, expectationDictionary, depth);
                else
                    differences.Add(new(context.Path.ToPathString(), $"expected a dictionary {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
            }
            else if (expectation is IEnumerable expectationEnumerable and not string)
            {
                if (subject is IEnumerable subjectEnumerable and not string)
                    CompareCollections(context, differences, subjectEnumerable, expectationEnumerable, declaredType, depth);
                else
                    differences.Add(new(context.Path.ToPathString(), $"expected a collection {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
            }
            else
            {
                CompareMembers(context, differences, subject, expectation, declaredType, depth);
            }
        }
        finally
        {
            context.Pop(subject, expectation);
        }
    }

    private static void CompareMembers(Context context, List<Difference> differences,
        object subject, object expectation, Type? declaredType, int depth)
    {
        var expectationType = ResolveNodeType(context.Options, declaredType, expectation);

        // Both are MemberAccessor[], and `var` is load-bearing here: widening either to
        // IReadOnlyList<MemberAccessor> puts a boxed enumerator on the foreach below, once per
        // structural node. See IMemberProvider.GetMembers.
        var expectationMembers = context.MemberProvider.GetMembers(expectationType, context.MemberSelection);
        var subjectMembers = context.MemberProvider.GetMembers(subject.GetType(), context.MemberSelection);
        var excludedNames = GetNestedExclusions(context.Options, expectationType, subject.GetType());

        // Counts the members that actually took part in the comparison. Zero of them means this
        // node asserted nothing and therefore cannot fail — see the ValidationResult docs.
        var comparedMembers = 0;

        foreach (var expectationMember in expectationMembers)
        {
            if (excludedNames is not null && excludedNames.Contains(expectationMember.Name)) continue;
            if (context.Path.IsRoot && context.Options.IncludedMembers.Count > 0
                && !context.Options.IncludedMembers.Contains(expectationMember.Name)) continue;

            var childSegment = PathSegment.ForMember(expectationMember.Name);

            var subjectMember = FindByName(subjectMembers, expectationMember.Name);
            if (subjectMember is null)
            {
                if (!IsExcluded(context.Options, context.Path, childSegment))
                {
                    differences.Add(new(context.Path.ToPathStringWith(childSegment), $"expectation has member {expectationMember.Name} but subject does not"));
                    comparedMembers++;
                }
                continue;
            }

            if (IsExcluded(context.Options, context.Path, childSegment)) continue;
            comparedMembers++;

            CompareNode(context, differences, childSegment,
                subjectMember.Getter(subject), expectationMember.Getter(expectation),
                expectationMember.Type, depth + 1);
        }

        // A structural node that compared nothing can never fail. Two memberless values really
        // are equivalent, so the subject must have members for this to count as vacuous at all.
        //
        // ⚠️ "Has members" means the type's OWN members, not the ones this selection left. Once
        // ExcludingFields/ExcludingProperties existed, a node whose members had all been removed by
        // options looked identical to a genuinely memberless value — both tables empty — and the
        // assertion went green instead of reporting the vacuity. Excluding both kinds is exactly
        // that case, and it is the one this check exists for. The unfiltered lookup is cached and
        // only ever runs when nothing was compared, so it costs a normal comparison nothing.
        if (comparedMembers > 0) return;
        if (subjectMembers.Length == 0
            && (context.MemberSelection.IsDefault
                || context.MemberProvider.GetMembers(subject.GetType(), MemberSelection.Default).Length == 0))
        {
            return;
        }

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
        if (expectationMembers.Length == 0 || context.Path.IsRoot)
        {
            context.ReportVacuous(new(context.Path.ToPathString(), expectationType, subject.GetType(),
                ExpectationHasNoMembers: expectationMembers.Length == 0));
        }
    }

    private static void CompareDictionaries(Context context, List<Difference> differences,
        IDictionary subject, IDictionary expectation, int depth)
    {
        foreach (var key in expectation.Keys)
        {
            var keyText = Convert.ToString(key, CultureInfo.InvariantCulture);
            var childSegment = PathSegment.ForIndex(keyText);
            if (!subject.Contains(key!))
            {
                if (!IsExcluded(context.Options, context.Path, childSegment))
                    differences.Add(new(context.Path.ToPathString(), $"expected dictionary to contain key {Formatter.Format(key)}, but it was not found"));
                continue;
            }
            CompareNode(context, differences, childSegment, subject[key!], expectation[key!], null, depth + 1);
        }

        var extraKeys = new List<object?>();
        foreach (var key in subject.Keys)
        {
            if (!expectation.Contains(key!)) extraKeys.Add(key);
        }
        if (extraKeys.Count > 0)
            differences.Add(new(context.Path.ToPathString(), $"found unexpected key(s) {Formatter.Format(extraKeys)}"));
    }

    /// <summary>
    /// The placeholder segment a trial comparison runs under. Unordered matching does not know which
    /// index an item will end up at — that is what it is working out — so a probe's path reads
    /// <c>Lines[?]</c>. A single shared value because every probe uses the same one.
    /// </summary>
    private static readonly PathSegment ProbeSegment = PathSegment.ForIndex("?");

    private static void CompareCollections(Context context, List<Difference> differences,
        IEnumerable subject, IEnumerable expectation, Type? declaredType, int depth)
    {
        var subjectItems = Materialize(subject);
        var expectationItems = Materialize(expectation);
        var itemDeclaredType = GetElementType(declaredType);

        if (subjectItems.Count != expectationItems.Count)
            differences.Add(new(context.Path.ToPathString(), $"expected {expectationItems.Count} item(s), but found {subjectItems.Count}"));

        if (context.Options.UseStrictOrdering)
        {
            var count = Math.Min(subjectItems.Count, expectationItems.Count);
            for (var i = 0; i < count; i++)
            {
                CompareNode(context, differences, PathSegment.ForIndex(i.ToString()), subjectItems[i], expectationItems[i], itemDeclaredType, depth + 1);
            }
            return;
        }

        if (AllItemsValueLike(context.Options, subjectItems) && AllItemsValueLike(context.Options, expectationItems))
        {
            CompareMultisets(differences, context.Path, subjectItems, expectationItems);
            return;
        }

        // Unordered matching is a maximum bipartite matching problem: expectations on one side,
        // subject items on the other, an edge wherever a full subtree comparison finds no
        // difference. It is solved in two phases, and:
        //
        // ⚠️ THE TWO PHASES ARE NOT A STYLE CHOICE. DO NOT COLLAPSE THEM INTO ONE ALGORITHM.
        //
        // Phase 1 below is greedy first-fit. It is WRONG on its own — that was the shipped bug this
        // replaced. Expectations [A, B] against subjects [X, Y], where A is equivalent to both and B
        // only to X: greedy hands X to A, strands B, and reports a difference between two genuinely
        // equivalent collections. The perfect matching A–Y, B–X exists and greedy cannot find it.
        //
        // Phase 1 is kept anyway because it is the ONLY phase that runs for a collection that
        // already lines up, which is nearly every collection in a passing test: ~n comparisons, and
        // the bool[] below is its whole allocation. The correct algorithm — augmenting paths over a
        // candidate grid — is reached only when phase 1 strands something, in MaximumMatcher.
        //
        // The obvious "clean" rewrite is to drop phase 1 and always build the candidate grid. That
        // version shipped once: it costs n×m FULL SUBTREE comparisons unconditionally and measured
        // 1,027,288 B/op against 13,824 for the same graph, a 75× regression. Phase 1 exists to
        // keep that cost off the passing path; phase 2 exists so the answer is right.
        //
        // EquivalencyWalkerGateTests.AnAlignedCollectionCostsOneProbePerItem pins the probe count at
        // exactly 20 for the benchmark graph. If a change makes phase 2 run for an aligned
        // collection, that test fails immediately rather than the cost showing up in a benchmark
        // nobody runs. That gate was written before this fix, on purpose.
        var matched = new bool[subjectItems.Count];
        var allPlaced = true;
        for (var e = 0; e < expectationItems.Count && allPlaced; e++)
        {
            allPlaced = false;
            for (var i = 0; i < subjectItems.Count; i++)
            {
                if (matched[i]) continue;
                if (EquivalencyDiagnostics.Enabled) EquivalencyDiagnostics.MatchProbes++;
                var trial = new List<Difference>();
                CompareNode(context, trial, ProbeSegment, subjectItems[i], expectationItems[e], itemDeclaredType, depth + 1);
                if (trial.Count != 0) continue;
                matched[i] = true;
                allPlaced = true;
                break;
            }
        }

        if (!allPlaced)
        {
            // Greedy stranded an expectation. That means EITHER the collections genuinely differ, OR
            // greedy picked badly and a perfect matching still exists — and the two are
            // indistinguishable without running the real algorithm. Phase 1's assignments are
            // deliberately NOT carried over: reconstructing them would cost an int[] per collection
            // node on the passing path to serve a branch that passing tests never take.
            new MaximumMatcher(context, context.Path.ToPathString(), subjectItems, expectationItems, itemDeclaredType, depth)
                .MatchAndReport(differences);
            return;
        }

        // Lazy: a collection with no extra items is the passing case, and it must not allocate.
        List<object?>? extras = null;
        for (var i = 0; i < subjectItems.Count; i++)
        {
            if (!matched[i]) (extras ??= []).Add(subjectItems[i]);
        }
        if (extras is not null)
            differences.Add(new(context.Path.ToPathString(), $"found unexpected item(s) {Formatter.Format(extras)}"));
    }

    /// <summary>
    /// Maximum bipartite matching between expectation items and subject items by Kuhn's augmenting
    /// paths, reached only when the greedy pre-pass in <see cref="CompareCollections"/> stranded an
    /// expectation. Reports the expectations that genuinely cannot be matched and the subject items
    /// left over.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Everything here is on the failure-ish path, and that is what pays for it.</b> This type
    /// allocates a memo grid, two index arrays and an object of its own. None of that is acceptable
    /// per-node cost on the passing path, and none of it is ever reached from one — see the comment
    /// in <see cref="CompareCollections"/> for why the greedy phase is not redundant.
    /// <para>
    /// ⚠️ <b><see cref="AreEquivalent"/>'s memo is load-bearing, not a micro-optimisation.</b> An
    /// edge test is a FULL RECURSIVE SUBTREE COMPARISON, and augmenting paths revisit the same
    /// (expectation, subject) pair many times over. Without the memo the work is unbounded in the
    /// number of augmenting attempts instead of bounded at n×m; with it, every pair is compared at
    /// most once, which is also exactly the worst case the greedy loop already had. Do not "simplify"
    /// it away because the grid looks like a cache nobody needs.
    /// </para>
    /// <para>
    /// ⚠️ <b>Comparisons already made in phase 1 are intentionally repeated here.</b> Threading them
    /// through would mean materialising phase 1's results for every collection node, including the
    /// overwhelming majority that never reach this class. Paying n×m once on a path that is about to
    /// produce a failure message is the cheaper trade.
    /// </para>
    /// </remarks>
    private sealed class MaximumMatcher(Context context, string pathText, List<object?> subjectItems,
        List<object?> expectationItems, Type? itemDeclaredType, int depth)
    {
        /// <summary>0 = not compared yet, 1 = equivalent, -1 = not equivalent. Indexed [e * n + i].</summary>
        private readonly sbyte[] memo = new sbyte[expectationItems.Count * subjectItems.Count];
        private readonly int[] ownerOfSubject = FilledWithMinusOne(subjectItems.Count);
        private readonly int[] matchOfExpectation = FilledWithMinusOne(expectationItems.Count);
        private bool[]? visited;

        public void MatchAndReport(List<Difference> differences)
        {
            // Greedy again, now memoised — it settles the easy majority of the pairs cheaply and
            // leaves augmenting paths with only the contested ones to resolve.
            for (var e = 0; e < expectationItems.Count; e++)
            {
                for (var i = 0; i < subjectItems.Count; i++)
                {
                    if (ownerOfSubject[i] >= 0 || !AreEquivalent(e, i)) continue;
                    ownerOfSubject[i] = e;
                    matchOfExpectation[e] = i;
                    break;
                }
            }

            foreach (var e in Unplaced())
            {
                if (visited is null) visited = new bool[subjectItems.Count];
                else Array.Clear(visited);
                TryAugment(e);
            }

            for (var e = 0; e < expectationItems.Count; e++)
            {
                if (matchOfExpectation[e] < 0)
                    differences.Add(new(pathText, $"expected collection to contain {Formatter.Format(expectationItems[e])}, but no equivalent item was found"));
            }

            List<object?>? extras = null;
            for (var i = 0; i < subjectItems.Count; i++)
            {
                if (ownerOfSubject[i] < 0) (extras ??= []).Add(subjectItems[i]);
            }
            if (extras is not null)
                differences.Add(new(pathText, $"found unexpected item(s) {Formatter.Format(extras)}"));
        }

        /// <summary>Expectation indices greedy could not place, snapshotted before any augmenting runs.</summary>
        private List<int> Unplaced()
        {
            var unplaced = new List<int>();
            for (var e = 0; e < expectationItems.Count; e++)
            {
                if (matchOfExpectation[e] < 0) unplaced.Add(e);
            }
            return unplaced;
        }

        /// <summary>
        /// Kuhn's augmenting path: try to give expectation <paramref name="e"/> a subject item,
        /// displacing an incumbent only if that incumbent can be re-placed elsewhere.
        /// </summary>
        private bool TryAugment(int e)
        {
            for (var i = 0; i < subjectItems.Count; i++)
            {
                if (visited![i] || !AreEquivalent(e, i)) continue;
                visited[i] = true;
                var incumbent = ownerOfSubject[i];
                if (incumbent >= 0 && !TryAugment(incumbent)) continue;
                ownerOfSubject[i] = e;
                matchOfExpectation[e] = i;
                return true;
            }
            return false;
        }

        private bool AreEquivalent(int e, int i)
        {
            ref var cached = ref memo[(e * subjectItems.Count) + i];
            if (cached != 0) return cached > 0;

            if (EquivalencyDiagnostics.Enabled) EquivalencyDiagnostics.MatchProbes++;
            var trial = new List<Difference>();
            CompareNode(context, trial, ProbeSegment, subjectItems[i], expectationItems[e], itemDeclaredType, depth + 1);
            cached = (sbyte)(trial.Count == 0 ? 1 : -1);
            return cached > 0;
        }

        private static int[] FilledWithMinusOne(int length)
        {
            var values = new int[length];
            values.AsSpan().Fill(-1);
            return values;
        }
    }

    /// <summary>Hash-based multiset comparison for collections of value-like items (avoids O(n²)).</summary>
    /// <remarks>
    /// ⚠️ <b>The highest-risk method in this file for a future change, and the risk is not obvious.</b>
    /// <para>
    /// This is the O(n) path, taken when every item on both sides is value-like. It works by hashing,
    /// which means it depends on <see cref="object.Equals(object?)"/> and
    /// <see cref="object.GetHashCode"/> being the definition of "equal".
    /// </para>
    /// <para>
    /// The moment any option changes what equal MEANS for a value — a custom comparer, a
    /// case-insensitive string mode, a floating-point tolerance, <c>ComparingByMembers</c> on a type
    /// that reaches here — hashing stops being valid, and the obvious implementation is to fall back
    /// to pairwise matching. That is a silent O(n) → O(n²) cliff on collections of primitives, which
    /// are the most common collections in real test suites. Every one of those options is a
    /// legitimate feature request, so this will come up.
    /// </para>
    /// <para>
    /// If it does: guard the shortcut on "no option changes value equality", keep this path for the
    /// default case, and add an allocation/operation-count test over a LARGE collection of value-like
    /// items before merging. The gate's existing graph has 20 items, which is too few for a quadratic
    /// blow-up to be obvious in the numbers.
    /// </para>
    /// </remarks>
    private static void CompareMultisets(List<Difference> differences, PathStack path,
        List<object?> subjectItems, List<object?> expectationItems)
    {
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
                else differences.Add(new(path.ToPathString(), "expected collection to contain <null>, but no equivalent item was found"));
                continue;
            }
            if (counts.TryGetValue(item, out var n) && n > 0)
                counts[item] = n - 1;
            else
                differences.Add(new(path.ToPathString(), $"expected collection to contain {Formatter.Format(item)}, but no equivalent item was found"));
        }

        var extras = new List<object?>();
        for (var i = 0; i < nullBalance; i++) extras.Add(null);
        foreach (var (item, remaining) in counts)
        {
            for (var i = 0; i < remaining; i++) extras.Add(item);
        }
        if (extras.Count > 0)
            differences.Add(new(path.ToPathString(), $"found unexpected item(s) {Formatter.Format(extras)}"));
    }

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

    /// <summary>Whether this path was excluded by an option.</summary>
    /// <remarks>
    /// Called roughly twice per member per node, so it is on the hottest path in the walker and
    /// every line here is paid for by comparisons that configure nothing.
    /// <para>
    /// ⚠️ The <c>Count == 0</c> guard is load-bearing, not defensive. <c>ExcludedWildcardPaths</c> is
    /// an <c>IReadOnlyCollection&lt;string&gt;</c>, so the <c>foreach</c> below boxes a
    /// <c>HashSet&lt;string&gt;.Enumerator</c> — and without the guard it did so on every call even
    /// though almost no comparison configures a wildcard exclusion. The guard skips the loop, and
    /// the box with it, for the overwhelmingly common case. MPA0005 still reports the loop; that is
    /// correct and deliberate — it is only unreachable when the set is empty.
    /// </para>
    /// <para>
    /// <c>Contains</c> above does NOT box: <c>Enumerable.Contains</c> has an
    /// <c>ICollection&lt;T&gt;</c> fast path, so it stays O(1) and allocation-free despite the
    /// interface type. Only the <c>foreach</c> is the problem.
    /// </para>
    /// </remarks>
    private static bool IsExcluded(IEquivalencyOptions options, PathStack path)
        => IsExcluded(options, path, PathSegment.None);

    /// <summary>
    /// Whether the current position — optionally plus one more segment — is excluded.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The configuration checks come FIRST, before the path is rendered, and that order is the
    /// optimisation rather than the style.</b> This runs twice per member on every node of every
    /// comparison, and the overwhelming majority of comparisons configure no exclusions at all.
    /// Rendering the text up front would hand straight back the 49% that <see cref="PathStack"/>
    /// exists to save.
    /// </remarks>
    private static bool IsExcluded(IEquivalencyOptions options, PathStack path, in PathSegment extra)
    {
        if (options.ExcludedPaths.Count == 0 && options.ExcludedWildcardPaths.Count == 0) return false;
        if (path.IsRoot && extra.IsNone) return false;

        var text = path.ToPathStringWith(extra);
        if (text.Length == 0) return false;
        if (options.ExcludedPaths.Contains(text)) return true;
        if (options.ExcludedWildcardPaths.Count == 0) return false;

        foreach (var pattern in options.ExcludedWildcardPaths)
        {
            if (WildcardPattern.IsMatch(text, pattern)) return true;
        }
        return false;
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
    /// <remarks>
    /// ⚠️ Correctly guarded by <c>Count == 0</c>, so it costs nothing when nobody calls
    /// <c>ExcludingNested</c> — which is the common case and the only reason this is acceptable today.
    /// <para>
    /// When it IS configured, it allocates a fresh <see cref="HashSet{T}"/> <b>per structural node</b>
    /// and runs an <see cref="Type.IsAssignableFrom"/> scan over every registered exclusion. Memoise
    /// per (expectationType, subjectType) pair if that ever matters, or if <c>ExcludingNested</c>
    /// grows to accept interfaces, open generics or predicates — any of which turns this into a
    /// per-node type-hierarchy walk. Unmeasured: no gate covers the configured path.
    /// </para>
    /// </remarks>
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

    /// <summary>The subject member matching <paramref name="name"/>, or null.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><paramref name="members"/> must stay <c>MemberAccessor[]</c>.</b> This is the single
    /// hottest loop in the walker — once per expectation member per node, 112 times on the benchmark
    /// graph — so the boxed enumerator an interface-typed parameter produces cost 27% of the entire
    /// comparison's allocation. See <see cref="IMemberProvider.GetMembers"/> for the numbers.
    /// </para>
    /// <para>
    /// Known and deliberate: this is a linear scan, so member matching is O(members²) per node.
    /// That is invisible while member lists are short, and it is the first thing that will bite if a
    /// feature ever widens the member set — inherited members, explicit interface members, private
    /// members behind an option, or name mapping. The structural fix is a name→accessor dictionary
    /// built once per type beside the accessor array. <c>EquivalencyWalkerGateTests</c> asserts the
    /// exact lookup count, so that regression shows up as a failing number rather than a slow suite.
    /// </para>
    /// </remarks>
    private static MemberAccessor? FindByName(MemberAccessor[] members, string name)
    {
        if (EquivalencyDiagnostics.Enabled) EquivalencyDiagnostics.MemberLookups++;

        // foreach over an ARRAY: the compiler emits an indexed loop with no enumerator. The same
        // loop over IReadOnlyList<T> allocates one boxed enumerator per call and reads identically.
        foreach (var member in members)
        {
            if (string.Equals(member.Name, name, StringComparison.Ordinal)) return member;
        }
        return null;
    }
}
