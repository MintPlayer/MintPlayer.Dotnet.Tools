using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

/// <summary>Tuple key that compares two symbols with SymbolEqualityComparer.Default.</summary>
internal readonly struct SymbolPair : IEquatable<SymbolPair>
{
    public readonly ITypeSymbol Source;
    public readonly ITypeSymbol Destination;
    public SymbolPair(ITypeSymbol source, ITypeSymbol destination)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Destination = destination ?? throw new ArgumentNullException(nameof(destination));
    }

    public bool Equals(SymbolPair other) =>
        SymbolEqualityComparer.Default.Equals(Source, other.Source) &&
        SymbolEqualityComparer.Default.Equals(Destination, other.Destination);

    public override bool Equals(object? obj) => obj is SymbolPair sp && Equals(sp);

    public override int GetHashCode()
    {
        unchecked
        {
            var h1 = SymbolEqualityComparer.Default.GetHashCode(Source);
            var h2 = SymbolEqualityComparer.Default.GetHashCode(Destination);
            return (h1 * 397) ^ h2;
        }
    }
}