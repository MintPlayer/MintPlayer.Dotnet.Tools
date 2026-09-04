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
/// <item>Unordered collection matching is greedy: each expectation item claims the first
/// unmatched subject item it is fully equivalent to. When every item on both sides is
/// value-like, a hash-based multiset comparison is used instead of the O(n²) matching.</item>
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
            if (!Equals(subject, expectation))
                differences.Add(new(path, $"expected {Formatter.Format(expectation)}, but found {Formatter.Format(subject)}"));
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
                    CompareDictionaries(context, differences, path, subjectDictionary, expectationDictionary, depth);
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
        var expectationType = ResolveNodeType(context.Options, declaredType, expectation);
        var expectationMembers = context.MemberProvider.GetMembers(expectationType);
        var subjectMembers = context.MemberProvider.GetMembers(subject.GetType());
        var excludedNames = GetNestedExclusions(context.Options, expectationType, subject.GetType());

        // Counts the members that actually took part in the comparison. Zero of them means this
        // node asserted nothing and therefore cannot fail — see the ValidationResult docs.
        var comparedMembers = 0;

        foreach (var expectationMember in expectationMembers)
        {
            if (excludedNames is not null && excludedNames.Contains(expectationMember.Name)) continue;
            if (path.Length == 0 && context.Options.IncludedMembers.Count > 0
                && !context.Options.IncludedMembers.Contains(expectationMember.Name)) continue;

            var childPath = path.Length == 0 ? expectationMember.Name : $"{path}.{expectationMember.Name}";

            var subjectMember = FindByName(subjectMembers, expectationMember.Name);
            if (subjectMember is null)
            {
                if (!IsExcluded(context.Options, childPath))
                {
                    differences.Add(new(childPath, $"expectation has member {expectationMember.Name} but subject does not"));
                    comparedMembers++;
                }
                continue;
            }

            if (IsExcluded(context.Options, childPath)) continue;
            comparedMembers++;

            CompareNode(context, differences, childPath,
                subjectMember.Getter(subject), expectationMember.Getter(expectation),
                expectationMember.Type, depth + 1);
        }

        // A structural node that compared nothing can never fail. Two memberless values really
        // are equivalent, so the subject must have members for this to count as vacuous at all.
        if (comparedMembers > 0 || subjectMembers.Count == 0) return;

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
        if (expectationMembers.Count == 0 || path.Length == 0)
        {
            context.ReportVacuous(new(path, expectationType, subject.GetType(),
                ExpectationHasNoMembers: expectationMembers.Count == 0));
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

    private static void CompareCollections(Context context, List<Difference> differences, string path,
        IEnumerable subject, IEnumerable expectation, Type? declaredType, int depth)
    {
        var subjectItems = Materialize(subject);
        var expectationItems = Materialize(expectation);
        var itemDeclaredType = GetElementType(declaredType);

        if (subjectItems.Count != expectationItems.Count)
            differences.Add(new(path, $"expected {expectationItems.Count} item(s), but found {subjectItems.Count}"));

        if (context.Options.UseStrictOrdering)
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
            CompareMultisets(differences, path, subjectItems, expectationItems);
            return;
        }

        // Greedy bipartite matching: each expectation item claims the first unmatched subject
        // item it is fully equivalent to (trial comparison into a throwaway collector).
        var matched = new bool[subjectItems.Count];
        foreach (var expectationItem in expectationItems)
        {
            var found = false;
            for (var i = 0; i < subjectItems.Count; i++)
            {
                if (matched[i]) continue;
                var trial = new List<Difference>();
                CompareNode(context, trial, $"{path}[?]", subjectItems[i], expectationItem, itemDeclaredType, depth + 1);
                if (trial.Count == 0)
                {
                    matched[i] = true;
                    found = true;
                    break;
                }
            }
            if (!found)
                differences.Add(new(path, $"expected collection to contain {Formatter.Format(expectationItem)}, but no equivalent item was found"));
        }

        var extras = new List<object?>();
        for (var i = 0; i < subjectItems.Count; i++)
        {
            if (!matched[i]) extras.Add(subjectItems[i]);
        }
        if (extras.Count > 0)
            differences.Add(new(path, $"found unexpected item(s) {Formatter.Format(extras)}"));
    }

    /// <summary>Hash-based multiset comparison for collections of value-like items (avoids O(n²)).</summary>
    private static void CompareMultisets(List<Difference> differences, string path,
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
        foreach (var pattern in options.ExcludedWildcardPaths)
        {
            if (WildcardPattern.IsMatch(path, pattern)) return true;
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

    private static MemberAccessor? FindByName(IReadOnlyList<MemberAccessor> members, string name)
    {
        foreach (var member in members)
        {
            if (string.Equals(member.Name, name, StringComparison.Ordinal)) return member;
        }
        return null;
    }
}
