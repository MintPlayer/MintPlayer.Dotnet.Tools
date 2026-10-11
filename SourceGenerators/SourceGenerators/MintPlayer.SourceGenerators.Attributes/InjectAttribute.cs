namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Marks a field, or a get-only or init-only property, to be assigned from a constructor parameter of a generated
/// constructor.
/// </summary>
/// <remarks>
/// The containing type may be a <c>class</c>, <c>record</c>, <c>struct</c> or <c>record struct</c>, at top level or
/// nested. It, and every type containing it, must be <c>partial</c> (INJECT006). A type with a parameter list (a
/// positional record or a primary constructor) cannot get a generated constructor (INJECT007).
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class InjectAttribute : Attribute
{
}
