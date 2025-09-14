# Value-comparer generator
This library contains source-generators to simplify writing your own source-generators.

## Getting started
You need to install both [`MintPlayer.ValueComparerGenerator`](https://nuget.org/packages/MintPlayer.ValueComparerGenerator) and [`MintPlayer.ValueComparerGenerator`](https://nuget.org/packages/MintPlayer.ValueComparerGenerator.Attributes) packages in your project.

## Example
Note that the class (and parent classes) must be partial.

```csharp
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
        }

        [AutoValueComparer]
        public partial class Address
        {
            public string Street { get; set; } = string.Empty;
            public int Number { get; set; }
            public string City { get; set; } = string.Empty;
            public int PostalCode { get; set; }

            [ComparerIgnore]
            public string Text { get; set; }
        }
    }
}
```