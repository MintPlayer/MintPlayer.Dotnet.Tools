using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// The registration shapes <c>ServiceRegistrationsGenerator</c> supports beyond the plain
/// <c>[Register(typeof(IFoo), ServiceLifetime.Scoped)]</c> case.
/// </summary>
/// <remarks>
/// The existing suite covers the happy path and the lifetimes. Everything here is a distinct
/// branch of the attribute-shape decoding — three constructor overloads, open generics, base
/// classes, factories, assembly-level registration and accessibility — which together are most of
/// the uncovered mass in this generator.
/// </remarks>
public class ServiceRegistrationsShapesTests
{
    private const string Generator = "ServiceRegistrationsGenerator";

    private static GeneratorRun Run(string source) => GeneratorHarness.Run(Generator, [source]);

    private const string Preamble = """
        using Microsoft.Extensions.DependencyInjection;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;
        """;

    #region Constructor overloads

    /// <summary>
    /// The lifetime-only overload registers the concrete type against itself — there is no service
    /// type to point at.
    /// </summary>
    [Fact]
    public void ItRegistersAConcreteTypeAgainstItself()
    {
        var run = Run($$"""
            {{Preamble}}

            [Register(ServiceLifetime.Singleton)]
            public class Cache { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("AddSingleton");
        run.AllSources.Should().Contain("Cache");
    }

    [Fact]
    public void ItRegistersAgainstAnExplicitServiceType()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Transient)]
            public class Greeter : IGreeter { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("AddTransient");
        run.AllSources.Should().Contain("IGreeter");
    }

    /// <summary>
    /// A base class is as valid a service type as an interface, and reaches a different branch —
    /// the interface check fails and the base-type chain walk has to succeed.
    /// </summary>
    [Fact]
    public void ItRegistersAgainstABaseClass()
    {
        var run = Run($$"""
            {{Preamble}}

            public abstract class RepositoryBase { }

            [Register(typeof(RepositoryBase), ServiceLifetime.Scoped)]
            public class PersonRepository : RepositoryBase { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("RepositoryBase");
    }

    [Fact]
    public void ItWalksMoreThanOneLevelOfBaseClass()
    {
        var run = Run($$"""
            {{Preamble}}

            public abstract class RepositoryBase { }
            public abstract class AuditedRepository : RepositoryBase { }

            [Register(typeof(RepositoryBase), ServiceLifetime.Scoped)]
            public class PersonRepository : AuditedRepository { }
            """);

        run.AllSources.Should().Contain("RepositoryBase");
    }

    /// <summary>
    /// A service type the class does not actually implement is silently skipped rather than
    /// emitted — registering it would produce code that does not compile in the consumer.
    /// </summary>
    [Fact]
    public void ItSkipsAServiceTypeTheClassDoesNotImplement()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IUnrelated { }

            [Register(typeof(IUnrelated), ServiceLifetime.Scoped)]
            public class Greeter { }
            """);

        run.AllSources.Should().NotContain("IUnrelated");
    }

    #endregion

    #region Open generics

    [Fact]
    public void ItRegistersAnOpenGenericInterface()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepository<T> { }

            [Register(typeof(IRepository<>), ServiceLifetime.Scoped)]
            public class Repository<T> : IRepository<T> { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("IRepository");

        // Emitted as a closed generic method — AddScoped<IRepository<T>, Repository<T>>() inside a
        // generic extension method — not as AddScoped(typeof(IRepository<>), typeof(Repository<>)).
        run.AllSources.Should().Contain("AddScoped<");
    }

    [Fact]
    public void ItRegistersAnOpenGenericWithTwoParameters()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepository<TKey, TValue> { }

            [Register(typeof(IRepository<,>), ServiceLifetime.Singleton)]
            public class Repository<TKey, TValue> : IRepository<TKey, TValue> { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("IRepository");
    }

    [Fact]
    public void ItRegistersAnOpenGenericBaseClass()
    {
        var run = Run($$"""
            {{Preamble}}

            public abstract class RepositoryBase<T> { }

            [Register(typeof(RepositoryBase<>), ServiceLifetime.Scoped)]
            public class Repository<T> : RepositoryBase<T> { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("RepositoryBase");
    }

    /// <summary>
    /// Open-generic registration needs a generic implementation. A closed class cannot satisfy
    /// <c>IRepository&lt;&gt;</c>, so the generator skips it rather than emitting
    /// <c>AddScoped(typeof(IRepository&lt;&gt;), typeof(Repository))</c>, which throws at runtime.
    /// </summary>
    [Fact]
    public void ItSkipsAnOpenGenericOnANonGenericClass()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepository<T> { }

            [Register(typeof(IRepository<>), ServiceLifetime.Scoped)]
            public class PersonRepository : IRepository<string> { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void ItSkipsAnOpenGenericTheClassDoesNotImplement()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepository<T> { }
            public interface IOther<T> { }

            [Register(typeof(IOther<>), ServiceLifetime.Scoped)]
            public class Repository<T> : IRepository<T> { }
            """);

        run.AllSources.Should().NotContain("IOther");
    }

    #endregion

    #region Factories

    [Fact]
    public void ItUsesAStaticFactoryForAConcreteRegistration()
    {
        var run = Run($$"""
            {{Preamble}}
            using System;

            [Register(ServiceLifetime.Scoped)]
            public class Greeter
            {
                [RegisterFactory]
                public static Greeter Create(IServiceProvider provider) => new Greeter();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Greeter.Create");
    }

    [Fact]
    public void ItUsesAStaticFactoryReturningTheServiceType()
    {
        var run = Run($$"""
            {{Preamble}}
            using System;

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                [RegisterFactory]
                public static IGreeter Create(IServiceProvider provider) => new Greeter();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Greeter.Create");
    }

    /// <summary>
    /// A parameterless factory is wrapped in a lambda so it satisfies
    /// <c>Func&lt;IServiceProvider, T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Passing the method group directly used to produce CS1503 — "cannot convert from 'method
    /// group'" — in the CONSUMER's build, pointing at generated code they never wrote. The
    /// parameterless form is the shape most people write first, so it has to work.
    /// </remarks>
    [Fact]
    public void AParameterlessFactoryIsWrappedInALambda()
    {
        var run = Run($$"""
            {{Preamble}}

            [Register(ServiceLifetime.Scoped)]
            public class Greeter
            {
                [RegisterFactory]
                public static Greeter Create() => new Greeter();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("sp =>");
        run.AllSources.Should().Contain("Greeter.Create()");
    }

    /// <summary>
    /// The lambda wrapping applies to the interface-registration path too, which reaches a
    /// different emission branch in the producer.
    /// </summary>
    [Fact]
    public void AParameterlessFactoryIsWrappedWhenRegisteringAnInterface()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                [RegisterFactory]
                public static Greeter Create() => new Greeter();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("sp =>");
    }

    /// <summary>
    /// A factory returning the implementation is used when registering against an interface.
    /// </summary>
    /// <remarks>
    /// Matching is by assignability, not identity. It used to compare the return type for exact
    /// symbol equality against the registered type, so this factory was silently skipped — the
    /// registration still happened, just without it, and the container constructed the service
    /// itself. Nothing failed; the factory simply never ran.
    /// </remarks>
    [Fact]
    public void AFactoryReturningTheImplementationIsUsedWhenRegisteringAnInterface()
    {
        var run = Run($$"""
            {{Preamble}}
            using System;

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                [RegisterFactory]
                public static Greeter Create(IServiceProvider provider) => new Greeter();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("IGreeter");
        run.AllSources.Should().Contain("Greeter.Create");
    }

    /// <summary>
    /// A factory returning something unrelated is still rejected — assignability widened the
    /// check, it did not remove it.
    /// </summary>
    [Fact]
    public void AFactoryReturningAnUnrelatedTypeIsStillRejected()
    {
        var run = Run($$"""
            {{Preamble}}
            using System;

            public interface IGreeter { }
            public class Unrelated { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                [RegisterFactory]
                public static Unrelated Create(IServiceProvider provider) => new Unrelated();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("Greeter.Create");
    }

    /// <summary>
    /// A factory returning something else is not a factory for this type, and must not be wired up
    /// — the generated call would not compile.
    /// </summary>
    [Fact]
    public void ItIgnoresAFactoryReturningADifferentType()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }
            public class Other { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                [RegisterFactory]
                public static Other CreateOther() => new Other();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("CreateOther");
    }

    [Fact]
    public void ItIgnoresANonStaticFactory()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                [RegisterFactory]
                public Greeter Create() => new Greeter();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    #endregion

    #region Method name and accessibility

    [Fact]
    public void ItHonoursTheMethodNameHint()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped, "AddGreeting")]
            public class Greeter : IGreeter { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("AddGreeting");
    }

    [Theory]
    [InlineData("EGeneratedAccessibility.Public", "public static")]
    [InlineData("EGeneratedAccessibility.Internal", "internal static")]
    public void ItHonoursTheRequestedAccessibility(string accessibility, string expected)
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped, "AddGreeting", {{accessibility}})]
            public class Greeter : IGreeter { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(expected);
    }

    #endregion

    #region Multiple registrations

    /// <summary>
    /// <c>[Register]</c> is <c>AllowMultiple</c>, so one class can be registered against several
    /// service types — each has to produce its own registration line.
    /// </summary>
    [Fact]
    public void ItEmitsOneRegistrationPerAttribute()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IReader { }
            public interface IWriter { }

            [Register(typeof(IReader), ServiceLifetime.Scoped)]
            [Register(typeof(IWriter), ServiceLifetime.Scoped)]
            public class Store : IReader, IWriter { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("IReader");
        run.AllSources.Should().Contain("IWriter");
    }

    [Fact]
    public void ItRegistersSeveralClassesInOneCompilation()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IA { }
            public interface IB { }

            [Register(typeof(IA), ServiceLifetime.Scoped)]
            public class AImpl : IA { }

            [Register(typeof(IB), ServiceLifetime.Singleton)]
            public class BImpl : IB { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("AImpl");
        run.AllSources.Should().Contain("BImpl");
    }

    #endregion

    /// <summary>
    /// The five-parameter constructor is the assembly-level form. Applying it to a class is a
    /// mistake the generator detects deliberately rather than silently ignoring.
    /// </summary>
    [Fact]
    public void ItReportsTheAssemblyLevelOverloadUsedOnAClass()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }

            [Register(typeof(IGreeter), typeof(Greeter), ServiceLifetime.Scoped, "AddGreeting", EGeneratedAccessibility.Public)]
            public class Greeter : IGreeter { }
            """);

        run.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void ItRegistersAtAssemblyLevel()
    {
        var run = Run($$"""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            [assembly: Register(typeof(Demo.IGreeter), typeof(Demo.Greeter), ServiceLifetime.Scoped, "AddGreeting", EGeneratedAccessibility.Public)]

            namespace Demo;

            public interface IGreeter { }
            public class Greeter : IGreeter { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("IGreeter");
    }
}
