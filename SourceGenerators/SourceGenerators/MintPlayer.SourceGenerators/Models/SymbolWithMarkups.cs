using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[GenerateEquality]
public partial class SymbolWithMarkups
{
    /// <summary>The fully qualified type name: one model per type, however many parts carry a summary.</summary>
    public string? TypeName { get; set; }
    public string? MarkupText { get; set; }
    public PathSpec? PathSpec { get; set; }
}
