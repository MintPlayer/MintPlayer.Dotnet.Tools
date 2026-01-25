using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>
/// Represents a diagnostic for [Config], [ConnectionString], or [Options] attributes.
/// </summary>
[AutoValueComparer]
public partial class ConfigDiagnostic
{
    public DiagnosticDescriptor Rule { get; set; } = null!;
    public LocationKey? Location { get; set; }
    public string[] MessageArgs { get; set; } = [];
}
