using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// MapperGenerator over every type kind (#193 and the PRD-TypeKindRobustness findings K6, K6', W8–W11 and P17–P20).
/// </summary>
/// <remarks>
/// The Mapper emits no partial of a user type, so the five-kind matrix is about what it maps: every pair of class,
/// record class, record (nominal, positional and init-only), struct and record struct, in both directions. Each case
/// compiles, recompiles warning-free under <c>#nullable enable</c>, and runs: a mapper that compiles but copies
/// nothing would pass every textual assertion.
/// </remarks>
public class MapperTypeKindTests
{
    private static GeneratorRun Run(params string[] sources)
        => GeneratorHarness.Run("MapperGenerator", sources, generatorAssemblyName: "MintPlayer.Mapper");

    /// <summary>A shape for a type with an <c>int Id</c> and a <c>string Name</c>.</summary>
    /// <param name="Declaration">The declaration, with <c>{0}</c> for the type name.</param>
    /// <param name="Construction">An expression creating Id 7 and Name "Ann", with <c>{0}</c> for the type name.</param>
    /// <param name="Copyable">Whether the copy overload <c>MapTo(input, output)</c> exists with this shape as the output.</param>
    public sealed record Shape(string Name, string Declaration, string Construction, bool Copyable)
    {
        public override string ToString() => Name;
    }

    private static readonly Shape[] Shapes =
    [
        new("class", "public class {0} { public int Id { get; set; } public string Name { get; set; } = \"\"; }", "new {0} { Id = 7, Name = \"Ann\" }", true),
        new("record class", "public record class {0} { public int Id { get; set; } public string Name { get; set; } = \"\"; }", "new {0} { Id = 7, Name = \"Ann\" }", true),
        new("record", "public record {0} { public int Id { get; set; } public string Name { get; set; } = \"\"; }", "new {0} { Id = 7, Name = \"Ann\" }", true),
        new("init-only record", "public record {0} { public int Id { get; init; } public string Name { get; init; } = \"\"; }", "new {0} { Id = 7, Name = \"Ann\" }", false),
        new("positional record", "public record {0}(int Id, string Name);", "new {0}(7, \"Ann\")", false),
        new("struct", "public struct {0} { public int Id { get; set; } public string Name { get; set; } }", "new {0} { Id = 7, Name = \"Ann\" }", false),
        new("record struct", "public record struct {0} { public int Id { get; set; } public string Name { get; set; } }", "new {0} { Id = 7, Name = \"Ann\" }", false),
        new("positional record struct", "public record struct {0}(int Id, string Name);", "new {0}(7, \"Ann\")", false),
    ];

    public static TheoryData<Shape, Shape> ShapePairs()
    {
        var data = new TheoryData<Shape, Shape>();
        foreach (var declared in Shapes)
            foreach (var mapping in Shapes)
                data.Add(declared, mapping);
        return data;
    }

