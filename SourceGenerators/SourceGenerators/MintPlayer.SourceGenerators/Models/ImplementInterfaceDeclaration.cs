using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>
/// One type with <c>[ImplementInterface]</c>, merged over all its attributes and partial parts: the interfaces to add
/// to its base list and the members to declare, both already rendered, so the producer only writes them.
/// </summary>
[GenerateEquality]
public partial class ImplementInterfaceDeclaration
{
    /// <summary>The type, re-declared by <c>OpenPathSpec(includeTarget: true)</c>.</summary>
    public PathSpec? PathSpec { get; set; }

    /// <summary>The fully qualified interfaces the type does not implement yet, in attribute order.</summary>
    public string[] BaseList { get; set; } = [];

    /// <summary>
    /// The members, one declaration each. A member whose types are nullable-oblivious is wrapped in
    /// <c>#nullable disable annotations</c> / <c>#nullable restore</c> lines, so an entry can span three lines.
    /// </summary>
    public string[] Members { get; set; } = [];
}
