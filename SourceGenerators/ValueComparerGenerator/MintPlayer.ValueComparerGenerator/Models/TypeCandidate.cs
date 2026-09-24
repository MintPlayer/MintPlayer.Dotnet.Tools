using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

/// <summary>
/// What the syntax transform knows about one class: enough to decide whether it gets a comparer, and
/// to build its <see cref="ClassDeclaration"/> or its entry in a <see cref="TypeTreeDeclaration"/>.
/// </summary>
/// <remarks>
/// This used to be an anonymous type. Its compiler-generated <c>Equals</c> compares each member with
/// its default comparer, so the arrays, the <see cref="BaseType"/> and the <see cref="PathSpec"/> all
/// compared by reference, and every class in an edited file counted as changed. It also carried the
/// class's line-based location, which nothing read and which moved on every edit above the class.
/// </remarks>
[ValueComparer(typeof(TypeCandidateValueComparer))]
public class TypeCandidate
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsPartial { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsInternal { get; set; }
    public bool HasAttribute { get; set; }
    public BaseType? BaseType { get; set; }
    public PathSpec? PathSpec { get; set; }
    public PropertyDeclaration[] Properties { get; set; } = [];
    public PropertyDeclaration[] AllProperties { get; set; } = [];
    public bool HasCodeAnalysisReference { get; set; }

    public override string ToString() => Type;
}
