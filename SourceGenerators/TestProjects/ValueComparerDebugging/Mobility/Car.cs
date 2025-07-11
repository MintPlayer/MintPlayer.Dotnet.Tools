using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

// Add/remove the abstract keyword to see how it affects the generated code.

[AutoValueComparer]
public abstract partial class Car : Vehicle
{
    public int NumberOfDoors { get; set; }
}
