using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class SymbolWithMarkups
{
    public string? TypeName { get; set; }
    public string? MarkupText { get; set; }
    public PathSpec? PathSpec { get; set; }
}
