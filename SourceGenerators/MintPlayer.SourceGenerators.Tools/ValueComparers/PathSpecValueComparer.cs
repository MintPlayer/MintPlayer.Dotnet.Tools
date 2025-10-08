using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

public class PathSpecValueComparer : ValueComparer<Extensions.PathSpec>
{
    protected override bool AreEqual(Extensions.PathSpec x, Extensions.PathSpec y)
    {
        if (!IsEquals(x.ContainingNamespace, y.ContainingNamespace)) return false;
        if (!IsEquals(x.Parents, y.Parents)) return false;
        return true;
    }
}

public class PathSpecElementValueComparer : ValueComparer<Extensions.PathSpecElement>
{
    protected override bool AreEqual(Extensions.PathSpecElement x, Extensions.PathSpecElement y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Type, y.Type)) return false;
        if (!IsEquals(x.IsPartial, y.IsPartial)) return false;
        return true;
    }
}
