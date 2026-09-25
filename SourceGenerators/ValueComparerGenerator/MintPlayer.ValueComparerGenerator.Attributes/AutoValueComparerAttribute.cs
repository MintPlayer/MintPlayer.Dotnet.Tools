namespace MintPlayer.ValueComparerGenerator.Attributes;

/// <summary>
/// Generates <see cref="IEquatable{T}"/>, <c>Equals(object)</c> and <c>GetHashCode()</c> on the type that this
/// attribute is applied to, comparing its properties by value. Every type deriving from it gets them as well.
/// </summary>
/// <remarks>
/// The type must be <c>partial</c>, and so must every type deriving from it and every containing type.
/// Works on classes (sealed, non-sealed, abstract), records, structs and record structs.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public class AutoValueComparerAttribute : Attribute
{
}
