using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal class LocationKeyValueComparer : ValueComparer<LocationKey>
{
    protected override bool AreEqual(LocationKey x, LocationKey y)
    {
        return x.Equals(y);
    }
}