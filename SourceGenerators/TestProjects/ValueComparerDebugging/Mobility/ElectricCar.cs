using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

[AutoValueComparer]
public partial class ElectricCar : Car
{
    public int BatteryCapacity { get; set; } // in kWh
}
