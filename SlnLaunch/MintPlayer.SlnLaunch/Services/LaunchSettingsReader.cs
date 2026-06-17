using System.Text.Json;

namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Inspects a project's <c>Properties/launchSettings.json</c> to decide whether a named launch profile
/// is one the <c>dotnet</c> CLI can honor via <c>--launch-profile</c> (only <c>commandName: Project</c>
/// profiles qualify).
/// </summary>
internal static class LaunchSettingsReader
{
    private static readonly JsonDocumentOptions _options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// True only when the profile is positively identified as a non-Project profile (IIS Express,
    /// Docker, Executable, …). Returns false when launchSettings is absent, the profile isn't listed,
    /// or it's a Project profile — i.e. when the safe choice is to pass <c>--launch-profile</c> through
    /// and let the CLI be the authority.
    /// </summary>
    public static bool IsNonProjectProfile(string projectFilePath, string profileName)
    {
        var projectDir = Path.GetDirectoryName(projectFilePath);
        if (projectDir is null)
            return false;

        var settingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(settingsPath))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), _options);
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var profile in profiles.EnumerateObject())
            {
                if (!string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (profile.Value.TryGetProperty("commandName", out var commandName) && commandName.ValueKind == JsonValueKind.String)
                    return !string.Equals(commandName.GetString(), "Project", StringComparison.OrdinalIgnoreCase);

                return false; // profile found but no commandName → don't second-guess it
            }

            return false; // profile not listed → pass through; the CLI reports available profiles if wrong
        }
        catch (JsonException)
        {
            return false; // unreadable launchSettings → leave the decision to the CLI
        }
    }
}
