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
        ContactInfo.Create("Email", "info@example.com"),
        ContactInfo.Create("Phone", "123-456-7890"),
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
    private ContactInfo() { }
    public static ContactInfo Create(string type, string value)
    {
        return new ContactInfo
        {
            Type = type,
            Value = value
        };
    }

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