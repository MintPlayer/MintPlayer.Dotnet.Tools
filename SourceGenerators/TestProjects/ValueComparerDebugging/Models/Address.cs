using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Models;

[AutoValueComparer]
public class Address
{
    public string Street { get; set; } = string.Empty;
    public int Number { get; set; }
    public string City { get; set; } = string.Empty;
    public int PostalCode { get; set; }
}
