using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

[AutoValueComparer]
public partial class RaceBike : Bike
{
    public int TopSpeed { get; set; } // in km/h
    public int Weight { get; set; } // in kg
    public bool HasAerodynamicWheels { get; set; }
    public bool HasDropHandlebars { get; set; }
    public bool HasCliplessPedals { get; set; }
    public string GearRatio { get; set; } = string.Empty;
}
