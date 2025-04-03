using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

[AutoValueComparer]
public partial class Motorcycle : Vehicle
{
    public bool HasSidecar { get; set; }
}
