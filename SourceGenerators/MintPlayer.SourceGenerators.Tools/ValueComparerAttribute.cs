using MintPlayer.SourceGenerators.Tools.Extensions;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System;

namespace MintPlayer.SourceGenerators.Tools;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ValueComparerAttribute : Attribute
{
    public Type ComparerType { get; }

    public ValueComparerAttribute(Type comparerType)
    {
        if (!comparerType.IsDerivedFrom(typeof(ValueComparer<>)))
            throw new ArgumentException($"Argument 'comparerType' must be derived type from ValueComparer<>", nameof(comparerType));

        ComparerType = comparerType;
    }
}
