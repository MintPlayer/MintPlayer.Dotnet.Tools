using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Configuration;

namespace MintPlayer.Verz.Commands;

public sealed class InitCommand(ILogger<InitCommand> logger)
{
    public Task<int> HandleAsync(InitOptions options, CancellationToken cancellationToken)
    {
        var cwd = Directory.GetCurrentDirectory();
        var configPath = Path.Combine(cwd, "verz.json");

        if (File.Exists(configPath))
        {
            throw new InitConflictException(configPath);
        }

        var config = new VerzConfig
        {
            Registries = BuildRegistries(options.Registries),
            Plugins = new List<PluginEntry>(),
        };

        VerzConfigSerializer.Save(config, configPath);
        logger.LogInformation("created {Path} with {Count} registries and 0 plugins",
            configPath, config.Registries.Count);

        if (options.StampPlaceholders)
        {
            logger.LogWarning("--stamp-placeholders is not implemented in this milestone");
        }

        Console.WriteLine($"Created verz.json ({config.Registries.Count} registries, 0 plugins).");
        Console.WriteLine("Next: edit verz.json to add SDK and registry plugins, then commit.");
        return Task.FromResult(0);
    }

    private static List<RegistryEntry> BuildRegistries(IReadOnlyList<string> raw)
    {
        if (raw.Count == 0)
        {
            return new List<RegistryEntry>
            {
                new() { Id = "nuget.org", Kind = "nuget", Url = "https://api.nuget.org/v3/index.json" },
            };
        }

        var list = new List<RegistryEntry>(capacity: raw.Count);
        foreach (var spec in raw)
        {
            var eq = spec.IndexOf('=');
            if (eq <= 0 || eq == spec.Length - 1)
            {
                throw new ArgumentException($"--registry expects id=url, got '{spec}'");
            }
            var id = spec[..eq];
            var url = spec[(eq + 1)..];
            list.Add(new RegistryEntry
            {
                Id = id,
                Url = url,
                Kind = url.Contains("registry.npmjs.org", StringComparison.OrdinalIgnoreCase) ||
                       url.Contains("npm.pkg.github.com", StringComparison.OrdinalIgnoreCase)
                    ? "npm"
                    : "nuget",
            });
        }
        return list;
    }
}

public sealed record InitOptions(bool StampPlaceholders, IReadOnlyList<string> Registries);
