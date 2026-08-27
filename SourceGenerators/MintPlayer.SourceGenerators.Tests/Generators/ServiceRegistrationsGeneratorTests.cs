using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

public class ServiceRegistrationsGeneratorTests
{
    private const string Generator = "ServiceRegistrationsGenerator";

    private static GeneratorRun Run(string source, string? rootNamespace = "TestRoot")
        => GeneratorHarness.Run(Generator, [source], rootNamespace);

    [Fact]
    public void ItEmitsARegistrationExtensionMethod()
    {
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IGreeter { string Greet(); }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                public string Greet() => "hi";
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().NotBeEmpty();
        run.AllSources.Should().Contain("AddScoped");
        run.AllSources.Should().Contain("IGreeter");
    }

    [Fact]
    public void ItHonoursEachLifetime()
    {
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IA { }
            public interface IB { }
            public interface IC { }

            [Register(typeof(IA), ServiceLifetime.Singleton)]
            public class A : IA { }

            [Register(typeof(IB), ServiceLifetime.Scoped)]
            public class B : IB { }

            [Register(typeof(IC), ServiceLifetime.Transient)]
            public class C : IC { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("AddSingleton");
        run.AllSources.Should().Contain("AddScoped");
        run.AllSources.Should().Contain("AddTransient");
    }

    [Fact]
    public void ItEmitsNothingWhenNothingIsDecorated()
    {
        var run = Run("""
            namespace Demo;

            public class Plain { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("AddScoped");
    }

    [Fact]
    public void TheGeneratedCodeCompiles()
    {
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IGreeter { string Greet(); }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter
            {
                public string Greet() => "hi";
            }
            """);

        // The single most important assertion in the suite: without it, a generator that
        // emits syntactically invalid C# still passes every "contains" check.
        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    /// <summary>
    /// Regression for R3.2 in docs/PRD-TestCoverage.md. Several producers pass
    /// <c>RootNamespace!</c> straight into the emitted <c>namespace</c> declaration. With no
    /// <c>build_property.rootnamespace</c> supplied the value is null, which used to emit a
    /// bare <c>namespace</c> and produce CS1001 in the consumer's build. This asserts the
    /// generator either handles it or fails loudly — never emits broken syntax silently.
    /// </summary>
    [Fact]
    public void WithoutARootNamespace_ItDoesNotEmitUncompilableCode()
    {
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter { }
            """, rootNamespace: null);

        run.Errors.Should().NotContain(d => d.Id == "CS1001", "a bare `namespace` is never acceptable output");
    }

    [Fact]
    public void ARegisterAttributeOnAnUnrelatedShape_DoesNotCrashTheGenerator()
    {
        // Whatever the generator decides here, it must not swallow an exception and emit
        // nothing without saying why — which is what the Producer catch used to do.
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            [Register(typeof(string), ServiceLifetime.Scoped)]
            public class Odd { }
            """);

        run.Errors.Should().NotContain(d => d.Id == "MPSG001", run.ErrorText);
    }

    [Fact]
    public void ItPlacesGeneratedCodeInTheRootNamespace()
    {
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter { }
            """, rootNamespace: "My.Chosen.Root");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("My.Chosen.Root");
    }

    [Fact]
    public void EveryGeneratedFileCarriesTheAutoGeneratedHeader()
    {
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter { }
            """);

        run.GeneratedSources.Should().NotBeEmpty();
        run.GeneratedSources.Should().OnlyContain(s => s.Source.Contains("auto-generated"));
    }
}
