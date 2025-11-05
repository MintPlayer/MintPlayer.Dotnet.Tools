using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Cli.Models;

internal sealed record CliCommandTree(
    CliCommandDefinition Command,
    ImmutableArray<CliCommandTree> Children);
