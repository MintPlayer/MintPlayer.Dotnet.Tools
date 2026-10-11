using MintPlayer.ValueComparerGenerator.Attributes;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.CliGenerator.Models;

/// <summary>
/// A root command's type name and location, for the duplicate-name check. Kept out of
/// <see cref="CliCommandDefinition"/> so that a location change never invalidates the producer.
/// </summary>
[GenerateEquality]
internal sealed partial class CliRootName
{
    public string TypeName { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public LocationKey? Location { get; set; }
}
