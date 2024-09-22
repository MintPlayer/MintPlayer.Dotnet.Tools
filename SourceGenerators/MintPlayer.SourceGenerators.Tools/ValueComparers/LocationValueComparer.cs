using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal class LocationValueComparer : ValueComparer<Location>
{
    protected override bool AreEqual(Location x, Location y)
    {
        return x.Equals(y);
    }
}