using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// ServiceRegistrationsGenerator on every type kind and on the shapes that crashed it or made it emit broken code
/// (docs/PRD-TypeKindRobustness.md: C2, C3, K3, W5, W6, P9–P12).
/// </summary>
public class ServiceRegistrationsRobustnessTests
{
    private const string Generator = "ServiceRegistrationsGenerator";

    private static GeneratorRun Run(params string[] sources) => GeneratorHarness.Run(Generator, sources);

    private const string Preamble = """
        using Microsoft.Extensions.DependencyInjection;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;
        """;

    #region Five kinds

    /// <summary>
    /// K3: [Register] is valid on records, which were dropped silently. A struct or record struct is n.a.
    /// (<c>AttributeUsage(Class)</c>: CS0592) and must neither crash nor emit anything.
    /// </summary>
    [Theory]
    [InlineData("class", true)]
    [InlineData("record class", true)]
    [InlineData("record", true)]
    [InlineData("struct", false)]
    [InlineData("record struct", false)]
    public void ItRegistersEveryClassKind(string keyword, bool registered)
    {
        var run = Run($$"""
            {{Preamble}}

            [Register(ServiceLifetime.Scoped)]
            public partial {{keyword}} Svc { }
            """);

        run.GeneratedErrors().Should().BeEmpty();
        if (registered)
        {
            run.Errors.Should().BeEmpty(run.ErrorText);
            run.AllSources.Should().Contain(".AddScoped<global::Demo.Svc>()");
        }
        else
        {
            run.Errors.Should().OnlyContain(d => d.Id == "CS0592", run.ErrorText);
            run.AllSources.Should().NotContain("global::Demo.Svc");
        }
    }

