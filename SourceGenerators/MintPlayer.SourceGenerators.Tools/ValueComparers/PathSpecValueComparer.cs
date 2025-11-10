using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

public class PathSpecValueComparer : ValueComparer<PathSpec>
{
    protected override bool AreEqual(PathSpec x, PathSpec y)
    {
        if (!IsEquals(x.ContainingNamespace, y.ContainingNamespace)) return false;
        if (!IsEquals(x.Parents, y.Parents)) return false;
        return true;
    }
}

public class PathSpecElementValueComparer : ValueComparer<PathSpecElement>
{
    protected override bool AreEqual(PathSpecElement x, PathSpecElement y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Type, y.Type)) return false;
        if (!IsEquals(x.IsPartial, y.IsPartial)) return false;
        return true;
    }
}
