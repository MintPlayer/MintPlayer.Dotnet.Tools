using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using MintPlayer.Assertions.Json;

namespace MintPlayer.Assertions;

public static partial class AssertionExtensions
{
    /// <summary>Returns assertions for a <see cref="JsonElement"/>.</summary>
    public static JsonElementAssertions Should(this JsonElement subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a <see cref="JsonNode"/> (or any of its subclasses).</summary>
    public static JsonNodeAssertions Should(this JsonNode? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>
    /// Returns assertions for a <see cref="JsonDocument"/>'s root element. A null document yields
    /// a null subject, so assertions on it fail with a clear message instead of throwing.
    /// </summary>
    public static JsonElementAssertions Should(this JsonDocument? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject?.RootElement, subjectExpression);
}
