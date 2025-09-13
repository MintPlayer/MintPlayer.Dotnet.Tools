// See https://aka.ms/new-console-template for more information
using MintPlayer.Mapper.Attributes;

Console.WriteLine("Hello, World!");

public static class Conversions
{
    [MapperConversion]
    public static int? StringToNullableInt(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        if (int.TryParse(input, out int result))
            return result;
        return null;
    }

    [MapperConversion]
    public static string? NullableIntToString(int? input)
    {
        return input?.ToString();
    }
}

public static class Test
{
    public static TDest ConvertProperty<TSource, TDest>(TSource source)
    {
        return (typeof(TSource), typeof(TDest)) switch
        {
            (Type t1, Type t2) when t1 == typeof(string) && t2 == typeof(int?) => (TDest)(object)Conversions.StringToNullableInt((string?)(object)source),
            (Type t1, Type t2) when t1 == typeof(int?) && t2 == typeof(string) => (TDest)(object)Conversions.NullableIntToString((int?)(object)source),
            _ => throw new NotSupportedException($"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported."),
        };
    }
}

[GenerateMapper(typeof(PersonDto))]
public class Person
{
    public string? Name { get; set; }
    public int? Age { get; set; }
    public Address Address { get; set; }
    public List<ContactInfo> ContactInfos { get; set; } = [];
    public List<string> Notes { get; set; }
    public double Weight { get; set; }
}

public class PersonDto
{
    [MapperAlias(nameof(Person.Name))]
    public string? Naam { get; set; }

    [MapperAlias(nameof(Person.Age))]
    public int? Leeftijd { get; set; }

    [MapperAlias(nameof(Person.Address))]
    public AddressDto Adres { get; set; }

    [MapperAlias(nameof(Person.ContactInfos))]
    public List<ContactInfoDto> Contactgegevens { get; set; } = [];

    [MapperAlias(nameof(Person.Notes))]
    public List<string> Notities { get; set; }

    [MapperAlias(nameof(Person.Weight))]
    public string Gewicht { get; set; }
}

[GenerateMapper(typeof(AddressDto))]
public class Address
{
    public string? Street { get; set; }
    public string? City { get; set; }
}

public class AddressDto
{
    [MapperAlias(nameof(Address.Street))]
    public string? Straatnaam { get; set; }

    [MapperAlias(nameof(Address.City))]
    public string? Stad { get; set; }
}

public class ContactInfo
{
    public string Type { get; set; }
    public string Value { get; set; }
}

[GenerateMapper(typeof(ContactInfo))]
public class ContactInfoDto
{
    [MapperAlias(nameof(ContactInfo.Type))]
    public string Soort { get; set; }

    [MapperAlias(nameof(ContactInfo.Value))]
    public string Waarde { get; set; }
}