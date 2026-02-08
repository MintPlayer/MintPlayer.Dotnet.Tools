using MintPlayer.Mapper.Attributes;
using MapperDebugging;
using System.Diagnostics;

[assembly: GenerateMapper(typeof(Person), typeof(PersonDto), "Persoon")]
[assembly: GenerateMapper(typeof(ContactInfo), typeof(ContactInfoDto), "MapTo")]
[assembly: GenerateMapper(typeof(Product), typeof(ProductDto), "MapTo")]


var person = new Person
{
    Name = "John Doe",
    Age = 30,
    Address = new Address
    {
        Street = "123 Main St",
        City = "Anytown"
    },
    ContactInfos =
    [
        new ContactInfo { Type = "Email", Value = "info@example.com" },
        new ContactInfo { Type = "Phone", Value = "123-456-7890" }
    ],
    Notes = ["Note 1", "Note 2"],
    Weight = 70.5,
    Password = "QWJjMTIzIQ==",
};
var dto = person.MapToPersonDto();
var entity = dto.MapToPerson();

// Record with primary constructor (no parameterless ctor)
var product = new Product("Widget", 9.99m);
var productDto = product.MapToProductDto();
var productBack = productDto.MapToProduct();

// Record with primary constructor + extra settable property
var coord = new Coordinate(1.0, 2.0) { Label = "Origin" };
var coordDto = coord.MapToCoordinateDto();
var coordBack = coordDto.MapToCoordinate();

// Record without primary constructor (parameterless, init-only properties)
var tag = new Tag { Name = "Important", Color = "Red" };
var tagDto = tag.MapToTagDto();
var tagBack = tagDto.MapToTag();

Debugger.Break();



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

    [MapperConversion<EPasswordState>(EPasswordState.Plaintext, EPasswordState.Base64)]
    public static string StringToBase64(string input, EPasswordState inState, EPasswordState outState)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes);
    }

    // TODO: Allow multiple conversions through same method
    [MapperConversion<EPasswordState>(EPasswordState.Base64, EPasswordState.Plaintext)]
    public static string Base64ToString(string input, EPasswordState inState, EPasswordState outState)
    {
        var bytes = Convert.FromBase64String(input);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}

public enum EPasswordState
{
    Plaintext,
    Base64
}

//[GenerateMapper(typeof(PersonDto), "Persoon")]
public class Person
{
    [IgnoreMap]
    public string? Name { get; set; }
    public int? Age { get; set; }
    internal Address? Address { get; set; }
    public List<ContactInfo> ContactInfos { get; set; } = [];
    public List<string> Notes { get; set; }
    public double Weight { get; set; }

    [MapTo(nameof(Person.Password)), MapperState<EPasswordState>(EPasswordState.Plaintext)]
    public string Password { get; set; }

    // Read-only property
    public string Field1 { get; }

    // Init-only property
    public string Field2 { get; set; }
}

//[GenerateMapper(typeof(Person), typeof(PersonDto), "PersoonDto")]
//[GenerateMapper(typeof(Person), "PersoonDto")]
public class PersonDto
{
    [MapTo(nameof(Person.Name))]
    public string? Naam { get; set; }

    [MapTo(nameof(Person.Age))]
    public int? Leeftijd { get; set; }

    [MapTo(nameof(Person.Address))]
    internal AddressDto? Adres { get; set; }

    [MapTo(nameof(Person.ContactInfos))]
    public List<ContactInfoDto> Contactgegevens { get; set; } = [];

    [MapTo(nameof(Person.Notes))]
    public List<string> Notities { get; set; }

    [MapTo(nameof(Person.Weight))]
    public string Gewicht { get; set; }

    [MapTo(nameof(Person.Password)), MapperState<EPasswordState>(EPasswordState.Base64)]
    public string Password { get; set; }

    // Read-only property
    [MapTo(nameof(Person.Field1))]
    public string Veld1 { get; set; }

    // Init-only property
    [MapTo(nameof(Person.Field2))]
    public string Veld2 { get; init; }
}

[GenerateMapper(typeof(AddressDto))]
internal class Address
{
    public string? Street { get; set; }
    public string? City { get; set; }
}

internal class AddressDto
{
    [MapTo(nameof(Address.Street))]
    public string? Straatnaam { get; set; }

    [MapTo(nameof(Address.City))]
    public string? Stad { get; set; }
}

public class ContactInfo
{
    public string Type { get; set; }
    public string Value { get; set; }
}

//[GenerateMapper(typeof(ContactInfo))]
public class ContactInfoDto
{
    [MapTo(nameof(ContactInfo.Type))]
    public string Soort { get; set; }

    [MapTo(nameof(ContactInfo.Value))]
    public string Waarde { get; set; }
}

// --- Record test scenarios ---

// Scenario 1: Record with primary constructor (no parameterless ctor)
public record Product(string Name, decimal Price);

public class ProductDto
{
    [MapTo(nameof(Product.Name))]
    public string Name { get; set; }

    [MapTo(nameof(Product.Price))]
    public decimal Price { get; set; }
}

// Scenario 2: Record with primary ctor + extra settable property
[GenerateMapper(typeof(CoordinateDto))]
public record Coordinate(double X, double Y)
{
    public string? Label { get; set; }
}

public class CoordinateDto
{
    [MapTo(nameof(Coordinate.X))]
    public double X { get; set; }

    [MapTo(nameof(Coordinate.Y))]
    public double Y { get; set; }

    [MapTo(nameof(Coordinate.Label))]
    public string? Label { get; set; }
}

// Scenario 3: Record without primary constructor (uses init properties)
[GenerateMapper(typeof(TagDto))]
public record Tag
{
    public string Name { get; init; }
    public string Color { get; init; }
}

public record TagDto
{
    [MapTo(nameof(Tag.Name))]
    public string Name { get; init; }

    [MapTo(nameof(Tag.Color))]
    public string Color { get; init; }
}