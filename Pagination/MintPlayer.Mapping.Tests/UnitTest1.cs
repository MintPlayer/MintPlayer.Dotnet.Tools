using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Mapping.Extensions;

namespace MintPlayer.Mapping.Tests;

class Person
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
}

class PersonDTO
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
}

class PersonMapper : IMapper<PersonDTO, Person>, IMapper<Person, PersonDTO>
{
    public Task<Person> Map(PersonDTO source)
    {
        return Task.FromResult(new Person
        {
            Id = source.Id,
            FirstName = source.FirstName,
            LastName = source.LastName,
        });
    }

    public Task<PersonDTO> Map(Person source)
    {
        return Task.FromResult(new PersonDTO
        {
            Id = source.Id,
            FirstName = source.FirstName,
            LastName = source.LastName,
        });
    }
}

public class UnitTest1
{
    [Fact]
    public async void Test1()
    {
        var services = new ServiceCollection()
            .AddMapper<Person, PersonDTO>(async (entity) =>
            {
                return new PersonDTO
                {
                    Id = entity.Id,
                    FirstName = entity.FirstName,
                    LastName = entity.LastName
                };
            })
            .AddMapper<PersonDTO, Person>(async (dto) =>
            {
                return new Person
                {
                    Id = dto.Id,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName
                };
            })
            .AddMapper<PersonMapper>()
            .BuildServiceProvider();

        var entity = new Person
        {
            Id = 1,
            FirstName = "Test",
            LastName = "Test"
        };

        await MintPlayer.Mapping.Extensions.ServiceCollectionExtensions.Map<Person, PersonDTO>(services, entity);

        var dto = services.Map<PersonDTO>(entity);
        //Assert.
    }
}
