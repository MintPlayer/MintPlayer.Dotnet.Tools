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

            // Uncomment to see MINT001: a Roslyn symbol in a model pins the compilation between runs.
            // It stays commented out so that the solution builds.
            //public INamedTypeSymbol Symbol { get; set; }

            public string this[int index] => string.Empty;
        }
    }
}