using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.ValueComparerGenerator.Models;

[ValueComparer(typeof(JoinMethodInfoComparer))]
public partial class JoinMethodInfo
{
    public uint? NumberOfJoinMethods { get; set; }
    public bool HasCodeAnalysisReference { get; set; }
}
