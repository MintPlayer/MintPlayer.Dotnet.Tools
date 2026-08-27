using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Assertions;
using MintPlayer.Mapping.Extensions;

namespace MintPlayer.Mapping.Tests;

/// <summary>
/// Covers the registration surface of AddMapper — the guards, the lifetimes and the
/// two-directions-from-one-instance behaviour — which MapperTests exercises only along
/// its happy path.
/// </summary>
public class AddMapperTests
{
    #region Fixtures

    private sealed class OneWayMapper : IMapper<Person, PersonDTO>
    {
        public Task<PersonDTO> Map(Person source) => Task.FromResult(new PersonDTO { Id = source.Id });
    }

    /// <summary>A valid two-direction mapper that ALSO implements a non-generic interface.</summary>
    private sealed class DisposableTwoWayMapper : IMapper<Person, PersonDTO>, IMapper<PersonDTO, Person>, IDisposable
    {
        public bool Disposed { get; private set; }
        public Task<PersonDTO> Map(Person source) => Task.FromResult(new PersonDTO { Id = source.Id });
        public Task<Person> Map(PersonDTO source) => Task.FromResult(new Person { Id = source.Id });
        public void Dispose() => Disposed = true;
    }

    private sealed class NotAMapper { }

    private sealed class MismatchedMapper : IMapper<Person, PersonDTO>, IMapper<Person, string>
    {
        public Task<PersonDTO> Map(Person source) => Task.FromResult(new PersonDTO());
        Task<string> IMapper<Person, string>.Map(Person source) => Task.FromResult(string.Empty);
    }

    #endregion

    #region D3 regression

    /// <summary>
    /// Regression for D3 in docs/PRD-TestCoverage.md. AddMapper called
    /// GetGenericTypeDefinition() on every interface the mapper implements, and that
    /// throws InvalidOperationException for a non-generic one — so a mapper that also
    /// implemented IDisposable (or any other plain interface) could not be registered.
    /// </summary>
    [Fact]
    public void AddMapper_AcceptsAMapperThatAlsoImplementsANonGenericInterface()
    {
        var act = () => new ServiceCollection().AddMapper<DisposableTwoWayMapper>();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task AddMapper_WithANonGenericInterface_StillMapsBothDirections()
    {
        using var provider = new ServiceCollection()
            .AddMapper<DisposableTwoWayMapper>()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        var forward = await scope.ServiceProvider
            .GetRequiredService<IMapper<Person, PersonDTO>>().Map(new Person { Id = 7 });
        var backward = await scope.ServiceProvider
            .GetRequiredService<IMapper<PersonDTO, Person>>().Map(new PersonDTO { Id = 9 });

        forward.Id.Should().Be(7);
        backward.Id.Should().Be(9);
    }

    #endregion

    #region Guards

    [Fact]
    public void AddMapper_WhenTMapperImplementsNoIMapper_Throws()
    {
        var act = () => new ServiceCollection().AddMapper<NotAMapper>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*expects TMapper to implement IMapper*");
    }

    [Fact]
    public void AddMapper_WhenTheTwoDirectionsDoNotAlternate_Throws()
    {
        var act = () => new ServiceCollection().AddMapper<MismatchedMapper>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*alternating type arguments*");
    }

    [Fact]
    public void AddMapper_WithASingleDirection_Throws()
    {
        // The private overload constrains TMapper to both directions, so a one-way mapper
        // cannot satisfy it. The failure surfaces from the reflective MakeGenericMethod.
        var act = () => new ServiceCollection().AddMapper<OneWayMapper>();

        act.Should().Throw<Exception>();
    }

    #endregion

    #region Lifetimes and identity

    [Fact]
    public void AddMapper_RegistersBothDirectionsPlusTheConcreteType()
    {
        var services = new ServiceCollection().AddMapper<DisposableTwoWayMapper>();

        services.Should().Contain(d => d.ServiceType == typeof(DisposableTwoWayMapper));
        services.Should().Contain(d => d.ServiceType == typeof(IMapper<Person, PersonDTO>));
        services.Should().Contain(d => d.ServiceType == typeof(IMapper<PersonDTO, Person>));
    }

    [Fact]
    public void AddMapper_RegistersEverythingAsScoped()
    {
        var services = new ServiceCollection().AddMapper<DisposableTwoWayMapper>();

        services.Where(d =>
                d.ServiceType == typeof(DisposableTwoWayMapper) ||
                d.ServiceType == typeof(IMapper<Person, PersonDTO>) ||
                d.ServiceType == typeof(IMapper<PersonDTO, Person>))
            .Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddMapper_ResolvesOneInstanceForBothDirectionsWithinAScope()
    {
        using var provider = new ServiceCollection()
            .AddMapper<DisposableTwoWayMapper>()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        var forward = scope.ServiceProvider.GetRequiredService<IMapper<Person, PersonDTO>>();
        var backward = scope.ServiceProvider.GetRequiredService<IMapper<PersonDTO, Person>>();

        // Both interfaces resolve through GetRequiredService<TMapper>(), so they are the
        // same object — which is the point of the two-direction registration.
        backward.Should().BeSameAs(forward);
    }

    [Fact]
    public void AddMapper_ResolvesDifferentInstancesInDifferentScopes()
    {
        using var provider = new ServiceCollection()
            .AddMapper<DisposableTwoWayMapper>()
            .BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        second.ServiceProvider.GetRequiredService<DisposableTwoWayMapper>()
            .Should().NotBeSameAs(first.ServiceProvider.GetRequiredService<DisposableTwoWayMapper>());
    }

    [Fact]
    public void AddMapper_ReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();
        services.AddMapper<DisposableTwoWayMapper>().Should().BeSameAs(services);
    }

    #endregion

    #region Delegate overloads

    [Fact]
    public async Task AddMapper_WithASimpleDelegate_Maps()
    {
        using var provider = new ServiceCollection()
            .AddMapper<Person, PersonDTO>(p => Task.FromResult(new PersonDTO { Id = p.Id * 2 }))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IMapper<Person, PersonDTO>>().Map(new Person { Id = 4 });

        result.Id.Should().Be(8);
    }

    [Fact]
    public async Task AddMapper_WithAProviderAwareDelegate_ReceivesTheProvider()
    {
        IServiceProvider? seen = null;

        using var provider = new ServiceCollection()
            .AddMapper<Person, PersonDTO>((p, sp) =>
            {
                seen = sp;
                return Task.FromResult(new PersonDTO { Id = p.Id });
            })
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMapper<Person, PersonDTO>>().Map(new Person());

        seen.Should().NotBeNull();
    }

    [Fact]
    public void AddMapper_WithADelegate_RegistersAsScoped()
    {
        var services = new ServiceCollection()
            .AddMapper<Person, PersonDTO>(p => Task.FromResult(new PersonDTO()));

        services.Should().ContainSingle(d => d.ServiceType == typeof(IMapper<Person, PersonDTO>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Mapper() entry point

    [Fact]
    public async Task Mapper_MapsThroughTheRegisteredMapper()
    {
        using var provider = new ServiceCollection()
            .AddMapper<Person, PersonDTO>(p => Task.FromResult(new PersonDTO { Id = p.Id + 1 }))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .Mapper(new Person { Id = 10 })
            .MapTo<PersonDTO>();

        result.Id.Should().Be(11);
    }

    [Fact]
    public async Task Mapper_WithNoRegisteredMapper_Throws()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = async () => await provider.Mapper(new Person()).MapTo<PersonDTO>();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion
}
