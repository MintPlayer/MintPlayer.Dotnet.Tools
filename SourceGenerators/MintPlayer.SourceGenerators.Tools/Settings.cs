namespace MintPlayer.SourceGenerators.Tools;

[ValueComparer(typeof(SettingsValueComparer))]
public sealed class Settings
{
    public string? RootNamespace { get; set; }
    public string? IncrementalValueProviderSymbol { get; set; }
}