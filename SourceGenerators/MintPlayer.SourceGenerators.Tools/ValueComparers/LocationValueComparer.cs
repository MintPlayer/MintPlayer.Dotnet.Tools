using MintPlayer.SourceGenerators.Tools.Polyfills;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal class LocationKeyValueComparer : ValueComparer<LocationKey>
{
    // This used to be x.Equals(y). LocationKey is a class with no Equals override, so that was
    // reference equality, and every model holding a LocationKey compared unequal to its previous
    // run — the one type whose whole purpose is "a location that compares by value".
    protected override bool AreEqual(LocationKey x, LocationKey y)
        => string.Equals(x.FilePath, y.FilePath, StringComparison.Ordinal)
        && x.StartLine == y.StartLine
        && x.StartColumn == y.StartColumn
        && x.EndLine == y.EndLine
        && x.EndColumn == y.EndColumn;

    protected override void AddHash(ref HashCodeCompat h, LocationKey? obj)
    {
        if (obj is null) { h.Add(0); return; }
        h.Add(obj.FilePath);
        h.Add(obj.StartLine);
        h.Add(obj.StartColumn);
        h.Add(obj.EndLine);
        h.Add(obj.EndColumn);
    }
}
