namespace MintPlayer.ValueComparerGenerator.Attributes;

/// <summary>
/// Indicates that the property should be ignored by the value comparer.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ComparerIgnoreAttribute : Attribute
{
}
