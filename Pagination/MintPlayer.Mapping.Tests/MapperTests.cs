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
    private readonly IServiceProvider serviceProvider;
    public PersonMapper(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

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

public class MapperTests
{
    private IServiceProvider Setup2FunctionalMappers()
    {
        return new ServiceCollection()
            // "provider" parameter is optional
            .AddMapper<Person, PersonDTO>(async (entity, provider) =>
            {
                return new PersonDTO
                {
                    Id = entity.Id,
                    FirstName = entity.FirstName,
                    LastName = entity.LastName
                };
            })
            .AddMapper<PersonDTO, Person>(async (dto, provider) =>
            {
                return new Person
                {
                    Id = dto.Id,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName
                };
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Mapper_FunctionMapper_Entity2Dto_Test()
    {
        var services = Setup2FunctionalMappers();
        var entity = new Person
        {
            Id = 1,
            FirstName = "MyFirstName",
            LastName = "MyLastName"
        };

        var dto = await services.Mapper(entity).MapTo<PersonDTO>();
        Assert.NotNull(dto);
        Assert.IsType<PersonDTO>(dto);

        Assert.Equal(1, dto.Id);
        Assert.Equal("MyFirstName", dto.FirstName);
        Assert.Equal("MyLastName", dto.LastName);
    }

    [Fact]
    public async Task Mapper_FunctionMapper_Dto2Entity_Test()
    {
        var services = Setup2FunctionalMappers();
        var dto = new PersonDTO
        {
            Id = 1,
            FirstName = "MyFirstName",
            LastName = "MyLastName"
        };

        var entity = await services.Mapper(dto).MapTo<Person>();
        Assert.NotNull(entity);
        Assert.IsType<Person>(entity);

        Assert.Equal(1, entity.Id);
        Assert.Equal("MyFirstName", entity.FirstName);
        Assert.Equal("MyLastName", entity.LastName);
    }

    private IServiceProvider SetupClassMapper()
    {
        return new ServiceCollection()
            .AddMapper<PersonMapper>()
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Mapper_ClassMapper_Entity2Dto_Test()
    {
        var services = SetupClassMapper();
        var entity = new Person
        {
            Id = 1,
            FirstName = "MyFirstName",
            LastName = "MyLastName"
        };

        var dto = await services.Mapper(entity).MapTo<PersonDTO>();
        Assert.NotNull(dto);
        Assert.IsType<PersonDTO>(dto);

        Assert.Equal(1, dto.Id);
        Assert.Equal("MyFirstName", dto.FirstName);
        Assert.Equal("MyLastName", dto.LastName);
    }

    [Fact]
    public async Task Mapper_ClassMapper_Dto2Entity_Test()
    {
        var services = SetupClassMapper();
        var dto = new PersonDTO
        {
            Id = 1,
            FirstName = "MyFirstName",
            LastName = "MyLastName"
        };

        var entity = await services.Mapper(dto).MapTo<Person>();
        Assert.NotNull(entity);
        Assert.IsType<Person>(entity);

        Assert.Equal(1, entity.Id);
        Assert.Equal("MyFirstName", entity.FirstName);
        Assert.Equal("MyLastName", entity.LastName);
    }
}
