using MintPlayer.ValueComparerGenerator.Attributes;

[assembly: GenerateJoinMethods(10)]
namespace ValueComparerDebugging.Models;

public partial class Context
{
    public partial class Models
    {
        [AutoValueComparer]
        public partial class Address
        {
            public string Street { get; set; } = string.Empty;
            public int Number { get; set; }
            public string City { get; set; } = string.Empty;
            public int PostalCode { get; set; }
        }
    }
}