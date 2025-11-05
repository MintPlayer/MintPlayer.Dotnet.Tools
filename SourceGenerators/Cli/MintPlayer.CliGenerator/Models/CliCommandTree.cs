using System.Collections.Immutable;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.CliGenerator.Models;

[AutoValueComparer]
internal sealed partial class CliCommandTree
{
    public CliCommandDefinition Command { get; set; } = null!;
    public ImmutableArray<CliCommandTree> Children { get; set; } = ImmutableArray<CliCommandTree>.Empty;
}
