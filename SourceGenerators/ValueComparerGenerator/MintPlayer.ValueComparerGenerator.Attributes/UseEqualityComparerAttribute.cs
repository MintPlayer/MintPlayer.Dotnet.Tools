namespace MintPlayer.ValueComparerGenerator.Attributes;

/// <summary>
/// Compares this property with <paramref name="comparerType"/> instead of the comparison the generator would choose
/// from the property's type.
/// </summary>
/// <param name="comparerType">
/// An <see cref="System.Collections.Generic.IEqualityComparer{T}"/> of the property's type, with either a static
/// <c>Instance</c> member or a public parameterless constructor. Anything else is reported as <c>MINT004</c>.
/// </param>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class UseEqualityComparerAttribute(Type comparerType) : Attribute
{
    /// <summary>The comparer type passed to the constructor.</summary>
    public Type ComparerType { get; } = comparerType;
}