    /// <summary>
    /// K6, K6', P17 and the init-only empty copy body: every pair compiles warning-free, maps the values both ways
    /// through the extension methods and through <c>IMapper</c>, and has a copy overload exactly when there is a
    /// reference-type output with something to copy into.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapePairs))]
    public void EveryPairOfKinds_MapsBothWays(Shape declared, Shape mapping)
    {
        var copy = mapping.Copyable
            ? """
                var target = new PersonDto();
                p.MapToPersonDto(target);
                result += $"|{target.Id}|{target.Name}";
            """
            : string.Empty;

        var run = Run($$"""
            #nullable enable
            using MintPlayer.Mapper.Attributes;
            using TestRoot;

            namespace Demo;

            {{mapping.Declaration.Replace("{0}", "PersonDto")}}

            [GenerateMapper(typeof(PersonDto))]
            {{declared.Declaration.Replace("{0}", "Person")}}

            public static class Probe
            {
                public static string Run()
                {
                    var p = {{declared.Construction.Replace("{0}", "Person")}};
                    PersonDto d = p.MapToPersonDto();
                    Person back = d.MapToPerson();
                    PersonDto viaMapper = new TestRoot.Mapper().Map<Person, PersonDto>(p)!;
                    var result = $"{d.Id}|{d.Name}|{back.Id}|{back.Name}|{viaMapper.Id}";
                    {{copy}}
                    return result;
                }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();

        var assembly = run.Emit();
        var result = (string)assembly.GetGeneratedType("Demo.Probe").GetMethod("Run")!.Invoke(null, null)!;
        result.Should().Be(mapping.Copyable ? "7|Ann|7|Ann|7|7|Ann" : "7|Ann|7|Ann|7");

        CopyOverloads(assembly, "MapToPersonDto").Should().Be(mapping.Copyable ? 1 : 0,
            "a copy overload exists only for a reference-type output with a settable mapped property");
        CopyOverloads(assembly, "MapToPerson").Should().Be(declared.Copyable ? 1 : 0);
    }

    public static TheoryData<string> ParentKinds() => new() { "class", "record class", "record", "struct", "record struct", "interface" };

    /// <summary>Mapped types nested in a parent of each kind, and in an interface.</summary>
    [Theory]
    [MemberData(nameof(ParentKinds))]
    public void TypesNestedInAParentOfEachKind_AreMapped(string parentKind)
    {
        var run = Run($$"""
            #nullable enable
            using MintPlayer.Mapper.Attributes;
            using TestRoot;

            namespace Demo;

            public {{parentKind}} Holder
            {
                public class PersonDto { public int Id { get; set; } }

                [GenerateMapper(typeof(PersonDto))]
                public record struct Person(int Id);
            }

            public static class Probe
            {
                public static int Run() => new Holder.Person(7).MapToPersonDto().MapToPerson().Id;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
        run.Emit().GetGeneratedType("Demo.Probe").GetMethod("Run")!.Invoke(null, null).Should().Be(7);
    }

    /// <summary>
    /// The #193 case: unrelated records, structs and interfaces with a <c>[GeneratedRegex]</c> partial method, beside
    /// a partial static class whose two parts each hold a <c>[MapperConversion]</c> (registered twice before, when
    /// static classes were scanned per declaration).
    /// </summary>
    [Fact]
    public void UnrelatedCode_DoesNotDisturbAValidUse()
    {
        var run = Run(GeneratedCodeChecks.UnrelatedPartials, """
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public static partial class Conversions { [MapperConversion] public static string IntToString(int x) => x.ToString(); }
            public static partial class Conversions { [MapperConversion] public static int StringToInt(string x) => int.Parse(x); }

            public class PersonDto { public string Id { get; set; } = ""; }

            [GenerateMapper(typeof(PersonDto))]
            public class Person { public int Id { get; set; } }
            """);

        run.CrashDiagnostics.Should().BeEmpty();
        run.GeneratedErrors().Should().BeEmpty();
        run.AllSources.Should().Contain("MapToPersonDto");
        CountOf(run.SourceFor("Mappers.g.cs")!, "Conversions.IntToString(").Should().Be(1);
        CountOf(run.SourceFor("Mappers.g.cs")!, "Conversions.StringToInt(").Should().Be(1);
    }

    /// <summary>W8: an attribute on the error path left half-filled models in MapperEntrypoint.g.cs.</summary>
    [Fact]
    public void AnAttributeOnTheErrorPath_LeavesTheEntrypointCompilable()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto { public int Id { get; set; } }

            [GenerateMapper(typeof(PersonDto), typeof(Person))]
            public class Person { public int Id { get; set; } }

            [GenerateMapper(typeof(PersonDto))]
            public class Valid { public int Id { get; set; } }
            """);

        run.Of("MAPC002").Should().ContainSingle();
        run.Errors.Select(d => d.Id).Should().OnlyContain(id => id == "MAPC002", run.ErrorText);
        run.SourceFor("MapperEntrypoint.g.cs").Should().Contain("case global::Demo.Valid sourceValue:");
    }

    /// <summary>A mid-typing <c>typeof</c> is the compiler's error to report; the generator emits nothing broken.</summary>
    [Fact]
    public void AnUnboundTypeof_EmitsNothingBroken()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            [assembly: GenerateMapper(typeof(Demo.Missing), typeof(Demo.PersonDto), "MapTo")]

            namespace Demo;

            public class PersonDto { public int Id { get; set; } }

            [GenerateMapper(typeof(Pers))]
            public class Person { public int Id { get; set; } }
            """);

        run.CrashDiagnostics.Should().BeEmpty();
        run.GeneratedErrors().Should().BeEmpty();
        run.AllSources.Should().NotContain("Missing").And.NotContain("Pers)");
    }

