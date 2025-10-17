//using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

public partial class Context
{
    public partial class Entities
    {
        // [AutoValueComparer]
        public partial class Motorcycle : Vehicle
        {
            public bool HasSidecar { get; set; }
        }
    }
}