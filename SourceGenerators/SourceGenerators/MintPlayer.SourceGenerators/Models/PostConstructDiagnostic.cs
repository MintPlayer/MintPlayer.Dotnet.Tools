using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class PostConstructDiagnostic
{
    // MINT001 objects to a Roslyn type in a model. DiagnosticDescriptor is safe: it is immutable,
    // equatable by value, and a static singleton here, so unlike a symbol or a syntax node it
    // neither pins a compilation nor changes between runs.
#pragma warning disable MINT001
    public DiagnosticDescriptor Rule { get; set; } = null!;
#pragma warning restore MINT001
    public LocationKey? Location { get; set; }
    public string[] MessageArgs { get; set; } = [];
}
