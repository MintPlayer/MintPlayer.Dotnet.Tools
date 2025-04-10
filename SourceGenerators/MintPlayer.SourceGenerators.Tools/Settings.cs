using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Tools;

[ValueComparer(typeof(ValueComparers.SettingsValueComparer))]
public sealed class Settings
{
    public string? RootNamespace { get; set; }
    public bool Disable { get; set; }
}