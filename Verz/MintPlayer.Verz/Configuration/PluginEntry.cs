using System.Text.Json.Serialization;

namespace MintPlayer.Verz.Configuration;

[JsonConverter(typeof(PluginEntryJsonConverter))]
public sealed class PluginEntry
{
    public string Id { get; set; } = default!;
    public string? Version { get; set; }
}