    /// <summary>W9: a mapped base and a mapped derived class. <c>case Base</c> before <c>case Derived</c> was CS8120.</summary>
    [Fact]
    public void ABaseAndADerivedMappedType_DispatchToTheDerivedMapper()
    {
        var run = Run("""
            #nullable enable
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto { public int Id { get; set; } }
            public class EmployeeDto { public int Id { get; set; } }

            [GenerateMapper(typeof(PersonDto))]
            public class Person { public int Id { get; set; } }

            [GenerateMapper(typeof(EmployeeDto))]
            public class Employee : Person { }

            public static class Probe
            {
                public static string Run()
                {
                    var mapper = new TestRoot.Mapper();
                    var dto = new EmployeeDto();
                    mapper.Map<Person, EmployeeDto>(new Employee { Id = 3 }, dto);
                    return $"{mapper.Map<Person, EmployeeDto>(new Employee { Id = 7 })!.Id}|{dto.Id}";
                }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
        run.Emit().GetGeneratedType("Demo.Probe").GetMethod("Run")!.Invoke(null, null).Should().Be("7|3");
    }

    /// <summary>W10: an interface, abstract or static destination cannot be created with <c>new</c>.</summary>
    [Theory]
    [InlineData("public interface IPersonDto { int Id { get; set; } }", "IPersonDto", "it is an interface")]
    [InlineData("public abstract class PersonDtoBase { public int Id { get; set; } }", "PersonDtoBase", "it is abstract")]
    [InlineData("public static class Holder { }", "Holder", "it is static")]
    [InlineData("public enum Kind { A }", "Kind", "it is not a class or struct")]
    [InlineData("public class Box<T> { public int Id { get; set; } }", "Box<>", "it is an open generic type")]
    public void AnUninstantiableDestination_ReportsMapc005(string declaration, string typeofArgument, string reason)
    {
        var run = Run($$"""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            {{declaration}}

            [GenerateMapper(typeof({{typeofArgument}}))]
            public class Person { public int Id { get; set; } }
            """);

        run.Of("MAPC005").Should().ContainSingle().Which.GetMessage().Should().Contain(reason);
        run.Errors.Select(d => d.Id).Should().OnlyContain(id => id == "MAPC005", run.ErrorText);
    }

    /// <summary>W10 for the attributed type itself: an abstract class carrying <c>[GenerateMapper]</c>.</summary>
    [Fact]
    public void AnAbstractDeclaredType_ReportsMapc005()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto { public int Id { get; set; } }

            [GenerateMapper(typeof(PersonDto))]
            public abstract class Person { public int Id { get; set; } }
            """);

        run.Of("MAPC005").Should().ContainSingle();
        run.Errors.Select(d => d.Id).Should().OnlyContain(id => id == "MAPC005", run.ErrorText);
    }

