namespace MintPlayer.ValueComparerGenerator.Attributes;

/// <summary>
/// Generates a value comparer for the class that this attribute is applied to.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class AutoValueComparerAttribute : Attribute
{
}
