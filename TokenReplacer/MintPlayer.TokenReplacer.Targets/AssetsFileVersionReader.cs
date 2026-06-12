using System.Text;

namespace MintPlayer.TokenReplacer.Targets;

/// <summary>
/// Reads resolved package versions from a NuGet <c>project.assets.json</c> file.
/// Only the top-level <c>"libraries"</c> object is inspected; its keys have the shape
/// <c>"PackageId/Version"</c>. Implemented as a minimal JSON scanner so the task assembly
/// has no JSON package dependency to ship next to it (the assets file is machine-generated
/// and well-formed).
/// </summary>
public static class AssetsFileVersionReader
{
    /// <summary>
    /// Returns a case-insensitive map of package id → resolved version for every library
    /// (packages and projects) in the assets file.
    /// </summary>
    /// <exception cref="FormatException">The JSON is malformed.</exception>
    public static Dictionary<string, string> ReadLibraryVersions(string json)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pos = 0;

        SkipWhitespace(json, ref pos);
        Expect(json, ref pos, '{');

        while (true)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) throw Unexpected(pos);
            if (json[pos] == '}') { pos++; break; }
            if (json[pos] == ',') { pos++; continue; }

            var propertyName = ReadString(json, ref pos);
            SkipWhitespace(json, ref pos);
            Expect(json, ref pos, ':');
            SkipWhitespace(json, ref pos);

            if (propertyName == "libraries" && pos < json.Length && json[pos] == '{')
            {
                pos++;
                while (true)
                {
                    SkipWhitespace(json, ref pos);
                    if (pos >= json.Length) throw Unexpected(pos);
                    if (json[pos] == '}') { pos++; break; }
                    if (json[pos] == ',') { pos++; continue; }

                    var key = ReadString(json, ref pos);
                    SkipWhitespace(json, ref pos);
                    Expect(json, ref pos, ':');
                    SkipWhitespace(json, ref pos);
                    SkipValue(json, ref pos);

                    var slash = key.IndexOf('/');
                    if (slash > 0)
                        result[key.Substring(0, slash)] = key.Substring(slash + 1);
                }
            }
            else
            {
                SkipValue(json, ref pos);
            }
        }

        return result;
    }

    private static void SkipWhitespace(string json, ref int pos)
    {
        while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\t' || json[pos] == '\r' || json[pos] == '\n'))
            pos++;
    }

    private static void Expect(string json, ref int pos, char expected)
    {
        if (pos >= json.Length || json[pos] != expected)
            throw new FormatException($"Expected '{expected}' at position {pos} of the assets file JSON.");
        pos++;
    }

    private static FormatException Unexpected(int pos) =>
        new($"Unexpected end of assets file JSON at position {pos}.");

    private static string ReadString(string json, ref int pos)
    {
        Expect(json, ref pos, '"');
        var sb = new StringBuilder();
        while (true)
        {
            if (pos >= json.Length) throw Unexpected(pos);
            var c = json[pos++];
            if (c == '"') break;
            if (c == '\\')
            {
                if (pos >= json.Length) throw Unexpected(pos);
                var escape = json[pos++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > json.Length) throw Unexpected(pos);
                        sb.Append((char)Convert.ToInt32(json.Substring(pos, 4), 16));
                        pos += 4;
                        break;
                    default:
                        throw new FormatException($"Invalid JSON escape '\\{escape}' at position {pos - 1}.");
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static void SkipValue(string json, ref int pos)
    {
        if (pos >= json.Length) throw Unexpected(pos);
        switch (json[pos])
        {
            case '"':
                ReadString(json, ref pos);
                return;
            case '{':
                SkipComposite(json, ref pos, '{', '}');
                return;
            case '[':
                SkipComposite(json, ref pos, '[', ']');
                return;
            default:
                // number / true / false / null
                while (pos < json.Length && json[pos] != ',' && json[pos] != '}' && json[pos] != ']'
                       && json[pos] != ' ' && json[pos] != '\t' && json[pos] != '\r' && json[pos] != '\n')
                    pos++;
                return;
        }
    }

    private static void SkipComposite(string json, ref int pos, char open, char close)
    {
        var depth = 0;
        while (pos < json.Length)
        {
            var c = json[pos];
            if (c == '"')
            {
                ReadString(json, ref pos);
                continue;
            }
            pos++;
            if (c == open) depth++;
            else if (c == close && --depth == 0) return;
        }
        throw Unexpected(pos);
    }
}
