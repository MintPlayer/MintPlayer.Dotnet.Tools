# Mapper-generator
This library contains source-generators that automatically generates mapper extension-methods for you.

## Getting started
You need to install both [`MintPlayer.Mapper`](https://nuget.org/packages/MintPlayer.Mapper) and [`MintPlayer.Mapper.Attributes`](https://nuget.org/packages/MintPlayer.Mapper.Attributes) packages in your project.

## Basic mapping
This demo shows how you can:
- Rename properties
- Change data-types
- Use the generated extension methods

You can mark a class with `[GenerateMapper(typeof(TargetType))]` to generate a mapper to another type.

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
    
    // Property with a state (plaintext, base64, ...)
    [MapperState(EKeyType.Plain)]
    public string Key { get; set; }
}

public enum EKeyType
{
    Plain,
    Base64
}

public class PersonDto
{
    public string Naam { get; set; }
    public AddressDto HoofdAdres { get; set; }
    public ContactInfoDto[] ContactGegevens { get; set; }
    public string Gewicht { get; set; }
    
    // Property with a state (plaintext, base64, ...)
    [MapperState(EKeyType.Base64)]
    public string Key { get; set; }
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

    // Conversion inbetween states, same property types
    [MapperConversion<EKeyType>(EKeyType.Plain, EKeyType.Base64)]
    public static string StringToBase64(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes);
    }

    [MapperConversion<EKeyType>(EKeyType.Base64, EKeyType.Plain)]
    public static string Base64ToString(string input)
    {
        var bytes = Convert.FromBase64String(input);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
```

Usage

```csharp
var person = new Person {
    Name = "John Doe",
    Age = 30,
    Address = new Address {
        Street = "123 Main St",
        City = "Anytown"
    },
    ContactInfos = new List<ContactInfo> {
        new ContactInfo { Type = "Email", Value = "info@example.com" },
        new ContactInfo { Type = "Phone", Value = "123-456-7890" }
    },
    Notes = new List<string> { "Note 1", "Note 2" },
    Weight = 70.5,
    Password = "QWJjMTIzIQ==",
};
var dto = person.MapToPersonDto();
var entity = dto.MapToPerson();
Debugger.Break();
```

## Example
A more complete example is available [here](https://github.com/PieterjanDeClippel/MapperDemo).

## Assembly-Level Usage

If you cannot apply `[GenerateMapper]` directly on a type (e.g. third-party classes), you can also apply it on the assembly:

```csharp
[assembly: GenerateMapper(typeof(SourceType), typeof(TargetType))]
```

This will generate a mapper between the given types at the assembly level.

## Property States

In some cases, two properties may share the same type (e.g. both are `string`) but represent different formats.  
Normally, these properties would be assigned directly without passing through a conversion.

You can explicitly mark such properties with a **`[MapperState("statename")]`** attribute.  
This ensures that the mapping will still go through the `ConvertProperty<>` method and apply the appropriate `MapperConversion`.

```csharp
public enum EKeyType
{
    Plain,
    Base64
}

public class PlainKeyDto
{
    [MapperState(EKeyType.Plain)]
    public string Key { get; set; }
}

public class Base64KeyDto
{
    [MapperState(EKeyType.Base64)]
    public string Key { get; set; }
}

public static class Conversions
{
    [MapperConversion(EKeyType.Plain, EKeyType.Base64)]
    public static string ToBase64(string plain, EKeyType inType, EKeyType outType)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

    [MapperConversion(EKeyType.Base64, EKeyType.Plain)]
    public static string FromBase64(string base64, EKeyType inType, EKeyType outType)
        => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
}
```

In this example:

- Both properties are `string`
- Without `[MapperState]`, the mapper would assign `Key` directly
- With `[MapperState]`, the mapper forces a conversion:
  - `plain → base64` calls `ToBase64`
  - `base64 → plain` calls `FromBase64`

This makes it possible to handle format-specific conversions even when the property types are identical.

## Complete example

```csharp
using MintPlayer.Mapper.Attributes;
using System.Diagnostics;
using MapperDemo;


var person = new Person
{
    Name = "John Doe",
    MainAddress = new Address
    {
        Street = "123 Main St",
        City = "Anytown",
        Country = "USA",
        Number = "A1",
        PostalCode = "12345",
    },
    ContactInfos = [
        new ContactInfo { Type = "Email", Value = "info@example.com" },
        new ContactInfo { Type = "Phone", Value = "123-456-7890" }
    ],
    Notes = ["Note 1", "Note 2"],
    Weight = 70.5,
    Key = "QWJjMTIzIQ==",
};
var dto = person.MapToPersonDto();
var entity = dto.MapToPerson();
Debugger.Break();

namespace MapperDemo
{
    [GenerateMapper(typeof(PersonDto))]
    public class Person
    {
        [MapperAlias(nameof(PersonDto.Naam))]
        public string Name { get; set; }

        [MapperAlias(nameof(PersonDto.HoofdAdres))]
        public Address MainAddress { get; set; }

        [MapperAlias(nameof(PersonDto.Notities))]
        public string[] Notes { get; set; }

        [MapperAlias(nameof(PersonDto.ContactGegevens))]
        public ContactInfo[] ContactInfos { get; set; }

        [MapperAlias(nameof(PersonDto.Gewicht))]
        public double Weight { get; set; }

        // Property with a state (plaintext, base64, ...)
        [MapperState<EKeyType>(EKeyType.Plain)]
        public string Key { get; set; }
    }

    [GenerateMapper(typeof(AddressDto))]
    public class Address
    {
        [MapperAlias(nameof(AddressDto.Straat))]
        public string Street { get; set; }
        [MapperAlias(nameof(AddressDto.Nummer))]
        public string Number { get; set; }
        [MapperAlias(nameof(AddressDto.Gemeente))]
        public string City { get; set; }
        [MapperAlias(nameof(AddressDto.Postcode))]
        public string PostalCode { get; set; }
        [MapperAlias(nameof(AddressDto.Land))]
        public string Country { get; set; }
    }

    [GenerateMapper(typeof(ContactInfoDto))]
    public class ContactInfo
    {
        [MapperAlias(nameof(ContactInfoDto.Type))]
        public string Type { get; set; }
        [MapperAlias(nameof(ContactInfoDto.Waarde))]
        public string Value { get; set; }
    }

    public class PersonDto
    {
        public string Naam { get; set; }
        public AddressDto HoofdAdres { get; set; }
        public string[] Notities { get; set; }
        public ContactInfoDto[] ContactGegevens { get; set; }
        public string Gewicht { get; set; }

        // Property with a state (plaintext, base64, ...)
        [MapperState<EKeyType>(EKeyType.Base64)]
        public string Key { get; set; }
    }

    public class AddressDto
    {
        public string Straat { get; set; }
        public string Nummer { get; set; }
        public string Gemeente { get; set; }
        public string Postcode { get; set; }
        public string Land { get; set; }
    }

    public class ContactInfoDto
    {
        public string Type { get; set; }
        public string Waarde { get; set; }
    }

    public enum EKeyType
    {
        Plain,
        Base64
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

        // Conversion inbetween states, same property types
        [MapperConversion<EKeyType>(EKeyType.Plain, EKeyType.Base64)]
        public static string StringToBase64(string input)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            return Convert.ToBase64String(bytes);
        }

        [MapperConversion<EKeyType>(EKeyType.Base64, EKeyType.Plain)]
        public static string Base64ToString(string input)
        {
            var bytes = Convert.FromBase64String(input);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
```
