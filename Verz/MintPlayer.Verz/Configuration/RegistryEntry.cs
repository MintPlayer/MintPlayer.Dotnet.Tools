using System.Text.Json.Serialization;

namespace MintPlayer.Verz.Configuration;

public sealed class RegistryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("url")]
    public string Url { get; set; } = default!;

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }
}
