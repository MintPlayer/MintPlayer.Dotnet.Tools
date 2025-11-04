using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Models;

internal sealed record CliCommandTree(
    CliCommandDefinition Command,
    ImmutableArray<CliCommandTree> Children);
