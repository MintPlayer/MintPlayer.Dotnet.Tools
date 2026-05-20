using System.Text.Json;
using System.Text.Json.Nodes;

namespace MintPlayer.Verz.Sdks.NodeJS;

/// <summary>
/// Minimal package.json view: name, private, version, and the four
/// dependency dictionaries Verz cares about. Edits are done via JsonNode
/// since the file may be re-written during set-versions / publish.
/// </summary>
internal sealed class PackageJsonReader
{
    public PackageJsonReader(string path)
    {
        Path = path;
        var text = File.ReadAllText(path);
        Root = JsonNode.Parse(text)?.AsObject()
            ?? throw new InvalidDataException($"{path}: not a JSON object");
    }

    public string Path { get; }
    public JsonObject Root { get; }

    public string? Name => Root["name"]?.GetValue<string>();

    public string? Version => Root["version"]?.GetValue<string>();

    public bool Private =>
        Root["private"] is JsonNode node && node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(node.GetValue<string>(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    public IReadOnlyDictionary<string, string> Dependencies =>
        ReadDepDict("dependencies");

    public IReadOnlyDictionary<string, string> DevDependencies =>
        ReadDepDict("devDependencies");

    public IReadOnlyDictionary<string, string> PeerDependencies =>
        ReadDepDict("peerDependencies");

    public IReadOnlyList<string>? Workspaces
    {
        get
        {
            var node = Root["workspaces"];
            if (node is null) return null;

            // npm/yarn 1: ["packages/*"]
            if (node is JsonArray arr)
            {
                return arr.Select(n => n?.GetValue<string>())
                    .Where(s => !string.IsNullOrEmpty(s))!
                    .Cast<string>()
                    .ToArray();
            }

            // yarn classic: { "packages": ["..."] }
            if (node is JsonObject obj && obj["packages"] is JsonArray pkgArr)
            {
                return pkgArr.Select(n => n?.GetValue<string>())
                    .Where(s => !string.IsNullOrEmpty(s))!
                    .Cast<string>()
                    .ToArray();
            }

            return null;
        }
    }

    private IReadOnlyDictionary<string, string> ReadDepDict(string section)
    {
        if (Root[section] is not JsonObject obj) return new Dictionary<string, string>();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in obj)
        {
            if (kv.Value is null) continue;
            try { result[kv.Key] = kv.Value.GetValue<string>(); }
            catch { /* skip non-string entries */ }
        }
        return result;
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path, Root.ToJsonString(options));
    }
}
