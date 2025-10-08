using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.CommandLineApp.Models;

[AutoValueComparer]
public partial class ConsoleApp
{
    public string? Namespace { get; set; }
    public string? ClassName { get; set; }
}
