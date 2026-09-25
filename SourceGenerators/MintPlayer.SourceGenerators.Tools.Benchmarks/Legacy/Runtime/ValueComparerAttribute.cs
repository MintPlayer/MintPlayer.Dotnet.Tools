// Vendored from master:SourceGenerators/MintPlayer.SourceGenerators.Tools/ValueComparerAttribute.cs for
// benchmark B1. The constructor's IsDerivedFrom check called a Tools extension method that is not vendored;
// it runs once per type (the registry caches the result), so it has no bearing on per-call cost.
namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ValueComparerAttribute : Attribute
{
    public Type ComparerType { get; }

    public ValueComparerAttribute(Type comparerType)
    {
        ComparerType = comparerType;
    }
}
