using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using System.Text.RegularExpressions;

namespace MintPlayer.Verz;

[CliCommand("init-dotnet", Description = "Replace <Version> tags in all csproj with placeholder 0.0.0-placeholder")]
[CliParentCommand(typeof(VerzCommand))]
public partial class InitDotnetCommand : ICliCommand
{
    private static readonly Regex VersionRegex = new("<Version>.*?</Version>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    [CliOption("--root", Description = "Root directory to scan for .csproj files"), NoInterfaceMember]
    public string? Root { get; set; }

    public Task<int> Execute(CancellationToken cancellationToken)
    {
        var rootDir = string.IsNullOrWhiteSpace(Root) ? Directory.GetCurrentDirectory() : Root;
        var csprojs = Directory.GetFiles(rootDir, "*.csproj", SearchOption.AllDirectories);

        var updated = 0;
        foreach (var csproj in csprojs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var text = File.ReadAllText(csproj);
                var original = text;
                if (VersionRegex.IsMatch(text))
                {
                    text = VersionRegex.Replace(text, "<Version>0.0.0-placeholder</Version>");
                }
                else
                {
                    var insertIdx = text.IndexOf("<PropertyGroup>", StringComparison.OrdinalIgnoreCase);
                    if (insertIdx >= 0)
                    {
                        var endIdx = text.IndexOf("</PropertyGroup>", insertIdx, StringComparison.OrdinalIgnoreCase);
                        if (endIdx > insertIdx)
                        {
                            var toInsert = "\n    <Version>0.0.0-placeholder</Version>\n";
                            text = text.Insert(endIdx, toInsert);
                        }
                    }
                }

                if (!string.Equals(original, text, StringComparison.Ordinal))
                {
                    File.WriteAllText(csproj, text);
                    updated++;
                }
            }
            catch
            {
                // Ignore errors so other projects still get updated.
            }
        }

        Console.WriteLine($"Updated {updated} project files with placeholder version.");
        return Task.FromResult(0);
    }
}
