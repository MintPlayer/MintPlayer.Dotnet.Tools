using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Runtime;

/// <summary>
/// Layer 2. These assert what the generated code DOES, not what it says. A text assertion
/// cannot tell "AddScoped" from "AddSingleton" being the right choice, nor notice that the
/// resulting service graph does not resolve.
/// </summary>
public class GeneratedRegistrationBehaviourTests
{
    private const string Source = """
        using Microsoft.Extensions.DependencyInjection;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public interface IGreeter { string Greet(); }

        [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
        public class Greeter : IGreeter
        {
            public string Greet() => "hi";
        }

        public interface IClock { }

        [Register(typeof(IClock), ServiceLifetime.Singleton)]
        public class Clock : IClock { }

        public interface ICounter { }

        [Register(typeof(ICounter), ServiceLifetime.Transient)]
        public class Counter : ICounter { }
        """;

    private static IServiceCollection RunGeneratedRegistration()
    {
        var run = GeneratorHarness.Run("ServiceRegistrationsGenerator", [Source], "Demo");
        run.Errors.Should().BeEmpty(run.ErrorText);

        var assembly = run.Emit();
        var method = assembly.GetGeneratedMethod("AddTestInput");

        var services = new ServiceCollection();
        method.Invoke(null, [services]);
        return services;
    }

    [Fact]
    public void TheGeneratedMethodRegistersEveryDecoratedService()
    {
        var services = RunGeneratedRegistration();

        services.Select(d => d.ServiceType.Name).Should().Contain("IGreeter");
        services.Select(d => d.ServiceType.Name).Should().Contain("IClock");
        services.Select(d => d.ServiceType.Name).Should().Contain("ICounter");
    }

