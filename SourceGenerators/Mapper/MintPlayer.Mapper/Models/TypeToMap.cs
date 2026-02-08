using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class TypeToMap
{
    /// <summary>
    /// Fully-qualified name
    /// </summary>
    public string DeclaredType { get; set; }

    /// <summary>
    /// Short name
    /// </summary>
    public string DeclaredTypeName { get; set; }
    public int DeclaredTypeAccessibility { get; set; }
    public bool DeclaredTypeHasParameterlessConstructor { get; set; }
    public ConstructorParameterInfo[] DeclaredTypePrimaryConstructorParameters { get; set; } = [];
    public PropertyDeclaration[] DeclaredProperties { get; set; } = [];

    /// <summary>
    /// Fully-qualified name
    /// </summary>
    public string MappingType { get; set; }

    /// <summary>
    /// Short name
    /// </summary>
    public string MappingTypeName { get; set; }
    public int MappingTypeAccessibility { get; set; }
    public bool MappingTypeHasParameterlessConstructor { get; set; }
    public ConstructorParameterInfo[] MappingTypePrimaryConstructorParameters { get; set; } = [];
    public PropertyDeclaration[] MappingProperties { get; set; } = [];

    public string PreferredMappingMethodName { get; set; }
    public string PreferredDeclaredMethodName { get; set; }
    public string DestinationNamespace { get; set; }

    /// <summary>
    /// Indicates whether both types are decorated with the <see cref="Attributes.GenerateMapperAttribute"/>
    /// </summary>
    public bool AreBothDecorated { get; set; }

    public EAppliedOn AppliedOn { get; set; }

    public bool HasError { get; set; }

    public LocationKey? Location { get; set; }
    //public INamedTypeSymbol Symbol { get; internal set; }
}

[Flags]
public enum EAppliedOn
{
    None = 0,
    Class = 1,
    Assembly = 2,
}