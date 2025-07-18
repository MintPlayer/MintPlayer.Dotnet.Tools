using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

public partial class Context
{
    public partial class Entities
    {
        [AutoValueComparer]
        public partial class SportsCar : Car
        {
            public int Horsepower { get; set; } // in HP
            public bool IsConvertible { get; set; }
        }
    }
}