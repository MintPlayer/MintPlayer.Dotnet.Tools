// See https://aka.ms/new-console-template for more information
using MintPlayer.Mapper.Attributes;

Console.WriteLine("Hello, World!");

[GenerateMapper(typeof(PersonDto))]
public class Person
{
    public string? Name { get; set; }

    [MapperAlias("Leeftijd")]
    public int? Age { get; set; }
}

public class PersonDto
{
    public string? Name { get; set; }
    public int? Age { get; set; }
}