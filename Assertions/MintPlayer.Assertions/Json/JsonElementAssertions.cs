using System.Text.Json;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Json;

/// <summary>
/// Assertions on a <see cref="JsonElement"/>: deep JSON equivalency, property access, value-kind
/// checks and scalar value checks. The subject is nullable so that a null
/// <see cref="JsonDocument"/> can flow through <c>Should()</c> and fail with a clear message
/// instead of an exception. Operates on <see cref="JsonElement"/> only, so it is AOT-safe.
/// </summary>
public class JsonElementAssertions
{
    public JsonElementAssertions(JsonElement? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The element under test; null when the subject was a null <see cref="JsonDocument"/>.</summary>
    public JsonElement? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>
    /// Asserts the subject is deeply equivalent to the given JSON text: objects compared
    /// property-order-insensitively, arrays order-sensitively, numbers by numeric value
    /// (1.0 equals 1.00). The failure message lists all differences with their JSON paths.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="expectedJson"/> is not valid JSON.</exception>
    public AndConstraint<JsonElementAssertions> BeJsonEquivalentTo(string expectedJson, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedJson);
        using var document = ParseExpected(expectedJson);
        return BeJsonEquivalentTo(document.RootElement, because, becauseArgs);
    }

    /// <summary>
    /// Asserts the subject is deeply equivalent to the given element: objects compared
    /// property-order-insensitively, arrays order-sensitively, numbers by numeric value
    /// (1.0 equals 1.00). The failure message lists all differences with their JSON paths.
    /// </summary>
    public AndConstraint<JsonElementAssertions> BeJsonEquivalentTo(JsonElement expected, string? because = null, params object?[] becauseArgs)
    {
        var differences = Subject is { } subject ? JsonEquivalency.FindDifferences(subject, expected) : null;
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be JSON equivalent to {0}{reason}, but found <null>.", expected.GetRawText())
            .ForCondition(differences is null or { Count: 0 }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be JSON equivalent to {0}{reason}, but found the following differences:"
                + Environment.NewLine + EscapeBraces(string.Join(Environment.NewLine, differences ?? [])), expected.GetRawText());
        return new(this);
    }

