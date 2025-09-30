using Microsoft.CodeAnalysis;
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
    public PropertyDeclaration[] DeclaredProperties { get; set; } = [];

    /// <summary>
    /// Fully-qualified name
    /// </summary>
    public string MappingType { get; set; }

    /// <summary>
    /// Short name
    /// </summary>
    public string MappingTypeName { get; set; }
    public PropertyDeclaration[] MappingProperties { get; set; } = [];

    public string PreferredMappingMethodName { get; set; }
    public string PreferredDeclaredMethodName { get; set; }
    public string DestinationNamespace { get; set; }

    /// <summary>
    /// Indicates whether both types are decorated with the <see cref="Attributes.GenerateMapperAttribute"/>
    /// </summary>
    public bool AreBothDecorated { get; set; }

    /// <summary>
    /// Indicates that the source type has an indexer
    /// </summary>
    public bool SourceTypeHasIndexer { get; set; }

    /// <summary>
    /// Indicates that the destination type has an indexer
    /// </summary>
    public bool DestinationTypeHasIndexer { get; set; }

    public EAppliedOn AppliedOn { get; set; }

    public bool HasError { get; set; }

    public Location? Location { get; set; }
}

[Flags]
public enum EAppliedOn
{
    None = 0,
    Class = 1,
    Assembly = 2,
}