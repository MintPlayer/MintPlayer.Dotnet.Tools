using MintPlayer.Mapper.Attributes;
using MapperDebugging;
using System.Diagnostics;

[assembly: GenerateMapper(typeof(Person), typeof(PersonDto), "Persoon")]
[assembly: GenerateMapper(typeof(ContactInfo), typeof(ContactInfoDto), "MapTo")]


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
    [MapperIgnore]
    public string? Name { get; set; }
    public int? Age { get; set; }
    public Address Address { get; set; }
    public List<ContactInfo> ContactInfos { get; set; } = [];
    public List<string> Notes { get; set; }
    public double Weight { get; set; }

    [MapperAlias(nameof(Person.Password)), MapperState<EPasswordState>(EPasswordState.Plaintext)]
    public string Password { get; set; }
}

//[GenerateMapper(typeof(Person), typeof(PersonDto), "PersoonDto")]
//[GenerateMapper(typeof(Person), "PersoonDto")]
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

    [MapperAlias(nameof(Person.Password)), MapperState<EPasswordState>(EPasswordState.Base64)]
    public string Password { get; set; }
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

//[GenerateMapper(typeof(ContactInfo))]
public class ContactInfoDto
{
    [MapperAlias(nameof(ContactInfo.Type))]
    public string Soort { get; set; }

    [MapperAlias(nameof(ContactInfo.Value))]
    public string Waarde { get; set; }
}