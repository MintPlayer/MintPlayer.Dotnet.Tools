namespace MintPlayer.Assertions.Formatting;

/// <summary>
/// The knobs on failure-message rendering: how deep, how wide, how long, and whether a graph is
/// laid out over several lines.
/// </summary>
/// <remarks>
/// <para>
/// All of this is free by construction. <c>FailWith</c> returns before rendering anything when the
/// condition holds, so nothing here runs in a green suite — which is what makes richer failure
/// output admissible under §0 of the Phase 2 PRD at all.
/// </para>
/// <para>
/// A record, so a scope can take the global options and change one thing with a <c>with</c>
/// expression instead of copying every field and getting one of them wrong.
/// </para>
/// </remarks>
public sealed record FormattingOptions
{
    /// <summary>The defaults: the values the formatter used before any of this was configurable.</summary>
    public static FormattingOptions Default { get; } = new();

    /// <summary>How deep into an object graph to render before collapsing a node. Default 3.</summary>
    public int MaxDepth { get; init; } = 3;

    /// <summary>How many characters of a string to render before truncating. Default 512.</summary>
    public int MaxStringLength { get; init; } = 512;

    /// <summary>How many items of a sequence to render before truncating. Default 32.</summary>
    public int MaxEnumerableItems { get; init; } = 32;

    /// <summary>
    /// How many lines the rendered value may occupy before the rest is cut. Default 100.
    /// </summary>
    /// <remarks>
    /// Only bites with <see cref="UseLineBreaks"/>, where a large graph genuinely can run to
    /// hundreds of lines and bury the difference that caused the failure.
    /// </remarks>
    public int MaxLines { get; init; } = 100;

    /// <summary>
    /// Renders objects and collections one member or item per line, indented by depth, instead of on
    /// a single line. Off by default.
    /// </summary>
    /// <remarks>
    /// The single flat line is right for the small values most assertions fail on, and unreadable
    /// for a large graph — which is the case <c>BeEquivalentTo</c> fails with. Off by default so no
    /// existing message changes shape; turn it on for a scope around a graph comparison.
    /// </remarks>
    public bool UseLineBreaks { get; init; }
}
