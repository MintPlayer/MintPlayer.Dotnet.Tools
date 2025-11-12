using System.Collections.Immutable;
using MintPlayer.ValueComparerGenerator.Attributes;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.CliGenerator.Models;

[AutoValueComparer]
internal sealed partial class CliCommandDefinition
{
    public string Namespace { get; set; } = null!;
    public string Declaration { get; set; } = null!;
    public string TypeName { get; set; } = null!;
    public string FullyQualifiedName { get; set; } = null!;
    public string? ParentFullyQualifiedName { get; set; }
    public bool IsRoot { get; set; }
    public string? CommandName { get; set; }
    public string? Description { get; set; }
    public bool HasHandler { get; set; }
    public string? HandlerMethodName { get; set; }
    public bool HandlerUsesCancellationToken { get; set; }
    public ImmutableArray<CliOptionDefinition> Options { get; set; } = ImmutableArray<CliOptionDefinition>.Empty;
    public ImmutableArray<CliArgumentDefinition> Arguments { get; set; } = ImmutableArray<CliArgumentDefinition>.Empty;
    /// <summary>
    /// If the original source type was nested, this is its immediate containing type's fully qualified name.
    /// Used to avoid generating a nested partial when a parent is specified via attribute instead of actual nesting.
    /// </summary>
    public string? OriginalContainingTypeFullyQualifiedName { get; set; }
    public PathSpec? PathSpec { get; set; }
}
