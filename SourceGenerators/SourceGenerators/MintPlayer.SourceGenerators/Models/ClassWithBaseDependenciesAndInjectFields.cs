using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class ClassWithBaseDependenciesAndInjectFields
{
    public string FileName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string? ClassNamespace { get; set; } = string.Empty;
    public PathSpec? PathSpec { get; set; }
    public IList<InjectField> BaseDependencies { get; set; } = [];
    public IList<InjectField> InjectFields { get; set; } = [];
    public string? PostConstructMethodName { get; set; }
    public IList<PostConstructDiagnostic> Diagnostics { get; set; } = [];

    /// <summary>
    /// Generic type parameters for the class, e.g., "&lt;TUser, TKey&gt;"
    /// </summary>
    public string? GenericTypeParameters { get; set; }

    /// <summary>
    /// Generic type constraints for the class, e.g., "where TUser : IdentityUser&lt;TKey&gt; where TKey : IEquatable&lt;TKey&gt;"
    /// </summary>
    public string? GenericConstraints { get; set; }
}
