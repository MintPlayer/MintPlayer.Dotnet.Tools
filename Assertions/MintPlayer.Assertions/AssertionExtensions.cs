using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions;

/// <summary>
/// The fluent entry points. This class is partial: each assertion category contributes its own
/// Should() overloads from its own file. More specific overloads always win over this object
/// catch-all.
/// </summary>
public static partial class AssertionExtensions
{
    public static ObjectAssertions Should(this object? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);
}
