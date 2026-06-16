using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SlnLaunch.Services;

[Register(typeof(ISlnLaunchFileService), ServiceLifetime.Singleton, "SlnLaunchServices")]
public sealed class SlnLaunchFileService : ISlnLaunchFileService
{
    // Highest precedence first. Within a tier, every match is returned so the caller can report ambiguity.
    private static readonly string[] _searchPatterns = ["*.slnLaunch", "*.slnLaunch.user", "*.slnxLaunch"];

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public IReadOnlyList<string> Find(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        foreach (var pattern in _searchPatterns)
        {
            // ".slnLaunch.user" also matches the "*.slnLaunch" tier under some globbing rules — exclude it explicitly.
            var matches = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .Where(f => MatchesTier(f, pattern))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (matches.Length > 0)
                return matches;
        }

        return [];
    }

    private static bool MatchesTier(string file, string pattern) => pattern switch
    {
        "*.slnLaunch" => file.EndsWith(".slnLaunch", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    public SlnLaunchFile Load(string path)
    {
        if (!File.Exists(path))
            throw new SlnLaunchException($"Launch file not found: {path}");

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SlnLaunchException($"Could not read launch file '{path}': {ex.Message}", ex);
        }

        List<LaunchProfile>? profiles;
        try
        {
            profiles = JsonSerializer.Deserialize<List<LaunchProfile>>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new SlnLaunchException($"'{path}' is not a valid .slnLaunch file: {ex.Message}", ex);
        }

        if (profiles is null || profiles.Count == 0)
            throw new SlnLaunchException($"'{path}' contains no launch profiles.");

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new SlnLaunchException($"'{path}' contains a profile with no name.");

            foreach (var project in profile.Projects)
            {
                if (string.IsNullOrWhiteSpace(project.Path))
                    throw new SlnLaunchException($"Profile '{profile.Name}' in '{path}' has a project with no path.");
            }
        }

        return new SlnLaunchFile(path, profiles);
    }
}
