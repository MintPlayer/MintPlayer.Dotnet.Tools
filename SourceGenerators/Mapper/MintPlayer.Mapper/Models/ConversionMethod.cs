using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class ConversionMethod
{
    public string? MethodName { get; internal set; }
    public string? SourceType { get; internal set; }
    public string? SourceTypeName { get; internal set; }
    public string? DestinationType { get; internal set; }
    public string? DestinationTypeName { get; internal set; }
}
