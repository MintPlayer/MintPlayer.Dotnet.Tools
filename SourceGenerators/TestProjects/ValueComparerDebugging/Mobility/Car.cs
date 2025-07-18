using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

// Add/remove the abstract keyword to see how it affects the generated code.

public partial class Context
{
    public partial class Entities
    {
        [AutoValueComparer]
        public abstract partial class Car : Vehicle
        {
            public int NumberOfDoors { get; set; }
        }
    }
}