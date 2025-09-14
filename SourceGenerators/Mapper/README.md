# Mapper-generator
This library contains source-generators that automatically generates mapper extension-methods for you.

## Getting started
You need to install both [`MintPlayer.Mapper`](https://nuget.org/packages/MintPlayer.Mapper) and [`MintPlayer.Mapper.Attributes`](https://nuget.org/packages/MintPlayer.Mapper.Attributes) packages in your project.

## Basic mapping
This demo shows how you can:
- Rename properties
- Change data-types
- Use the generated extension methods

```csharp
[GenerateMapper(typeof(PersonDto))]
public class Person
{
    [MapperAlias(nameof(PersonDto.Naam))]
    public string Name { get; set; }

    [MapperAlias(nameof(PersonDto.HoofdAdres))]
    public Address MainAddress { get; set; }

    [MapperAlias(nameof(PersonDto.ContactGegevens))]
    public ContactInfo[] ContactInfos { get; set; }

    [MapperAlias(nameof(PersonDto.Gewicht))]
    public double Weight { get; set; }
}

public class PersonDto
{
    public string Naam { get; set; }
    public AddressDto HoofdAdres { get; set; }
    public ContactInfoDto[] ContactGegevens { get; set; }
    public string Gewicht { get; set; }
}

public static class Conversions
{
    [MapperConversion]
    public static double StringToDouble(string input)
    {
        if (double.TryParse(input, out double result))
            return result;
        return 0;
    }

    [MapperConversion]
    public static string DoubleToString(double input)
    {
        return input.ToString();
    }
}
```

Usage

```csharp
var person = new Person {
    Name = "John Doe",
    MainAddress = new Address {
        ...
    },
    ContactInfos = [
        ...
    ],
    Weight = 70.5,
};
var personDto = person.MapToPersonDto();
```

## Example
A more complete example is available [here](https://github.com/PieterjanDeClippel/MapperDemo).