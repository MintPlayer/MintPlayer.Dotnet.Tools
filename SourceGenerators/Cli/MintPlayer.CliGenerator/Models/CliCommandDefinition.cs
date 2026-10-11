using System.Collections.Immutable;
using MintPlayer.ValueComparerGenerator.Attributes;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.CliGenerator.Models;

[GenerateEquality]
internal sealed partial class CliCommandDefinition
{
    public string Namespace { get; set; } = null!;
    public string TypeName { get; set; } = null!;
    public string FullyQualifiedName { get; set; } = null!;
    public string? ParentFullyQualifiedName { get; set; }
    public bool IsRoot { get; set; }
    public string? CommandName { get; set; }
    public string? Description { get; set; }
    public ImmutableArray<CliOptionDefinition> Options { get; set; } = ImmutableArray<CliOptionDefinition>.Empty;
    public ImmutableArray<CliArgumentDefinition> Arguments { get; set; } = ImmutableArray<CliArgumentDefinition>.Empty;
    /// <summary>The containing types and the command itself, re-declared by <c>OpenPathSpec(includeTarget: true)</c>.</summary>
    public PathSpec PathSpec { get; set; } = null!;
}
