using System.Text;
using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions;

/// <summary>
/// Object-graph equivalency assertions: <c>BeEquivalentTo</c> compares subject and expectation
/// structurally (member by member, driven by the expectation's members) instead of via
/// <see cref="object.Equals(object?)"/>. Configure the comparison through the optional
/// <see cref="EquivalencyOptions{TExpectation}"/> lambda.
/// </summary>
/// <remarks>
/// Every method here rejects a <em>vacuous</em> comparison — one in which some node compares no
/// members, making the assertion incapable of failing — with an
/// <see cref="InvalidOperationException"/>. That is a caller mistake rather than an assertion
/// outcome, so it is thrown rather than reported as a failure: a failure in the negative direction
/// is exactly what would make <c>NotBeEquivalentTo</c> succeed, which would hide the mistake in
/// the very place it is easiest to make. Opt out with
/// <see cref="EquivalencyOptions{TExpectation}.AllowingVacuousComparison"/>.
/// </remarks>
public static class EquivalencyAssertionExtensions
{
    /// <summary>
    /// Asserts that the subject is structurally equivalent to <paramref name="expectation"/>:
    /// every member of the expectation must have an equal counterpart on the subject, recursively.
    /// Extra members on the subject are ignored, which makes comparing a full DTO against an
    /// anonymous object with only the interesting members work naturally.
    /// </summary>
    public static AndConstraint<Primitives.ObjectAssertions> BeEquivalentTo<TExpectation>(
        this Primitives.ObjectAssertions assertions,
        TExpectation expectation,
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config = null,
        string? because = null,
        params object?[] becauseArgs)
    {
        var result = Validate(assertions.Subject, expectation, config, typeof(TExpectation));
        if (result.Differences.Count > 0)
        {
            assertions.Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith(BuildFailureTemplate(result.Differences, assertions.SubjectExpression), expectation);
        }
        return new(assertions);
    }

