namespace MintPlayer.Assertions;

/// <summary>
/// Opts a type into source-generated equivalency member accessors, even when no
/// BeEquivalentTo call site with that static type is visible to the generator (e.g. the type is
/// only ever compared through a base type or interface, or from another assembly).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class AssertEquivalencyAttribute : Attribute
{
}