    /// <summary>W11: a conversion in a generic static class was emitted as <c>Conv&lt;T&gt;.ToS(...)</c>, CS0246.</summary>
    [Fact]
    public void AConversionInAGenericStaticClass_ReportsMapc007()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public static class Conv<T> { [MapperConversion] public static string ToS(int x) => x.ToString(); }
            """);

        run.Of("MAPC007").Should().ContainSingle().Which.GetMessage().Should().Contain("generic");
        run.Errors.Select(d => d.Id).Should().OnlyContain(id => id == "MAPC007", run.ErrorText);
        run.AllSources.Should().NotContain("ToS(");
    }

    /// <summary>A conversion in a private nested static class: CS0122 in generated code before.</summary>
    [Fact]
    public void AConversionInAPrivateNestedClass_ReportsMapc007()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class Outer
            {
                private static class Conv { [MapperConversion] public static string ToS(int x) => x.ToString(); }
            }
            """);

        run.Of("MAPC007").Should().ContainSingle();
        run.Errors.Select(d => d.Id).Should().OnlyContain(id => id == "MAPC007", run.ErrorText);
    }

    /// <summary>P18: a class-level mapper between public types was always <c>internal</c>.</summary>
    [Fact]
    public void AClassLevelMapperBetweenPublicTypes_IsPublic()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto { public int Id { get; set; } }

            [GenerateMapper(typeof(PersonDto))]
            public class Person { public int Id { get; set; } }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("public static global::Demo.PersonDto? MapToPersonDto(this global::Demo.Person? input)");
        run.AllSources.Should().NotContain("internal static");
    }

    /// <summary>P18: a public type nested in an internal one is not public, so its mapper must not be either (CS0050).</summary>
    [Fact]
    public void APublicTypeNestedInAnInternalType_GetsAnInternalMapper()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto { public int Id { get; set; } }

            internal class Holder
            {
                [GenerateMapper(typeof(PersonDto))]
                public class Person { public int Id { get; set; } }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("internal static global::Demo.PersonDto? MapToPersonDto(");
        run.AllSources.Should().NotContain("public static global::Demo.PersonDto");
    }

    /// <summary>P20: a private nested type gave CS0122 ×8 in generated code and no diagnostic.</summary>
    [Theory]
    [InlineData("private")]
    [InlineData("protected")]
    [InlineData("private protected")]
    public void AnInaccessibleNestedType_ReportsMapc006(string accessibility)
    {
        var run = Run($$"""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class Outer
            {
                [GenerateMapper(typeof(Dto))]
                {{accessibility}} class Ent { public int Id { get; set; } }
                public class Dto { public int Id { get; set; } }
            }
            """);

        run.Of("MAPC006").Should().ContainSingle().Which.GetMessage().Should().Contain("Ent");
        run.Errors.Select(d => d.Id).Should().OnlyContain(id => id == "MAPC006", run.ErrorText);
    }

    /// <summary>P20 for a file-local type: re-declaring it in the generated file names a different type.</summary>
    [Fact]
    public void AFileLocalType_ReportsMapc006()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class Dto { public int Id { get; set; } }

            [GenerateMapper(typeof(Dto))]
            file class Ent { public int Id { get; set; } }
            """);

        run.Of("MAPC006").Should().ContainSingle();
        run.Errors.Select(d => d.Id).Should().OnlyContain(id => id == "MAPC006", run.ErrorText);
    }

    /// <summary>
    /// P17 at the call site: with <c>[NotNullIfNotNull("input")]</c> a non-null input gives a non-null result, and a
    /// nullable input is accepted; neither warns. A nested non-null property and the sequence overload are included.
    /// </summary>
    [Fact]
    public void NullableCallers_GetNoWarnings()
    {
        var run = Run("""
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using MintPlayer.Mapper.Attributes;
            using TestRoot;

            namespace Demo;

            public class AddressDto { public string Street { get; set; } = ""; }

            [GenerateMapper(typeof(AddressDto))]
            public class Address { public string Street { get; set; } = ""; }

            public class PersonDto
            {
                public AddressDto Address { get; set; } = new();
                public string? Nick { get; set; }
                public List<string> Notes { get; set; } = new();
                public string[] Tags { get; set; } = [];
                public List<AddressDto> Others { get; set; } = new();
                public AddressDto[] More { get; set; } = [];
                public List<AddressDto>? Maybe { get; set; }
            }

            [GenerateMapper(typeof(PersonDto))]
            public class Person
            {
                public Address Address { get; set; } = new();
                public string? Nick { get; set; }
                public List<string> Notes { get; set; } = new();
                public string[] Tags { get; set; } = [];
                public List<Address> Others { get; set; } = new();
                public Address[] More { get; set; } = [];
                public List<Address>? Maybe { get; set; }
            }

            public static class Caller
            {
                public static PersonDto Map(Person p) => p.MapToPersonDto();
                public static PersonDto? MapMaybe(Person? p) => p.MapToPersonDto();
                public static List<PersonDto> MapAll(IEnumerable<Person> people) => people.MapToPersonDto().ToList();
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
        run.UpdatedCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Warning && d.Id.StartsWith("CS86", StringComparison.Ordinal))
            .Should().BeEmpty("a non-null input maps to a non-null output, and a nullable input is accepted");
        run.AllSources.Should().Contain("[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(\"input\")]");
    }

    /// <summary>
    /// The CS0162 half of P17: without a <c>[MapperConversion]</c>, ConvertProperty's switch had only a throwing
    /// default, and the return after it was unreachable. The entrypoint with no mapped type had the same shape.
    /// </summary>
    [Fact]
    public void WithoutConversionsOrMappedTypes_NoUnreachableCode()
    {
        var run = Run("""
            namespace Demo;

            public class Unrelated { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
    }

    private static int CopyOverloads(Assembly assembly, string methodName)
        => assembly.GetGeneratedType("TestRoot.MapperExtensions")
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Count(m => m.Name == methodName && m.ReturnType == typeof(void));

    private static int CountOf(string text, string value)
        => (text.Length - text.Replace(value, string.Empty).Length) / value.Length;
}
