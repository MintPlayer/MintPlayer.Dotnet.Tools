using MintPlayer.SourceGenerators.Tools.Polyfills;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.ValueComparerGenerator.Models;

public class TypeCandidateValueComparer : ValueComparer<TypeCandidate>
{
    protected override bool AreEqual(TypeCandidate x, TypeCandidate y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Type, y.Type)) return false;
        if (!IsEquals(x.IsPartial, y.IsPartial)) return false;
        if (!IsEquals(x.IsAbstract, y.IsAbstract)) return false;
        if (!IsEquals(x.IsInternal, y.IsInternal)) return false;
        if (!IsEquals(x.HasAttribute, y.HasAttribute)) return false;
        if (!IsEquals(x.BaseType, y.BaseType)) return false;
        if (!IsEquals(x.PathSpec, y.PathSpec)) return false;
        if (!IsEquals(x.Properties, y.Properties)) return false;
        if (!IsEquals(x.AllProperties, y.AllProperties)) return false;
        if (!IsEquals(x.HasCodeAnalysisReference, y.HasCodeAnalysisReference)) return false;

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, TypeCandidate? obj)
    {
        AddHash(ref h, obj?.Name);
        AddHash(ref h, obj?.Type);
        AddHash(ref h, obj?.IsPartial);
        AddHash(ref h, obj?.IsAbstract);
        AddHash(ref h, obj?.IsInternal);
        AddHash(ref h, obj?.HasAttribute);
        AddHash(ref h, obj?.BaseType);
        AddHash(ref h, obj?.PathSpec);
        AddHash(ref h, obj?.Properties);
        AddHash(ref h, obj?.AllProperties);
        AddHash(ref h, obj?.HasCodeAnalysisReference);
    }
}
