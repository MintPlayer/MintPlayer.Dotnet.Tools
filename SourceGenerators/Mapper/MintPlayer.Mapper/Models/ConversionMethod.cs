using Microsoft.CodeAnalysis;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class ConversionMethod
{
    public string? MethodName { get; internal set; }
    public string? SourceType { get; internal set; }
    public string? SourceTypeName { get; internal set; }
    public int? SourceState { get; internal set; }
    public string? DestinationType { get; internal set; }
    public string? DestinationTypeName { get; internal set; }
    public bool SourceTypeNullable { get; internal set; }
    public int? DestinationState { get; internal set; }
    public Location? AttributeLocation { get; internal set; }
}
