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
}
