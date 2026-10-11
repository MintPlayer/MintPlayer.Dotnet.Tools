using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using System.Globalization;

namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// A diagnostic to report later, held by value so it can sit in an incremental-pipeline model: no Roslyn object at
/// all. Not the <see cref="Location"/> (a <see cref="LocationKey"/> instead), not a symbol, and not the
/// <see cref="DiagnosticDescriptor"/> either: only its id.
/// </summary>
/// <remarks>
/// <para>
/// Equality is by value: the rule id, the <see cref="LocationKey"/> and the message arguments as strings.
/// </para>
/// <para>
/// The descriptor passed to <see cref="Create"/> or the constructor is registered under its id and looked up again
/// when the diagnostic is reported. Descriptors are <c>static readonly</c> fields with ids that are unique across
/// the generators, so the id identifies the descriptor. Never create a descriptor per call with a message that
/// varies: put the varying part in the arguments.
/// </para>
/// <para>
/// Report through <see cref="GeneratorExtensions.ReportDiagnostics(IncrementalGeneratorInitializationContext, IncrementalValuesProvider{DiagnosticInfo}[])"/>,
/// which maps the location back onto the compilation's own tree.
/// </para>
/// </remarks>
public sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    private static readonly ConcurrentDictionary<string, DiagnosticDescriptor> Descriptors = new(StringComparer.Ordinal);

    public DiagnosticInfo(DiagnosticDescriptor rule, LocationKey? location, EquatableArray<string> args)
    {
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        var registered = Descriptors.GetOrAdd(rule.Id, rule);
        if (!registered.Equals(rule))
        {
            // The id stands for the descriptor, so two different descriptors under one id would report one with the
            // other's message or severity. Fail loudly (a generator crash the test harness catches) instead.
            throw new InvalidOperationException($"Two different diagnostic descriptors share the id '{rule.Id}'. Give each descriptor its own id.");
        }
        RuleId = rule.Id;
        Location = location;
        Args = args;
    }

    /// <summary>
    /// Captures a diagnostic at <paramref name="location"/>. Each argument is stored as its string form (invariant
    /// culture for numbers; a symbol as its display string), which is also how the message would format it.
    /// </summary>
    public static DiagnosticInfo Create(DiagnosticDescriptor rule, Location? location, params object?[] args)
        => new(rule, location is null || location.Kind == LocationKind.None ? null : location.AsKey(), ToStrings(args));

    /// <summary>The <see cref="DiagnosticDescriptor.Id"/> of the rule.</summary>
    public string RuleId { get; }

    /// <summary>The descriptor registered under <see cref="RuleId"/>. Read at report time, never compared.</summary>
    public DiagnosticDescriptor Rule => Descriptors[RuleId];

    /// <summary>Null for a diagnostic without a location; it is then reported at <see cref="Location.None"/>.</summary>
    public LocationKey? Location { get; }

    public EquatableArray<string> Args { get; }

    /// <summary>
    /// Builds the <see cref="Diagnostic"/>. The location is mapped onto <paramref name="compilation"/>'s own tree, so
    /// <c>#pragma</c> and <c>.editorconfig</c> severity apply to it; a key that no longer fits (a stale span, an
    /// unknown file) gives <see cref="Location.None"/> rather than an exception.
    /// </summary>
    public Diagnostic ToDiagnostic(Compilation? compilation)
    {
        var location = compilation is null ? null : Location.ToLocation(compilation);
        return Diagnostic.Create(Rule, location ?? Microsoft.CodeAnalysis.Location.None, Args.Cast<object?>().ToArray());
    }

    public bool Equals(DiagnosticInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(RuleId, other.RuleId, StringComparison.Ordinal)
            && Equals(Location, other.Location)
            && Args.Equals(other.Args);
    }

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode()
        => ValueEquality.Combine(ValueEquality.Combine(StringComparer.Ordinal.GetHashCode(RuleId), Location?.GetHashCode() ?? 0), Args.GetHashCode());

    public override string ToString() => $"{RuleId} {string.Join(", ", Args)}";

    private static EquatableArray<string> ToStrings(object?[]? args)
    {
        if (args is null || args.Length == 0) return EquatableArray<string>.Empty;
        var result = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            result[i] = args[i] switch
            {
                null => string.Empty,
                ISymbol symbol => symbol.ToDisplayString(),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString() ?? string.Empty,
            };
        }
        return result;
    }
}