    /// <summary>Asserts that the subject is <em>not</em> structurally equivalent to <paramref name="expectation"/>.</summary>
    public static AndConstraint<Primitives.ObjectAssertions> NotBeEquivalentTo<TExpectation>(
        this Primitives.ObjectAssertions assertions,
        TExpectation expectation,
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config = null,
        string? because = null,
        params object?[] becauseArgs)
    {
        var result = Validate(assertions.Subject, expectation, config, typeof(TExpectation));
        assertions.Assert().ForCondition(result.Differences.Count > 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be equivalent to {0}{reason}, but no differences were found.", expectation);
        return new(assertions);
    }

    /// <summary>
    /// Asserts that the subject collection is structurally equivalent to
    /// <paramref name="expectation"/>: same number of items, each expectation item matched by an
    /// equivalent subject item (unordered by default; use <c>WithStrictOrdering()</c> to compare
    /// pairwise in order).
    /// </summary>
    public static AndConstraint<Collections.GenericCollectionAssertions<T>> BeEquivalentTo<T, TExpectation>(
        this Collections.GenericCollectionAssertions<T> assertions,
        IEnumerable<TExpectation> expectation,
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config = null,
        string? because = null,
        params object?[] becauseArgs)
    {
        var result = Validate(assertions.Subject, expectation, config, typeof(IEnumerable<TExpectation>));
        if (result.Differences.Count > 0)
        {
            assertions.Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith(BuildFailureTemplate(result.Differences, assertions.SubjectExpression), expectation);
        }
        return new(assertions);
    }

    /// <summary>
    /// Asserts that the subject collection is <em>not</em> structurally equivalent to
    /// <paramref name="expectation"/>. The mirror of the collection <c>BeEquivalentTo</c> above:
    /// without it, the negative form is unreachable from a typed collection and the only way to
    /// express it is a cast to <c>object</c>, which costs the source-generated member accessors
    /// and makes the <paramref name="config"/> lambda untypeable.
    /// </summary>
    public static AndConstraint<Collections.GenericCollectionAssertions<T>> NotBeEquivalentTo<T, TExpectation>(
        this Collections.GenericCollectionAssertions<T> assertions,
        IEnumerable<TExpectation> expectation,
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config = null,
        string? because = null,
        params object?[] becauseArgs)
    {
        var result = Validate(assertions.Subject, expectation, config, typeof(IEnumerable<TExpectation>));
        assertions.Assert().ForCondition(result.Differences.Count > 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be equivalent to {0}{reason}, but no differences were found.", expectation);
        return new(assertions);
    }

    /// <summary>
    /// Asserts that the subject dictionary is structurally equivalent to
    /// <paramref name="expectation"/>: every expected key present with an equivalent value, and no
    /// unexpected keys. Values are compared with the full object-graph comparison, so a dictionary
    /// of DTOs can be compared against a dictionary of anonymous objects holding only the
    /// interesting members.
    /// </summary>
    /// <remarks>
    /// When both sides are real dictionaries the comparison is key-based and reports missing and
    /// unexpected keys by name. A sequence of key/value pairs that is not a dictionary has no key
    /// lookup, so it is compared as a collection of pairs instead — same verdict, pair-shaped
    /// messages.
    /// </remarks>
    public static AndConstraint<Collections.GenericDictionaryAssertions<TKey, TValue>> BeEquivalentTo<TKey, TValue, TExpectation>(
        this Collections.GenericDictionaryAssertions<TKey, TValue> assertions,
        IEnumerable<KeyValuePair<TKey, TExpectation>> expectation,
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config = null,
        string? because = null,
        params object?[] becauseArgs)
    {
        var result = Validate(assertions.Subject, expectation, config, typeof(IEnumerable<KeyValuePair<TKey, TExpectation>>));
        if (result.Differences.Count > 0)
        {
            assertions.Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith(BuildFailureTemplate(result.Differences, assertions.SubjectExpression), expectation);
        }
        return new(assertions);
    }

    /// <summary>
    /// Asserts that the subject dictionary is <em>not</em> structurally equivalent to
    /// <paramref name="expectation"/>.
    /// </summary>
    public static AndConstraint<Collections.GenericDictionaryAssertions<TKey, TValue>> NotBeEquivalentTo<TKey, TValue, TExpectation>(
        this Collections.GenericDictionaryAssertions<TKey, TValue> assertions,
        IEnumerable<KeyValuePair<TKey, TExpectation>> expectation,
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config = null,
        string? because = null,
        params object?[] becauseArgs)
    {
        var result = Validate(assertions.Subject, expectation, config, typeof(IEnumerable<KeyValuePair<TKey, TExpectation>>));
        assertions.Assert().ForCondition(result.Differences.Count > 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be equivalent to {0}{reason}, but no differences were found.", expectation);
        return new(assertions);
    }

    /// <summary>
    /// Runs the comparison and rejects a vacuous one, so every assertion method above shares the
    /// same guard by construction instead of repeating the condition.
    /// </summary>
    private static ValidationResult Validate<TExpectation>(
        object? subject, object? expectation,
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config,
        Type rootDeclaredType)
    {
        var options = BuildOptions(config);
        var result = EquivalencyValidator.Validate(subject, expectation, options, rootDeclaredType);
        if (result.Vacuity is { } vacuity && !((IEquivalencyOptions)options).AllowVacuousComparison)
            throw new InvalidOperationException(BuildVacuityMessage(vacuity));
        return result;
    }

    private static EquivalencyOptions<TExpectation> BuildOptions<TExpectation>(
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config)
    {
        var options = new EquivalencyOptions<TExpectation>();
        return config is null ? options : config(options);
    }

    /// <summary>
    /// Explains a vacuous comparison and names both types, because the cure depends on which of
    /// the two causes it was: a memberless expectation type, or options that removed every member.
    /// </summary>
    private static string BuildVacuityMessage(VacuousNode vacuity)
    {
        var where = vacuity.Path.Length == 0 ? "this assertion" : $"the comparison at '{vacuity.Path}'";
        var cause = vacuity.ExpectationHasNoMembers
            ? $"the expectation type '{vacuity.ExpectationType.Name}' exposes no public properties or fields, "
                + $"while the subject '{vacuity.SubjectType.Name}' has "
                + $"{CountMembers(vacuity.SubjectType)}. Compare against a concrete type, or against an "
                + "anonymous object listing the members you care about"
            : $"every member of '{vacuity.ExpectationType.Name}' was removed by the configured "
                + "exclusions or inclusions. Keep at least one member in the comparison";

        return $"No members were compared, so {where} can never fail: {cause} — "
            + "or call AllowingVacuousComparison() if comparing nothing is intended.";
    }

    private static int CountMembers(Type type)
        => RegistryMemberProvider.Instance.GetMembers(type).Count;

    /// <summary>
    /// Builds the failure template with the difference block pre-rendered into it. The block is
    /// raw text (paths and already-formatted values), so it is embedded in the template with '{'
    /// escaped rather than passed through a Formatter placeholder; only the expectation itself is
    /// left as the {0} argument.
    /// </summary>
    private static string BuildFailureTemplate(List<Difference> differences, string subjectExpression)
    {
        var sb = new StringBuilder("Expected {subject} to be equivalent to {0}{reason}, but found the following difference(s):");
        foreach (var difference in differences)
        {
            sb.Append(Environment.NewLine).Append("  - ")
              .Append(EscapeBraces(difference.Path.Length == 0 ? subjectExpression : difference.Path))
              .Append(": ")
              .Append(EscapeBraces(difference.Message));
        }
        return sb.ToString();
    }

    private static string EscapeBraces(string text) => text.Replace("{", "{{");
}