    /// <summary>A registered class nested in a parent of each kind, and in an interface.</summary>
    [Theory]
    [InlineData("class")]
    [InlineData("record class")]
    [InlineData("record")]
    [InlineData("struct")]
    [InlineData("record struct")]
    [InlineData("interface")]
    public void ItRegistersAClassNestedInEveryKind(string parentKeyword)
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }

            public {{parentKeyword}} Outer
            {
                [Register(typeof(IGreeter), ServiceLifetime.Singleton)]
                public record Greeter : IGreeter;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(".AddSingleton<global::Demo.IGreeter, global::Demo.Outer.Greeter>()");
    }

    [Fact]
    public void UnrelatedPartialsDoNotDisturbIt()
    {
        var run = Run(GeneratedCodeChecks.UnrelatedPartials, $$"""
            {{Preamble}}

            [Register(ServiceLifetime.Transient)]
            public class Svc { }
            """);

        run.GeneratedErrors().Should().BeEmpty();
        run.AllSources.Should().Contain(".AddTransient<global::Demo.Svc>()");
    }

    [Fact]
    public void TheGeneratedCodeIsWarningFreeUnderNullable()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepo<T> { }
            public interface IGreeter { }

            [Register(typeof(IRepo<>), ServiceLifetime.Scoped)]
            public class Repo<T> : IRepo<T> where T : class? { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public record Greeter : IGreeter
            {
                [RegisterFactory] public static Greeter Create() => new();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
    }

    #endregion

    #region Crashes (C2, C3)

    /// <summary>C2: a configuration attribute mid-typing threw out of a CompilationProvider step: no registrations.</summary>
    [Theory]
    [InlineData("DefaultAccessibility = )")]
    [InlineData("DefaultAccessibility = EGeneratedAccessibility.Nope)")]
    [InlineData("DefaultAccessibility = (EGeneratedAccessibility)42)")]
    [InlineData("DefaultMethodName = )")]
    public void AnIncompleteConfigurationDoesNotCrashIt(string arguments)
    {
        var run = Run($$"""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            [assembly: ServiceRegistrationConfiguration({{arguments}}]

            namespace Demo;

            [Register(ServiceLifetime.Scoped)]
            public class Other { }
            """);

        run.GeneratedErrors().Should().BeEmpty();
        run.AllSources.Should().Contain(".AddScoped<global::Demo.Other>()");
    }

    /// <summary>
    /// C3: an undefined lifetime threw KeyNotFoundException in the producer, so ServiceMethods.g.cs and every valid
    /// registration were lost. It is now REGISTER004 on that attribute only.
    /// </summary>
    [Fact]
    public void AnUndefinedLifetimeIsADiagnostic()
    {
        var run = Run($$"""
            {{Preamble}}

            [Register((ServiceLifetime)7)]
            public class Svc { }

            [Register(ServiceLifetime.Scoped)]
            public class Other { }
            """);

        run.Of("REGISTER004").Should().ContainSingle().Which.GetMessage().Should().Contain("7");
        run.Errors.Should().OnlyContain(d => d.Id == "REGISTER004", run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.Other>()");
        run.AllSources.Should().NotContain("global::Demo.Svc");
    }

    [Fact]
    public void AnUndefinedLifetimeAtAssemblyLevelIsADiagnostic()
    {
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            [assembly: Register(typeof(Demo.Svc), (ServiceLifetime)9)]

            namespace Demo;

            public class Svc { }
            """);

        run.Of("REGISTER004").Should().ContainSingle();
        run.GeneratedErrors().Should().BeEmpty();
    }

    #endregion

    #region Split partials (W5)

    [Fact]
    public void APartialClassSplitOverTwoFilesIsRegisteredOnce()
    {
        var run = Run(
            $$"""
            {{Preamble}}

            [Register(ServiceLifetime.Scoped)]
            public partial class Svc { }
            """,
            """
            namespace Demo;

            public partial class Svc { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        Occurrences(run.AllSources, ".AddScoped<global::Demo.Svc>()").Should().Be(1);
    }

    [Fact]
    public void EachPartOfASplitClassContributesItsOwnRegistrations()
    {
        var run = Run(
            $$"""
            {{Preamble}}

            public interface IReader { }
            public interface IWriter { }

            [Register(typeof(IReader), ServiceLifetime.Scoped)]
            public partial class Store : IReader { }
            """,
            """
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            [Register(typeof(IWriter), ServiceLifetime.Scoped)]
            public partial class Store : IWriter { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        Occurrences(run.AllSources, "global::Demo.IReader, global::Demo.Store").Should().Be(1);
        Occurrences(run.AllSources, "global::Demo.IWriter, global::Demo.Store").Should().Be(1);
    }

    #endregion

    #region Accessibility (W6, P11)

    [Theory]
    [InlineData("private class Svc { }", "private")]
    [InlineData("protected class Svc { }", "protected")]
    [InlineData("private protected class Svc { }", "private protected")]
    [InlineData("private record Svc;", "private")]
    public void ATypeTheGeneratedMethodCannotReachIsADiagnostic(string declaration, string accessibility)
    {
        var run = Run($$"""
            {{Preamble}}

            public class Outer
            {
                [Register(ServiceLifetime.Scoped)]
                {{declaration}}
            }

            [Register(ServiceLifetime.Scoped)]
            public class Other { }
            """);

        run.Of("REGISTER003").Should().ContainSingle().Which.GetMessage().Should().Contain(accessibility);
        run.Errors.Should().OnlyContain(d => d.Id == "REGISTER003", run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.Other>()");
    }

    [Fact]
    public void AClassInsideAPrivateParentIsADiagnostic()
    {
        var run = Run($$"""
            {{Preamble}}

            public class Outer
            {
                private class Middle
                {
                    [Register(ServiceLifetime.Scoped)]
                    public class Svc { }
                }
            }
            """);

        run.Of("REGISTER003").Should().ContainSingle().Which.GetMessage().Should().Contain("Middle");
        run.GeneratedErrors().Should().BeEmpty();
    }

    [Fact]
    public void AnInaccessibleServiceTypeIsADiagnostic()
    {
        var run = Run($$"""
            {{Preamble}}

            public class Outer
            {
                private interface IHidden { }

                [Register(typeof(IHidden), ServiceLifetime.Scoped)]
                internal class Svc : IHidden { }
            }
            """);

        run.Of("REGISTER003").Should().ContainSingle().Which.GetMessage().Should().Contain("IHidden");
        run.GeneratedErrors().Should().BeEmpty();
    }

    [Fact]
    public void AFileLocalClassIsADiagnostic()
    {
        var run = Run($$"""
            {{Preamble}}

            [Register(ServiceLifetime.Scoped)]
            file class Svc { }
            """);

        run.Of("REGISTER003").Should().ContainSingle().Which.GetMessage().Should().Contain("file-local");
        run.GeneratedErrors().Should().BeEmpty();
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("protected internal")]
    [InlineData("public")]
    public void AnAccessibilityTheAssemblyCanReachIsRegistered(string accessibility)
    {
        var run = Run($$"""
            {{Preamble}}

            public class Outer
            {
                [Register(ServiceLifetime.Scoped, "AddNested", EGeneratedAccessibility.Internal)]
                {{accessibility}} class Svc { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.Outer.Svc>()");
    }

    #endregion

    #region Open generics (P9, P10)

    /// <summary>
    /// P9: two open generics of the same arity share one generic method. Their type parameters were filled in by name,
    /// so the method declared T and the second registration used TItem: CS0246.
    /// </summary>
    [Fact]
    public void SameArityGenericsWithDifferentTypeParameterNamesShareOneMethod()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepo<T> { }
            public interface ICache<TItem> { }

            [Register(typeof(IRepo<>), ServiceLifetime.Scoped)]
            public class Repo<T> : IRepo<T> { }

            [Register(typeof(ICache<>), ServiceLifetime.Scoped)]
            public class Cache<TItem> : ICache<TItem> { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.IRepo<T>, global::Demo.Repo<T>>()");
        run.AllSources.Should().NotContain("TItem");
        run.AllSources.Should().Contain(".AddScoped<global::Demo.ICache<T>, global::Demo.Cache<T>>()");
    }

    [Fact]
    public void SameConstraintsUnderDifferentNamesCombineIntoOneMethod()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepo<T> { }
            public interface ICache<T> { }

            [Register(typeof(IRepo<>), ServiceLifetime.Scoped)]
            public class Repo<TEntity> : IRepo<TEntity> where TEntity : class, new() { }

            [Register(typeof(ICache<>), ServiceLifetime.Scoped)]
            public class Cache<TItem> : ICache<TItem> where TItem : class, new() { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.ICache<TEntity>, global::Demo.Cache<TEntity>>()");
        run.AllSources.Should().NotContain("_Cache", "one constraint signature needs one method, not a suffixed second one");
    }

    /// <summary>P10: the service's type arguments come from the implemented interface, in its order.</summary>
    [Fact]
    public void ReorderedTypeParametersFollowTheInterface()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IMap<TKey, TValue> { }

            [Register(typeof(IMap<,>), ServiceLifetime.Scoped)]
            public class Map<TValue, TKey> : IMap<TKey, TValue> { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.IMap<TKey, TValue>, global::Demo.Map<TValue, TKey>>()");
    }

    [Fact]
    public void APartiallyClosedInterfaceKeepsItsClosedArgument()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IMap<TKey, TValue> { }

            [Register(typeof(IMap<,>), ServiceLifetime.Scoped)]
            public class StringMap<TValue> : IMap<string, TValue> { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.IMap<string, TValue>, global::Demo.StringMap<TValue>>()");
    }

    [Fact]
    public void AClosedImplementationOfAnOpenServiceIsRegisteredClosed()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepository<T> { }

            [Register(typeof(IRepository<>), ServiceLifetime.Scoped)]
            public class PersonRepository : IRepository<string> { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.IRepository<string>, global::Demo.PersonRepository>()");
    }

    [Fact]
    public void AGenericClassRegisteredAgainstItselfGetsAGenericMethod()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IGreeter { }

            [Register(ServiceLifetime.Scoped)]
            public class Box<T> { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter<T> : IGreeter { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain(".AddScoped<global::Demo.Box<T>>()");
        run.AllSources.Should().Contain(".AddScoped<global::Demo.IGreeter, global::Demo.Greeter<T>>()");
    }

    /// <summary>P10: a mapping the generated method cannot express is a diagnostic, not broken code.</summary>
    [Fact]
    public void ATypeParameterOfAContainingTypeIsADiagnostic()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IRepo<T> { }

            public class Outer<T>
            {
                [Register(typeof(IRepo<>), ServiceLifetime.Scoped)]
                public class Repo : IRepo<T> { }
            }
            """);

        run.Of("REGISTER005").Should().ContainSingle().Which.GetMessage().Should().Contain("'T'");
        run.GeneratedErrors().Should().BeEmpty();
    }

    [Fact]
    public void AnAmbiguousOpenServiceIsADiagnostic()
    {
        var run = Run($$"""
            {{Preamble}}

            public interface IFoo<T> { }

            [Register(typeof(IFoo<>), ServiceLifetime.Scoped)]
            public class Foo<T> : IFoo<T>, IFoo<int> { }
            """);

        run.Of("REGISTER005").Should().ContainSingle();
        run.GeneratedErrors().Should().BeEmpty();
    }

    /// <summary>The reordered registration actually resolves through the container, not only compiles.</summary>
    [Fact]
    public void AReorderedRegistrationResolves()
    {
        var run = GeneratorHarness.Run(Generator, ["""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;
            using TestRoot;

            namespace Demo;

            public interface IMap<TKey, TValue> { }

            [Register(typeof(IMap<,>), ServiceLifetime.Singleton)]
            public class Map<TValue, TKey> : IMap<TKey, TValue> { }

            public static class Probe
            {
                public static object? Resolve()
                    => new ServiceCollection().AddTestInput<int, string>().BuildServiceProvider().GetService(typeof(IMap<string, int>));
            }
            """],
            referenceTypes: [typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions)]);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("AddTestInput<TValue, TKey>");

        var resolved = run.Emit().GetGeneratedType("Demo.Probe").GetMethod("Resolve")!.Invoke(null, null);
        resolved.Should().NotBeNull();
        resolved!.GetType().FullName.Should().StartWith("Demo.Map`2[[System.Int32");
    }

    #endregion

    #region Metadata names (P12)

    [Fact]
    public void ASameNamedAttributeInAnotherNamespaceIsIgnored()
    {
        var run = Run("""
            using Microsoft.Extensions.DependencyInjection;

            namespace Other
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public class RegisterAttribute : System.Attribute
                {
                    public RegisterAttribute(ServiceLifetime lifetime, string methodNameHint = null, int accessibility = 0) { }
                }
            }

            namespace Demo
            {
                [Other.Register(ServiceLifetime.Scoped)]
                public class Svc { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("global::Demo.Svc");
    }

    #endregion

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
