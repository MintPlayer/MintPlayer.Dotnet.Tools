using System.Collections.Immutable;

namespace MintPlayer.CliGenerator.Models;

internal sealed record CliCommandTree(
    CliCommandDefinition Command,
    ImmutableArray<CliCommandTree> Children);
