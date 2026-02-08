using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class ConstructorParameterInfo
{
    public string ParameterName { get; set; }
    public string CorrespondingPropertyName { get; set; }
    public string ParameterType { get; set; }
}
