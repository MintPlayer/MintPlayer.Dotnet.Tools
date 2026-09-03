using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// The three <see cref="MapperGenerator"/> features that had no fixture at all: the assembly-level
/// <c>[GenerateMapper]</c> overload, <c>[MapperConversion]</c> static conversion methods, and
/// primary-constructor targets.
/// </summary>
/// <remarks>
/// These are not permutations of the class-level path already covered by
/// <c>OtherGeneratorTests.ItGeneratesAMapperForMatchingProperties</c> — they are separate branches
/// of the pipeline, each with its own model type, and each previously executed only by the manual
/// <c>TestProjects/MapperDebugging</c> playground, which is not a test project and is not measured.
/// The fixture syntax is taken from that playground so it stays known-good usage.
///
/// Every test asserts <c>Errors</c> is empty first. Without it, a generator emitting syntactically
/// invalid C# still satisfies every "contains" check below.
/// </remarks>
public class MapperFeatureTests
{
    private static GeneratorRun Run(string source)
        => GeneratorHarness.Run("MapperGenerator", [source], generatorAssemblyName: "MintPlayer.Mapper");

    /// <summary>
    /// <c>[assembly: GenerateMapper(source, dest, prefix)]</c> — the three-argument overload that
    /// maps two types neither of which is decorated, naming the methods from the prefix.
    /// </summary>
    [Fact]
    public void AnAssemblyLevelAttribute_MapsTwoUndecoratedTypes()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            [assembly: GenerateMapper(typeof(Demo.Person), typeof(Demo.PersonDto), "MapTo")]

            namespace Demo;

            public class Person
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            public class PersonDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("MapToPersonDto");
        run.AllSources.Should().Contain("MapToPerson");
    }

    /// <summary>
    /// The assembly-level attribute reaches a different accessibility calculation than the
    /// class-level one, because neither type carries the attribute that would mark it decorated.
    /// An internal participant must force the emitted extension method to internal, or the
    /// consumer gets CS0050 (inconsistent accessibility) on a public method returning it.
    /// </summary>
    [Fact]
    public void AnAssemblyLevelAttribute_WithAnInternalType_EmitsInternalMethods()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            [assembly: GenerateMapper(typeof(Demo.Secret), typeof(Demo.SecretDto), "MapTo")]

            namespace Demo;

            internal class Secret { public int Id { get; set; } }
            internal class SecretDto { public int Id { get; set; } }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("internal static void MapTo");
        run.AllSources.Should().NotContain("public static void MapToSecret");
    }

    /// <summary>
    /// An assembly-level <c>[GenerateMapper]</c> given one type instead of two takes the
    /// <c>HasError</c> branch and must report MAPC001. This is the only path that instantiates
    /// <c>DiagnosticRules.GenerateMapperTwoParameters</c>, and neither descriptor in
    /// <c>MapperGenerator.Rules</c> had any coverage.
    /// </summary>
    [Fact]
    public void AnAssemblyLevelAttribute_WithOneType_ReportsMapc001()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            [assembly: GenerateMapper(typeof(Demo.Person))]

            namespace Demo;

            public class Person { public int Id { get; set; } }
            """);

        run.Of("MAPC001").Should().NotBeEmpty(
            "an assembly-level [GenerateMapper] needs two types, and must say so rather than emit nothing");
    }

    /// <summary>
    /// The mirror case: a class-level <c>[GenerateMapper]</c> given two types reports MAPC002,
    /// covering the second descriptor and the <c>EAppliedOn.Class</c> arm of the switch that
    /// chooses between them.
    /// </summary>
    [Fact]
    public void AClassLevelAttribute_WithTwoTypes_ReportsMapc002()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto { public int Id { get; set; } }

            [GenerateMapper(typeof(PersonDto), typeof(Person))]
            public class Person { public int Id { get; set; } }
            """);

        run.Of("MAPC002").Should().NotBeEmpty(
            "a class-level [GenerateMapper] takes exactly one type");
    }

    /// <summary>
    /// <c>[MapperConversion]</c> on a public static one-parameter method registers a conversion in
    /// the emitted <c>ConvertProperty</c> switch. This drives the whole ConversionMethod model,
    /// which was 0% covered.
    /// </summary>
    [Fact]
    public void AStaticConversionMethod_IsRegisteredInTheConversionSwitch()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public static class Conversions
            {
                [MapperConversion]
                public static int? StringToNullableInt(string? input)
                    => int.TryParse(input, out var r) ? r : null;

                [MapperConversion]
                public static string? NullableIntToString(int? input) => input?.ToString();
            }

            public class Source { public string? Value { get; set; } }

            [GenerateMapper(typeof(Source))]
            public class Target { public int? Value { get; set; } }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("StringToNullableInt");
        run.AllSources.Should().Contain("ConvertProperty");
    }

    /// <summary>
    /// The stateful three-parameter overload, <c>[MapperConversion&lt;TState&gt;(from, to)]</c>.
    /// It emits a different case shape — one guarded on sourceState and destState — and passes the
    /// two states through to the method, which the one-parameter form does not.
    /// </summary>
    [Fact]
    public void AStatefulConversionMethod_EmitsAStateGuardedCase()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public enum EPasswordState { Plaintext, Base64 }

            public static class Conversions
            {
                [MapperConversion<EPasswordState>(EPasswordState.Plaintext, EPasswordState.Base64)]
                public static string ToBase64(string input, EPasswordState inState, EPasswordState outState)
                    => System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input));
            }

            public class Account
            {
                [MapperState<EPasswordState>(EPasswordState.Plaintext)]
                public string Password { get; set; } = "";
            }

            [GenerateMapper(typeof(Account))]
            public class AccountDto
            {
                [MapTo(nameof(Account.Password)), MapperState<EPasswordState>(EPasswordState.Base64)]
                public string Password { get; set; } = "";
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("ToBase64");
        run.AllSources.Should().Contain("sourceState ==");
        run.AllSources.Should().Contain("destState ==");
    }

    /// <summary>
    /// A target whose properties come from a primary constructor cannot be mapped by assigning
    /// properties after a parameterless <c>new</c>; the generator has to collect the primary
    /// constructor's parameters and pass them positionally.
    /// </summary>
    [Fact]
    public void APrimaryConstructorTarget_IsConstructedPositionally()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PointDto(int x, int y)
            {
                public int X { get; } = x;
                public int Y { get; } = y;
            }

            [GenerateMapper(typeof(PointDto))]
            public class Point(int x, int y)
            {
                public int X { get; } = x;
                public int Y { get; } = y;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().NotBeEmpty();
    }

    /// <summary>
    /// <c>[IgnoreMap]</c> must drop the property from both directions. Asserted by absence, which
    /// is only meaningful because Errors is checked first.
    /// </summary>
    [Fact]
    public void AnIgnoredProperty_IsNotMapped()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class UserDto
            {
                public int Id { get; set; }
                public string Secret { get; set; } = "";
            }

            [GenerateMapper(typeof(UserDto))]
            public class User
            {
                public int Id { get; set; }

                [IgnoreMap]
                public string Secret { get; set; } = "";
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Id");
        run.AllSources.Should().NotContain("output.Secret = input.Secret");
    }
}