    /// <summary>Asserts the subject is not deeply equivalent to the given JSON text.</summary>
    /// <exception cref="ArgumentException"><paramref name="expectedJson"/> is not valid JSON.</exception>
    public AndConstraint<JsonElementAssertions> NotBeJsonEquivalentTo(string expectedJson, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedJson);
        using var document = ParseExpected(expectedJson);
        return NotBeJsonEquivalentTo(document.RootElement, because, becauseArgs);
    }

    /// <summary>Asserts the subject is not deeply equivalent to the given element.</summary>
    public AndConstraint<JsonElementAssertions> NotBeJsonEquivalentTo(JsonElement unexpected, string? because = null, params object?[] becauseArgs)
    {
        var equivalent = Subject is { } subject && JsonEquivalency.FindDifferences(subject, unexpected).Count == 0;
        Assert().ForCondition(!equivalent).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be JSON equivalent to {0}{reason}.", unexpected.GetRawText());
        return new(this);
    }

    /// <summary>
    /// Asserts the subject is a JSON object with a property named <paramref name="name"/>;
    /// <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/> exposes the property's value.
    /// </summary>
    public AndWhichConstraint<JsonElementAssertions, JsonElement> HaveProperty(string name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        JsonElement value = default;
        var found = Subject is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(name, out value);
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have property {0}{reason}, but found <null>.", name)
            .ForCondition(Subject is null or { ValueKind: JsonValueKind.Object }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have property {0}{reason}, but " + NotAKind("a JSON object") + ".", name)
            .ForCondition(Subject is not { ValueKind: JsonValueKind.Object } || found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have property {0}{reason}, but it does not.", name);
        return new(this, value);
    }

    /// <summary>Asserts the subject does not have a property named <paramref name="name"/> (non-objects trivially pass).</summary>
    public AndConstraint<JsonElementAssertions> NotHaveProperty(string name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        var present = Subject is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(name, out _);
        Assert().ForCondition(!present).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have property {0}{reason}, but it does.", name);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON object.</summary>
    public AndConstraint<JsonElementAssertions> BeJsonObject(string? because = null, params object?[] becauseArgs)
        => BeKindCore(Subject is { ValueKind: JsonValueKind.Object }, "a JSON object", because, becauseArgs);

    /// <summary>Asserts the subject is a JSON array.</summary>
    public AndConstraint<JsonElementAssertions> BeJsonArray(string? because = null, params object?[] becauseArgs)
        => BeKindCore(Subject is { ValueKind: JsonValueKind.Array }, "a JSON array", because, becauseArgs);

    /// <summary>Asserts the subject is a JSON string.</summary>
    public AndConstraint<JsonElementAssertions> BeJsonString(string? because = null, params object?[] becauseArgs)
        => BeKindCore(Subject is { ValueKind: JsonValueKind.String }, "a JSON string", because, becauseArgs);

    /// <summary>Asserts the subject is a JSON number.</summary>
    public AndConstraint<JsonElementAssertions> BeJsonNumber(string? because = null, params object?[] becauseArgs)
        => BeKindCore(Subject is { ValueKind: JsonValueKind.Number }, "a JSON number", because, becauseArgs);

    /// <summary>Asserts the subject is a JSON boolean (true or false).</summary>
    public AndConstraint<JsonElementAssertions> BeJsonBoolean(string? because = null, params object?[] becauseArgs)
        => BeKindCore(Subject is { ValueKind: JsonValueKind.True or JsonValueKind.False }, "a JSON boolean", because, becauseArgs);

    /// <summary>Asserts the subject is JSON null (a null literal, not a missing document).</summary>
    public AndConstraint<JsonElementAssertions> BeJsonNull(string? because = null, params object?[] becauseArgs)
        => BeKindCore(Subject is { ValueKind: JsonValueKind.Null }, "JSON null", because, becauseArgs);

    /// <summary>Asserts the subject is a JSON string with exactly the given value (ordinal comparison).</summary>
    public AndConstraint<JsonElementAssertions> HaveStringValue(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var isString = Subject is { ValueKind: JsonValueKind.String };
        Assert().ForCondition(isString).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have string value {0}{reason}, but " + NotAKind("a JSON string") + ".", expected)
            .ForCondition(!isString || string.Equals(Subject!.Value.GetString(), expected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have string value {0}{reason}, but found {1}.", expected, isString ? Subject!.Value.GetString() : null);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON number with exactly the given value (1.0 equals 1.00).</summary>
    public AndConstraint<JsonElementAssertions> HaveNumberValue(decimal expected, string? because = null, params object?[] becauseArgs)
    {
        var isNumber = Subject is { ValueKind: JsonValueKind.Number };
        decimal actual = 0;
        var representable = isNumber && Subject!.Value.TryGetDecimal(out actual);
        Assert().ForCondition(isNumber).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have number value {0}{reason}, but " + NotAKind("a JSON number") + ".", expected)
            .ForCondition(!isNumber || (representable && actual == expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have number value {0}{reason}, but found {1}.", expected,
                !isNumber ? null : representable ? actual : Subject!.Value.GetRawText());
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON boolean with exactly the given value.</summary>
    public AndConstraint<JsonElementAssertions> HaveBooleanValue(bool expected, string? because = null, params object?[] becauseArgs)
    {
        var isBoolean = Subject is { ValueKind: JsonValueKind.True or JsonValueKind.False };
        var actual = Subject is { ValueKind: JsonValueKind.True };
        Assert().ForCondition(isBoolean).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have boolean value {0}{reason}, but " + NotAKind("a JSON boolean") + ".", expected)
            .ForCondition(!isBoolean || actual == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have boolean value {0}{reason}, but found {1}.", expected, actual);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON array with exactly the given number of elements.</summary>
    public AndConstraint<JsonElementAssertions> HaveArrayLength(int expected, string? because = null, params object?[] becauseArgs)
    {
        var length = Subject is { ValueKind: JsonValueKind.Array } array ? array.GetArrayLength() : -1;
        Assert().ForCondition(Subject is { ValueKind: JsonValueKind.Array }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have array length {0}{reason}, but " + NotAKind("a JSON array") + ".", expected)
            .ForCondition(Subject is not { ValueKind: JsonValueKind.Array } || length == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have array length {0}{reason}, but found {1}.", expected, length);
        return new(this);
    }

    private AndConstraint<JsonElementAssertions> BeKindCore(bool matches, string description, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be " + description + "{reason}, but found <null>.")
            .ForCondition(Subject is null || matches).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be " + description + "{reason}, but it is "
                + (Subject is { } subject ? JsonEquivalency.DescribeKind(subject.ValueKind) : "") + ".");
        return new(this);
    }

    /// <summary>Explains why the subject is not of the described kind: null, or a different kind.</summary>
    private string NotAKind(string description) => Subject is { } subject
        ? "it is " + JsonEquivalency.DescribeKind(subject.ValueKind) + " rather than " + description
        : "found <null>";

    /// <summary>
    /// Parses caller-supplied expected JSON; invalid JSON is a caller bug and throws
    /// <see cref="ArgumentException"/> rather than reporting an assertion failure.
    /// </summary>
    private static JsonDocument ParseExpected(string expectedJson)
    {
        try
        {
            return JsonDocument.Parse(expectedJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"The expected JSON is not valid: {ex.Message}", nameof(expectedJson), ex);
        }
    }

    /// <summary>
    /// Difference lines are embedded in the message template verbatim; escaping '{' keeps raw
    /// JSON fragments from being mistaken for template placeholders.
    /// </summary>
    private static string EscapeBraces(string text) => text.Replace("{", "{{");
}
