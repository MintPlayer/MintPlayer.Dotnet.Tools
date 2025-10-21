using Microsoft.CodeAnalysis;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Models;

public partial class Context
{
    public partial class Models
    {
        [AutoValueComparer]
        public partial class Person
        {
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public List<Address> Addresses { get; set; } = [];

            // Should get an error for this property
            public INamedTypeSymbol Symbol { get; set; }

            public string this[int index] => string.Empty;
        }
    }
}