namespace MintPlayer.SourceGenerators.Cli.Models;

internal sealed record CliArgumentDefinition(
    string PropertyName,
    string PropertyType,
    int Position,
    string ArgumentName,
    string? Description,
    bool Required);
