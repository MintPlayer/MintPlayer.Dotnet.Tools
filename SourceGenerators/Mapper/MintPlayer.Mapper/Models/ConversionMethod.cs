using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[GenerateEquality]
public partial class ConversionMethod
{
    /// <summary>The fully-qualified name of the static class declaring the method, with <c>global::</c>.</summary>
    public string? ContainingType { get; internal set; }
    public string? MethodName { get; internal set; }
    public int MethodParameterCount { get; internal set; }
    public string? SourceType { get; internal set; }
    public string? SourceTypeName { get; internal set; }
    public int? SourceState { get; internal set; }
    public string? DestinationType { get; internal set; }
    public string? DestinationTypeName { get; internal set; }
    public bool SourceTypeNullable { get; internal set; }
    public int? DestinationState { get; internal set; }

    public string? StateType { get; set; }
    public string? StateTypeName { get; set; }
}
