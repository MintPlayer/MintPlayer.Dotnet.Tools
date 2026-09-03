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
    /// DEFECT, pinned rather than fixed: a parameterless <c>[RegisterFactory]</c> generates code
    /// that does not compile.
    /// </summary>
    /// <remarks>
    /// The generator accepts any static method whose return type matches, without checking its
    /// signature, and emits <c>.AddScoped&lt;Greeter&gt;(Greeter.Create)</c>. But the DI overload
    /// it lands on takes <c>Func&lt;IServiceProvider, T&gt;</c>, so a parameterless factory fails
    /// with CS1503 — "cannot convert from 'method group'" — in the CONSUMER's build, pointing at
    /// generated code they did not write.
    ///
    /// Two reasonable fixes, both behavioural decisions for the maintainer: report a diagnostic on
    /// a factory with the wrong signature, or emit <c>sp =&gt; Greeter.Create()</c> for the
    /// parameterless case. This test documents the current state and will fail loudly when either
    /// is chosen.
    /// </remarks>
    [Fact]
    public void AParameterlessFactoryEmitsUncompilableCode()
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

        run.Diagnostics.Should().BeEmpty("the generator reports nothing — that is the problem");
        run.Errors.Should().NotBeEmpty("the emitted code does not compile");
        run.ErrorText.Should().Contain("CS1503");
    }

    /// <summary>
    /// Characterization, and arguably a limitation worth revisiting.
    /// </summary>
    /// <remarks>
    /// Factory matching compares the return type for EXACT symbol equality against the type being
    /// registered (<c>ServiceRegistrationsGenerator.cs:151</c>). So when registering against an
    /// interface, a factory returning the concrete implementation is silently ignored — even
    /// though <c>AddScoped&lt;IGreeter&gt;(Greeter.Create)</c> would compile perfectly well, since
    /// <c>Greeter</c> is assignable to <c>IGreeter</c>.
    ///
    /// The registration still happens, just without the factory, so the symptom is a service
    /// constructed by the container instead of by its factory — silent, and easy to miss. Pinned
    /// as current behaviour rather than changed, because widening it to an assignability check is
    /// a behavioural decision for the maintainer.
    /// </remarks>
    [Fact]
    public void AFactoryReturningTheImplementationIsIgnoredWhenRegisteringAnInterface()
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
        run.AllSources.Should().Contain("IGreeter");
        run.AllSources.Should().NotContain("Greeter.Create");
        run.Errors.Should().BeEmpty(run.ErrorText);
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
