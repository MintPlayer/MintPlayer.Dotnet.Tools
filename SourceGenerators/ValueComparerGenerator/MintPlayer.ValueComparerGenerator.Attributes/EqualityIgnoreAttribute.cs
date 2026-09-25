namespace MintPlayer.ValueComparerGenerator.Attributes;

/// <summary>
/// Leaves the property out of the generated <c>Equals</c> and <c>GetHashCode</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class EqualityIgnoreAttribute : Attribute
{
}
