using System.Text.Json;
using System.Text.Json.Nodes;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Json;

/// <summary>
/// Assertions on a <see cref="JsonNode"/>, mirroring <see cref="JsonElementAssertions"/>.
/// Structural checks are performed on a <see cref="JsonElement"/> rendered from the node
/// (AOT-safe, no reflection-based serialization); <see cref="HaveProperty"/> exposes the
/// property's <see cref="JsonNode"/> for further node-based drilling.
/// </summary>
public class JsonNodeAssertions : ReferenceTypeAssertions<JsonNode, JsonNodeAssertions>
{
    public JsonNodeAssertions(JsonNode? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>The element-based assertions this class delegates its structural checks to.</summary>
    private JsonElementAssertions Element => new(ToElement(), SubjectExpression);

    /// <summary>
    /// Asserts the subject is deeply equivalent to the given JSON text: objects compared
    /// property-order-insensitively, arrays order-sensitively, numbers by numeric value
    /// (1.0 equals 1.00). The failure message lists all differences with their JSON paths.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="expectedJson"/> is not valid JSON.</exception>
    public AndConstraint<JsonNodeAssertions> BeJsonEquivalentTo(string expectedJson, string? because = null, params object?[] becauseArgs)
    {
        Element.BeJsonEquivalentTo(expectedJson, because, becauseArgs);
        return new(this);
    }

    /// <inheritdoc cref="BeJsonEquivalentTo(string, string?, object?[])"/>
    public AndConstraint<JsonNodeAssertions> BeJsonEquivalentTo(JsonElement expected, string? because = null, params object?[] becauseArgs)
    {
        Element.BeJsonEquivalentTo(expected, because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is not deeply equivalent to the given JSON text.</summary>
    /// <exception cref="ArgumentException"><paramref name="expectedJson"/> is not valid JSON.</exception>
    public AndConstraint<JsonNodeAssertions> NotBeJsonEquivalentTo(string expectedJson, string? because = null, params object?[] becauseArgs)
    {
        Element.NotBeJsonEquivalentTo(expectedJson, because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is not deeply equivalent to the given element.</summary>
    public AndConstraint<JsonNodeAssertions> NotBeJsonEquivalentTo(JsonElement unexpected, string? because = null, params object?[] becauseArgs)
    {
        Element.NotBeJsonEquivalentTo(unexpected, because, becauseArgs);
        return new(this);
    }

    /// <summary>
    /// Asserts the subject is a JSON object with a property named <paramref name="name"/>;
    /// <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/> exposes the property's node
    /// (null when the property holds a JSON null literal).
    /// </summary>
    public AndWhichConstraint<JsonNodeAssertions, JsonNode?> HaveProperty(string name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        JsonNode? value = null;
        var found = Subject is JsonObject obj && obj.TryGetPropertyValue(name, out value);
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have property {0}{reason}, but found <null>.", name)
            .ForCondition(Subject is null or JsonObject).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have property {0}{reason}, but " + NotAKind("a JSON object") + ".", name)
            .ForCondition(Subject is not JsonObject || found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have property {0}{reason}, but it does not.", name);
        return new(this, value);
    }

    /// <summary>Asserts the subject does not have a property named <paramref name="name"/> (non-objects trivially pass).</summary>
    public AndConstraint<JsonNodeAssertions> NotHaveProperty(string name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        var present = Subject is JsonObject obj && obj.ContainsKey(name);
        Assert().ForCondition(!present).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have property {0}{reason}, but it does.", name);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON object.</summary>
    public AndConstraint<JsonNodeAssertions> BeJsonObject(string? because = null, params object?[] becauseArgs)
    {
        Element.BeJsonObject(because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON array.</summary>
    public AndConstraint<JsonNodeAssertions> BeJsonArray(string? because = null, params object?[] becauseArgs)
    {
        Element.BeJsonArray(because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON string.</summary>
    public AndConstraint<JsonNodeAssertions> BeJsonString(string? because = null, params object?[] becauseArgs)
    {
        Element.BeJsonString(because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON number.</summary>
    public AndConstraint<JsonNodeAssertions> BeJsonNumber(string? because = null, params object?[] becauseArgs)
    {
        Element.BeJsonNumber(because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON boolean (true or false).</summary>
    public AndConstraint<JsonNodeAssertions> BeJsonBoolean(string? because = null, params object?[] becauseArgs)
    {
        Element.BeJsonBoolean(because, becauseArgs);
        return new(this);
    }

    /// <summary>
    /// Asserts the subject represents JSON null. In the <see cref="JsonNode"/> model a JSON null
    /// literal is a null node reference, so a null subject passes.
    /// </summary>
    public AndConstraint<JsonNodeAssertions> BeJsonNull(string? because = null, params object?[] becauseArgs)
    {
        var isNull = Subject is null || Subject.GetValueKind() == JsonValueKind.Null;
        Assert().ForCondition(isNull).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be JSON null{reason}, but it is "
                + (Subject is null ? "" : JsonEquivalency.DescribeKind(Subject.GetValueKind())) + ".");
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON string with exactly the given value (ordinal comparison).</summary>
    public AndConstraint<JsonNodeAssertions> HaveStringValue(string expected, string? because = null, params object?[] becauseArgs)
    {
        Element.HaveStringValue(expected, because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON number with exactly the given value (1.0 equals 1.00).</summary>
    public AndConstraint<JsonNodeAssertions> HaveNumberValue(decimal expected, string? because = null, params object?[] becauseArgs)
    {
        Element.HaveNumberValue(expected, because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON boolean with exactly the given value.</summary>
    public AndConstraint<JsonNodeAssertions> HaveBooleanValue(bool expected, string? because = null, params object?[] becauseArgs)
    {
        Element.HaveBooleanValue(expected, because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts the subject is a JSON array with exactly the given number of elements.</summary>
    public AndConstraint<JsonNodeAssertions> HaveArrayLength(int expected, string? because = null, params object?[] becauseArgs)
    {
        Element.HaveArrayLength(expected, because, becauseArgs);
        return new(this);
    }

    /// <summary>
    /// Renders the node to a detached <see cref="JsonElement"/> (AOT-safe: text round-trip, no
    /// reflection-based serialization). Null for a null subject.
    /// </summary>
    private JsonElement? ToElement()
    {
        if (Subject is null) return null;
        using var document = JsonDocument.Parse(Subject.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>Explains why the subject is not of the described kind: null, or a different kind.</summary>
    private string NotAKind(string description) => Subject is null
        ? "found <null>"
        : "it is " + JsonEquivalency.DescribeKind(Subject.GetValueKind()) + " rather than " + description;
}
