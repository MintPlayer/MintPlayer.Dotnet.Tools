using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

[AutoValueComparer]
public partial class SportsCar : Car
{
    public int Horsepower { get; set; } // in HP
    public bool IsConvertible { get; set; }
}
