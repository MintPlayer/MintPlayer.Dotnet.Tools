using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Models;

[AutoValueComparer]
public partial class Person
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public List<Address> Addresses { get; set; } = [];
}
