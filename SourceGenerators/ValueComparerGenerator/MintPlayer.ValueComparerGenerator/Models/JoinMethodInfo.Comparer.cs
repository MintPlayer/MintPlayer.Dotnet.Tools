using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.ValueComparerGenerator.Models;

public class JoinMethodInfoComparer : ValueComparer<JoinMethodInfo>
{
    protected override bool AreEqual(JoinMethodInfo x, JoinMethodInfo y)
    {
        if (!IsEquals(x.NumberOfJoinMethods, y.NumberOfJoinMethods)) return false;
        if (!IsEquals(x.HasCodeAnalysisReference, y.HasCodeAnalysisReference)) return false;

        return true;
    }
}
