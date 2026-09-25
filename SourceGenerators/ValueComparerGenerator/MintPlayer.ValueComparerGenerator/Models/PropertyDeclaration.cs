using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

/// <summary>One property of a model, with its comparison already chosen from its type symbol.</summary>
/// <remarks>
/// <see cref="EqualsExpression"/> uses <c>$L$</c> and <c>$R$</c> for the two values, <see cref="HashExpression"/>
/// uses <c>$V$</c>. The producer only substitutes them, so it never needs a symbol.
/// </remarks>
public sealed class PropertyDeclaration : IEquatable<PropertyDeclaration>
{
    public string Name { get; set; } = string.Empty;
    public string EqualsExpression { get; set; } = string.Empty;
    public string HashExpression { get; set; } = string.Empty;
    public bool HasEqualityIgnore { get; set; }

    public bool Equals(PropertyDeclaration? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(EqualsExpression, other.EqualsExpression, StringComparison.Ordinal)
            && string.Equals(HashExpression, other.HashExpression, StringComparison.Ordinal)
            && HasEqualityIgnore == other.HasEqualityIgnore;
    }

    public override bool Equals(object? obj) => Equals(obj as PropertyDeclaration);

    public override int GetHashCode()
    {
        var h = 17;
        h = ValueEquality.Combine(h, StringComparer.Ordinal.GetHashCode(Name));
        h = ValueEquality.Combine(h, StringComparer.Ordinal.GetHashCode(EqualsExpression));
        h = ValueEquality.Combine(h, StringComparer.Ordinal.GetHashCode(HashExpression));
        h = ValueEquality.Combine(h, HasEqualityIgnore ? 1 : 0);
        return h;
    }

    public override string ToString() => Name;
}

/// <summary>A <c>private static readonly</c> comparer field that a nested collection comparison needs.</summary>
public sealed class ComparerField : IEquatable<ComparerField>
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Initializer { get; set; } = string.Empty;

    public bool Equals(ComparerField? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Type, other.Type, StringComparison.Ordinal)
            && string.Equals(Initializer, other.Initializer, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as ComparerField);

    public override int GetHashCode()
    {
        var h = 17;
        h = ValueEquality.Combine(h, StringComparer.Ordinal.GetHashCode(Name));
        h = ValueEquality.Combine(h, StringComparer.Ordinal.GetHashCode(Type));
        h = ValueEquality.Combine(h, StringComparer.Ordinal.GetHashCode(Initializer));
        return h;
    }

    public override string ToString() => Name;
}
