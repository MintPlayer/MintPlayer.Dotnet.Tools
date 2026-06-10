using System.Text.Json;

namespace MintPlayer.Verz.Configuration;

public static class VerzConfigSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static VerzConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<VerzConfig>(stream, Options)
            ?? throw new InvalidDataException($"failed to deserialize {path}");
    }

    public static void Save(VerzConfig config, string path)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, config, Options);
    }
}
