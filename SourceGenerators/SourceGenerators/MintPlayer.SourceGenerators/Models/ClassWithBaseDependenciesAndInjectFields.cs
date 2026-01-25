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
    public IList<ConfigField> ConfigFields { get; set; } = [];
    public IList<ConnectionStringField> ConnectionStringFields { get; set; } = [];
    public IList<OptionsField> OptionsFields { get; set; } = [];
    public string? PostConstructMethodName { get; set; }
    public IList<PostConstructDiagnostic> Diagnostics { get; set; } = [];
    public IList<ConfigDiagnostic> ConfigDiagnostics { get; set; } = [];

    /// <summary>
    /// Whether the class has an explicit [Inject] IConfiguration field.
    /// If true, the generator reuses that field; if false, it adds __configuration as a parameter.
    /// </summary>
    public bool HasExplicitIConfiguration { get; set; }

    /// <summary>
    /// The name of the IConfiguration field/property if explicitly injected, otherwise null.
    /// </summary>
    public string? ExplicitIConfigurationName { get; set; }

    /// <summary>
    /// Generic type parameters for the class, e.g., "&lt;TUser, TKey&gt;"
    /// </summary>
    public string? GenericTypeParameters { get; set; }

    /// <summary>
    /// Generic type constraints for the class, e.g., "where TUser : IdentityUser&lt;TKey&gt; where TKey : IEquatable&lt;TKey&gt;"
    /// </summary>
    public string? GenericConstraints { get; set; }
}
