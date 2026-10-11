using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[GenerateEquality]
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
    /// <summary>
    /// The accessibility as seen from the generated <c>MapperExtensions</c> class: public only when the type and every
    /// type it is nested in are public.
    /// </summary>
    public int DeclaredTypeAccessibility { get; set; }
    public bool DeclaredTypeIsValueType { get; set; }

    /// <summary>The number of base types, so the generated type switch can test a derived type before its base.</summary>
    public int DeclaredTypeDepth { get; set; }
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
    public bool MappingTypeIsValueType { get; set; }
    public int MappingTypeDepth { get; set; }
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

    // An attribute that cannot be mapped gives no model at all, only diagnostics (see MapperGenerator), so
    // nothing downstream has to filter out a half-filled model.
}