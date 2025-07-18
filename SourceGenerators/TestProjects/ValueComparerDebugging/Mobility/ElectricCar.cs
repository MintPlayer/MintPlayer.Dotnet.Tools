using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

public partial class Context
{
    public partial class Entities
    {
        [AutoValueComparer]
        public partial class ElectricCar : Car
        {
            public int BatteryCapacity { get; set; } // in kWh
        }
    }
}