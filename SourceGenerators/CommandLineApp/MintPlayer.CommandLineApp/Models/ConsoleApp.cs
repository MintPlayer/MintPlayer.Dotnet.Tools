using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.CommandLineApp.Models;

[AutoValueComparer]
public partial class ConsoleApp
{
    public Microsoft.CodeAnalysis.Location? ClassSymbolLocation { get; set; }
    public string? Namespace { get; set; }
    public string? ClassName { get; set; }
    public string? Description { get; set; }
}
