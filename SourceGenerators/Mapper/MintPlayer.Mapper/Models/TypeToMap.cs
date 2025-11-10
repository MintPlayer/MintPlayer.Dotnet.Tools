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
    public PropertyDeclaration[] DeclaredProperties { get; set; } = [];

    /// <summary>
    /// Fully-qualified name
    /// </summary>
    public string MappingType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether map-like objects are represented as dictionaries during serialization
    /// and deserialization.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, map-like objects are handled as 
    /// <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> instances. When <see langword="false"/>
    /// .</remarks>
    public bool MapAsDictionary { get; set; }

    /// <summary>
    /// Short name
    /// </summary>
    public string MappingTypeName { get; set; }
    public int MappingTypeAccessibility { get; set; }
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
}

[Flags]
public enum EAppliedOn
{
    None = 0,
    Class = 1,
    Assembly = 2,
}