using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class ClassesByNamespace
{
    public string? Namespace { get; set; }
    public ClassWithBaseDependenciesAndInjectFields[] Classes { get; set; } = [];
}
