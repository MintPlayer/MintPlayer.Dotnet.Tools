using Microsoft.CodeAnalysis;
using System.Globalization;

namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// A diagnostic to report later, held by value so it can sit in an incremental-pipeline model: no
/// <see cref="Location"/>, no symbol and no other compilation-bound object.
/// </summary>
/// <remarks>
/// <para>
/// Equality is by value: the descriptor, the <see cref="LocationKey"/> and the message arguments as strings. A
/// <see cref="DiagnosticDescriptor"/> is itself value-equatable (id, title, message format, category, severity,
/// ...) and is normally a <c>static readonly</c> field, so it compares cheaply and stays equal between runs.
/// Never create a descriptor per call with a message that varies: put the varying part in the arguments.
/// </para>
/// <para>
/// Report through <see cref="GeneratorExtensions.ReportDiagnostics(IncrementalGeneratorInitializationContext, IncrementalValuesProvider{DiagnosticInfo}[])"/>,
/// which maps the location back onto the compilation's own tree.
/// </para>
/// </remarks>
public sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    public DiagnosticInfo(DiagnosticDescriptor rule, LocationKey? location, EquatableArray<string> args)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        Location = location;
        Args = args;
    }

    /// <summary>
    /// Captures a diagnostic at <paramref name="location"/>. Each argument is stored as its string form (invariant
    /// culture for numbers; a symbol as its display string), which is also how the message would format it.
    /// </summary>
    public static DiagnosticInfo Create(DiagnosticDescriptor rule, Location? location, params object?[] args)
        => new(rule, location is null || location.Kind == LocationKind.None ? null : location.AsKey(), ToStrings(args));

    public DiagnosticDescriptor Rule { get; }

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
        return Rule.Equals(other.Rule)
            && Equals(Location, other.Location)
            && Args.Equals(other.Args);
    }

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode()
        => ValueEquality.Combine(ValueEquality.Combine(Rule.GetHashCode(), Location?.GetHashCode() ?? 0), Args.GetHashCode());

    public override string ToString() => $"{Rule.Id} {string.Join(", ", Args)}";

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