    [Fact]
    public void TheGeneratedMethodUsesTheDeclaredLifetimes()
    {
        var services = RunGeneratedRegistration();

        ServiceLifetime LifetimeOf(string serviceTypeName)
            => services.Single(d => d.ServiceType.Name == serviceTypeName).Lifetime;

        // This is the assertion no text check can make: "AddScoped" appearing in the output
        // does not prove it was applied to the RIGHT service.
        LifetimeOf("IGreeter").Should().Be(ServiceLifetime.Scoped);
        LifetimeOf("IClock").Should().Be(ServiceLifetime.Singleton);
        LifetimeOf("ICounter").Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void TheResultingServiceGraphPassesValidateOnBuild()
    {
        var services = RunGeneratedRegistration();

        // ValidateOnBuild walks every registration and throws on anything unconstructible,
        // so this catches a generator that registered an implementation type it cannot
        // actually instantiate — right-looking text, unusable container.
        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void TheGeneratedMethodInvokesTheRealImplementation()
    {
        var run = GeneratorHarness.Run("ServiceRegistrationsGenerator", [Source], "Demo");
        var assembly = run.Emit();

        var services = new ServiceCollection();
        assembly.GetGeneratedMethod("AddTestInput").Invoke(null, [services]);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var greeterInterface = assembly.GetGeneratedType("Demo.IGreeter");
        var greeter = scope.ServiceProvider.GetRequiredService(greeterInterface);

        var greeting = greeterInterface.GetMethod("Greet")!.Invoke(greeter, null);

        greeting.Should().Be("hi");
    }

    [Fact]
    public void ASingletonResolvesToTheSameInstanceAcrossScopes()
    {
        var run = GeneratorHarness.Run("ServiceRegistrationsGenerator", [Source], "Demo");
        var assembly = run.Emit();

        var services = new ServiceCollection();
        assembly.GetGeneratedMethod("AddTestInput").Invoke(null, [services]);

        using var provider = services.BuildServiceProvider();
        var clockInterface = assembly.GetGeneratedType("Demo.IClock");

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        second.ServiceProvider.GetRequiredService(clockInterface)
            .Should().BeSameAs(first.ServiceProvider.GetRequiredService(clockInterface));
    }

    [Fact]
    public void AScopedServiceResolvesToDifferentInstancesInDifferentScopes()
    {
        var run = GeneratorHarness.Run("ServiceRegistrationsGenerator", [Source], "Demo");
        var assembly = run.Emit();

        var services = new ServiceCollection();
        assembly.GetGeneratedMethod("AddTestInput").Invoke(null, [services]);

        using var provider = services.BuildServiceProvider();
        var greeterInterface = assembly.GetGeneratedType("Demo.IGreeter");

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        second.ServiceProvider.GetRequiredService(greeterInterface)
            .Should().NotBeSameAs(first.ServiceProvider.GetRequiredService(greeterInterface));
    }

    [Fact]
    public void ATransientServiceResolvesToANewInstanceEachTime()
    {
        var run = GeneratorHarness.Run("ServiceRegistrationsGenerator", [Source], "Demo");
        var assembly = run.Emit();

        var services = new ServiceCollection();
        assembly.GetGeneratedMethod("AddTestInput").Invoke(null, [services]);

        using var provider = services.BuildServiceProvider();
        var counterInterface = assembly.GetGeneratedType("Demo.ICounter");

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(counterInterface)
            .Should().NotBeSameAs(scope.ServiceProvider.GetRequiredService(counterInterface));
    }
}

public class GeneratedInjectBehaviourTests
{
    [Fact]
    public void TheGeneratedConstructorAssignsTheInjectedField()
    {
        var run = GeneratorHarness.Run("InjectSourceGenerator", ["""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IService { string Name { get; } }

            public class Service : IService { public string Name => "svc"; }

            public partial class Consumer
            {
                [Inject] private readonly IService service;

                public string Describe() => service.Name;
            }
            """], "Demo");

        run.Errors.Should().BeEmpty(run.ErrorText);

        var assembly = run.Emit();
        var consumerType = assembly.GetGeneratedType("Demo.Consumer");
        var serviceType = assembly.GetGeneratedType("Demo.Service");

        // The generated constructor is what makes this possible at all: Consumer declares no
        // constructor of its own.
        var consumer = Activator.CreateInstance(consumerType, Activator.CreateInstance(serviceType));

        consumerType.GetMethod("Describe")!.Invoke(consumer, null).Should().Be("svc");
    }

    [Fact]
    public void TheGeneratedConstructorTakesEveryInjectedDependencyInOrder()
    {
        var run = GeneratorHarness.Run("InjectSourceGenerator", ["""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IA { }
            public interface IB { }
            public class A : IA { }
            public class B : IB { }

            public partial class Consumer
            {
                [Inject] private readonly IA a;
                [Inject] private readonly IB b;
            }
            """], "Demo");

        run.Errors.Should().BeEmpty(run.ErrorText);

        var assembly = run.Emit();
        var consumerType = assembly.GetGeneratedType("Demo.Consumer");

        var constructor = consumerType.GetConstructors().Should().ContainSingle().Which;

        constructor.GetParameters().Select(p => p.ParameterType.Name).Should().Equal(["IA", "IB"]);
    }

    [Fact]
    public void TheGeneratedConstructorWorksThroughDependencyInjection()
    {
        var run = GeneratorHarness.Run("InjectSourceGenerator", ["""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IService { string Name { get; } }
            public class Service : IService { public string Name => "svc"; }

            public partial class Consumer
            {
                [Inject] private readonly IService service;

                public string Describe() => service.Name;
            }
            """], "Demo");

        var assembly = run.Emit();
        var consumerType = assembly.GetGeneratedType("Demo.Consumer");
        var serviceInterface = assembly.GetGeneratedType("Demo.IService");
        var serviceImpl = assembly.GetGeneratedType("Demo.Service");

        var services = new ServiceCollection();
        services.AddSingleton(serviceInterface, serviceImpl);
        services.AddSingleton(consumerType);

        using var provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService(consumerType);

        consumerType.GetMethod("Describe")!.Invoke(consumer, null).Should().Be("svc");
    }
}
