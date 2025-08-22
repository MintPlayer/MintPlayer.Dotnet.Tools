// See https://aka.ms/new-console-template for more information
using MintPlayer.Mapper.Attributes;

Console.WriteLine("Hello, World!");

[GenerateMapper(typeof(PersonDto))]
public class Person
{
    public string? Name { get; set; }
    public int? Age { get; set; }
    public Address Address { get; set; }
    public List<ContactInfo> ContactInfos { get; set; } = [];
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