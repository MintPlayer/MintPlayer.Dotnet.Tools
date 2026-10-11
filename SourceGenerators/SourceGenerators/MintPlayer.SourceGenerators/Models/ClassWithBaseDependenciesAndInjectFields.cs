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

    /// <summary>
    /// The parameter passed to each slot of the base constructor, in the base constructor's order, matched by type.
    /// One parameter can fill several slots. Empty: no base call.
    /// </summary>
    public IList<string> BaseConstructorArguments { get; set; } = [];

    /// <summary>Each [Inject] and [Options] member with the parameter it is assigned from (several members can share one).</summary>
    public IList<MemberAssignment> Assignments { get; set; } = [];

    public IList<ConfigField> ConfigFields { get; set; } = [];
    public IList<ConnectionStringField> ConnectionStringFields { get; set; } = [];
    public string? PostConstructMethodName { get; set; }

    /// <summary>
    /// The IConfiguration parameter [Config] and [ConnectionString] members read from: an explicit [Inject]
    /// IConfiguration member's, a base's, or the auto-injected <c>__configuration</c>. Null without config members.
    /// </summary>
    public string? ConfigurationParameterName { get; set; }
}

/// <summary><c>this.{Member} = {Parameter};</c> in the generated constructor.</summary>
[GenerateEquality]
public partial class MemberAssignment
{
    public string Member { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
}
