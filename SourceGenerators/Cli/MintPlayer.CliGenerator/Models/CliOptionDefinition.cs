using System.Collections.Generic;

namespace MintPlayer.CliGenerator.Models;

internal sealed record CliOptionDefinition(
    string PropertyName,
    string PropertyType,
    IReadOnlyList<string> Aliases,
    string? Description,
    bool Required,
    bool Hidden,
    string? DefaultValueExpression,
    bool HasDefaultValue);
