using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class PostConstructDiagnostic
{
    public DiagnosticDescriptor Rule { get; set; } = null!;
    public LocationKey? Location { get; set; }
    public string[] MessageArgs { get; set; } = [];
}
