using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.ValueComparers;

namespace MintPlayer.SourceGenerators.Models;

[ValueComparer(typeof(InjectFieldValueComparer))]
public class InjectField
{
    public string? Type { get; set; }
    public string? Name { get; set; }
}
