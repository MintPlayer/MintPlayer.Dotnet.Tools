using System.Text.Json;
using System.Text.Json.Serialization;

namespace MintPlayer.Verz.Configuration;

internal sealed class PluginEntryJsonConverter : JsonConverter<PluginEntry>
{
    public override PluginEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var id = reader.GetString();
                return id is null ? null : new PluginEntry { Id = id };

            case JsonTokenType.StartObject:
                string? objId = null;
                string? objVersion = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;
                    var name = reader.GetString();
                    reader.Read();
                    if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                        objId = reader.GetString();
                    else if (string.Equals(name, "version", StringComparison.OrdinalIgnoreCase))
                        objVersion = reader.GetString();
                    else
                        reader.Skip();
                }
                if (objId is null) throw new JsonException("plugin entry object missing 'id'");
                return new PluginEntry { Id = objId, Version = objVersion };

            default:
                throw new JsonException($"plugin entry must be string or object, got {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, PluginEntry value, JsonSerializerOptions options)
    {
        if (value.Version is null)
        {
            writer.WriteStringValue(value.Id);
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("version", value.Version);
            writer.WriteEndObject();
        }
    }
}
