using System.Text.Json.Serialization;

namespace MintPlayer.Verz.Configuration;

public sealed class VerzConfig
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; } = "https://mintplayer.com/verz/v1/schema.json";

    [JsonPropertyName("Registries")]
    public List<RegistryEntry> Registries { get; set; } = new();

    [JsonPropertyName("Plugins")]
    public List<PluginEntry> Plugins { get; set; } = new();
}
