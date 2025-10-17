//using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

public partial class Context
{
    public partial class Entities
    {
        // [AutoValueComparer]
        public partial class DirtBike : Bike
        {
            public bool HasKnobbyTires { get; set; }
            public int SuspensionTravel { get; set; } // in mm
            public string EngineType { get; set; } = string.Empty;
            public int Displacement { get; set; } // in cc
            public bool HasHeadlight { get; set; }
            public bool HasTaillight { get; set; }
            public bool HasTurnSignals { get; set; }
        }
    }
}