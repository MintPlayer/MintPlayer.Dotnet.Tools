using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[GenerateEquality]
public partial class ClassDeclaration
{
    /// <summary>The class name as written, without <c>@</c>.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The containing namespace of a top-level class (empty for the global namespace), or null for a nested class.
    /// Only a top-level class can collide with the generated <c>ClassNames</c> / <c>ClassNameList</c> types.
    /// </summary>
    public string? TopLevelNamespace { get; set; }
}
