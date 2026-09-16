namespace MintPlayer.Assertions.Formatting;

/// <summary>
/// Renders one kind of value into failure-message text, ahead of the built-in rendering.
/// </summary>
/// <remarks>
/// <para>
/// Register globally with <see cref="Formatter.Register"/>, or for the duration of one scope with
/// <see cref="AssertionScope.Using(IValueFormatter)"/>. Scope formatters are consulted first, then
/// global ones, then the built-in rendering; the first that says it can format a value wins.
/// </para>
/// <para>
/// There is deliberately no assembly scan for a <c>[ValueFormatter]</c> attribute, which is how
/// FluentAssertions discovers these. Scanning every loaded assembly for attributed types is exactly
/// what an AOT- and trimming-friendly library cannot do: the types are reachable only by reflection,
/// so a trimmer removes them and the scan finds nothing, silently. Explicit registration is one line
/// and cannot fail that way.
/// </para>
/// <para>
/// Formatting only ever runs on the failure path, so an implementation may be as expensive as it
/// likes.
/// </para>
/// </remarks>
public interface IValueFormatter
{
    /// <summary>True when this formatter handles <paramref name="value"/>, which is never null.</summary>
    bool CanFormat(object value);

    /// <summary>
    /// Renders <paramref name="value"/>. Use <paramref name="formatChild"/> for nested values so
    /// they go through the whole pipeline — depth limits, cycle detection and other formatters
    /// included.
    /// </summary>
    string Format(object value, Func<object?, string> formatChild);
}
