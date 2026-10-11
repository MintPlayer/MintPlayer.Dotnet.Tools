using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>
/// One type that gets a generated constructor. Holds only values: no symbol, no syntax, no location and no
/// diagnostic descriptor, so it compares by value between runs. Diagnostics travel beside it in a
/// <see cref="Discovered{TModel}"/>.
/// </summary>
[GenerateEquality]
public partial class ClassWithBaseDependenciesAndInjectFields
{
    /// <summary>The namespace, the containing types and the type itself, re-declared through <c>OpenPathSpec</c>.</summary>
    public PathSpec? PathSpec { get; set; }

    /// <summary>The type's name as the constructor is named: escaped, without type parameters.</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>A generic type is named in <c>&lt;c&gt;</c> in the doc comment, because a cref would need <c>{T}</c>.</summary>
    public bool IsGeneric { get; set; }

    /// <summary>A struct or record struct: the constructor chains <c>: this()</c>, so every field is assigned (CS0171).</summary>
    public bool IsValueType { get; set; }

    /// <summary>
    /// <c>public</c>, unless a parameter type is less accessible than the type itself (CS0051); then the
    /// accessibility of that parameter type.
    /// </summary>
    public string ConstructorAccessibility { get; set; } = "public";

    /// <summary>
    /// The constructor's parameters in emitted order: required ones first, then the optional ones
    /// (<see cref="InjectField.IsNullable"/>), each defaulted. An optional parameter before a required one is CS1737.
    /// </summary>
    public IList<InjectField> Parameters { get; set; } = [];

    /// <summary>The argument names passed to <c>base(...)</c>, in the base constructor's order. Empty: no base call.</summary>
    public IList<string> BaseConstructorArguments { get; set; } = [];

    public IList<InjectField> InjectFields { get; set; } = [];
    public IList<ConfigField> ConfigFields { get; set; } = [];
    public IList<ConnectionStringField> ConnectionStringFields { get; set; } = [];
    public IList<OptionsField> OptionsFields { get; set; } = [];
    public string? PostConstructMethodName { get; set; }

    /// <summary>
    /// Whether the class has an explicit [Inject] IConfiguration field.
    /// If true, the generator reuses that field; if false, it adds __configuration as a parameter.
    /// </summary>
    public bool HasExplicitIConfiguration { get; set; }

    /// <summary>
    /// The name of the IConfiguration field/property if explicitly injected, otherwise null.
    /// </summary>
    public string? ExplicitIConfigurationName { get; set; }
}
