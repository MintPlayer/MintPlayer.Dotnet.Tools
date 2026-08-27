using System.Text;
using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions;

/// <summary>
/// Object-graph equivalency assertions: <c>BeEquivalentTo</c> compares subject and expectation
/// structurally (member by member, driven by the expectation's members) instead of via
/// <see cref="object.Equals(object?)"/>. Configure the comparison through the optional
/// <see cref="EquivalencyOptions{TExpectation}"/> lambda.
/// </summary>
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
        var differences = EquivalencyValidator.Validate(assertions.Subject, expectation, BuildOptions(config), typeof(TExpectation));
        if (differences.Count > 0)
        {
            assertions.Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith(BuildFailureTemplate(differences, assertions.SubjectExpression), expectation);
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
        var differences = EquivalencyValidator.Validate(assertions.Subject, expectation, BuildOptions(config), typeof(TExpectation));
        assertions.Assert().ForCondition(differences.Count > 0).BecauseOf(because, becauseArgs)
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
        var differences = EquivalencyValidator.Validate(assertions.Subject, expectation, BuildOptions(config), typeof(IEnumerable<TExpectation>));
        if (differences.Count > 0)
        {
            assertions.Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith(BuildFailureTemplate(differences, assertions.SubjectExpression), expectation);
        }
        return new(assertions);
    }

    private static EquivalencyOptions<TExpectation> BuildOptions<TExpectation>(
        Func<EquivalencyOptions<TExpectation>, EquivalencyOptions<TExpectation>>? config)
    {
        var options = new EquivalencyOptions<TExpectation>();
        return config is null ? options : config(options);
    }

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
