namespace MintPlayer.Resilience.SourceGenerator.Diagnostics;

/// <summary>
/// The analyzer rules of MintPlayer.Resilience (PRD §2.6). This folder is where their DiagnosticAnalyzers live,
/// one <c>*Analyzer.cs</c> plus <c>*Analyzer.Rule.cs</c> per rule, as in the Assertions generator.
/// </summary>
/// <remarks>
/// The generator reports nothing itself: a class it cannot generate gets no code (its model carries a
/// <c>SkipReason</c>), and these rules explain why. MPR0004–MPR0007 complete milestone 4.
/// </remarks>
internal static class DiagnosticIds
{
    /// <summary>Invalid attribute combination (inner timeout ≥ outer timeout, a missing fallback action, a typed-only strategy in a generic pipeline, …).</summary>
    public const string InvalidCombination = "MPR0004";

    /// <summary><c>ExecuteAsync</c> result not awaited.</summary>
    public const string NotAwaited = "MPR0005";

    /// <summary>Pooled <c>ValueTask</c> misuse: awaited twice, <c>.Result</c> without an await, or <c>WhenAll</c>/<c>WhenAny</c> without <c>.AsTask()</c>.</summary>
    public const string PooledValueTaskMisuse = "MPR0006";

    /// <summary>Strategy attributes split across <c>partial</c> declarations.</summary>
    public const string SplitAcrossPartials = "MPR0007";
}
