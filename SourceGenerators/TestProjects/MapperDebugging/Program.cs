// See https://aka.ms/new-console-template for more information
using MintPlayer.Mapper.Attributes;

Console.WriteLine("Hello, World!");

[GenerateMapper(typeof(PersonDto))]
public class Person
{
    public string? Name { get; set; }
    public int? Age { get; set; }
}

public class PersonDto
{
    [MapperAlias(nameof(Person.Name))]
    public string? Naam { get; set; }

    [MapperAlias(nameof(Person.Age))]
    public int? Leeftijd { get; set; }
}