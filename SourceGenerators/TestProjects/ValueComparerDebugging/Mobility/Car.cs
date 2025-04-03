using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

[AutoValueComparer]
public partial class Car : Vehicle
{
    public int NumberOfDoors { get; set; }
}
