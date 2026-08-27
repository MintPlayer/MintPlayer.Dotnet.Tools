using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// Layer 1 for the remaining generators. Every test asserts <c>Errors</c> is empty, which is
/// the assertion that makes the rest meaningful: without it a generator that emits
/// syntactically invalid C# still satisfies every "contains" check.
/// </summary>
public class InjectSourceGeneratorTests
{
    private static GeneratorRun Run(string source)
        => GeneratorHarness.Run("InjectSourceGenerator", [source]);

    [Fact]
    public void ItGeneratesAConstructorForInjectedFields()
    {
        var run = Run("""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IService { }

            public partial class Consumer
            {
                [Inject] private readonly IService service;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Consumer");
        run.AllSources.Should().Contain("IService");
    }

    [Fact]
    public void ItInjectsSeveralDependencies()
    {
        var run = Run("""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IA { }
            public interface IB { }

            public partial class Consumer
            {
                [Inject] private readonly IA a;
                [Inject] private readonly IB b;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("IA");
        run.AllSources.Should().Contain("IB");
    }

    [Fact]
    public void ItLeavesUndecoratedClassesAlone()
    {
        var run = Run("""
            namespace Demo;

            public partial class Plain
            {
                private readonly string value = "x";
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    /// <summary>
    /// Characterization, and a defect recorded in docs/PRD-TestCoverage.md rather than fixed:
    /// the generator emits a partial declaration for a class that is not declared partial, so
    /// the consumer gets CS0260 ("Missing partial modifier") pointing at their own class with
    /// no explanation. A diagnostic naming the [Inject] field would be far better, but adding
    /// one is a feature decision about the generator contract, not a coverage change.
    /// </summary>
    [Fact]
    public void ANonPartialClass_EmitsAPartialAnywayAndBreaksTheConsumerBuild()
    {
        var run = Run("""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IService { }

            public class NotPartial
            {
                [Inject] private readonly IService service;
            }
            """);

        run.Errors.Should().Contain(d => d.Id == "CS0260");
    }

    [Fact]
    public void ItHandlesANestedClass()
    {
        var run = Run("""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IService { }

            public partial class Outer
            {
                public partial class Inner
                {
                    [Inject] private readonly IService service;
                }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }
}

public class ClassNamesSourceGeneratorTests
{
    [Fact]
    public void ItEmitsClassNameConstants()
    {
        var run = GeneratorHarness.Run("ClassNamesSourceGenerator", ["""
            namespace Demo;

            public class Alpha { }
            public class Beta { }
            """]);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void OnAnEmptyCompilation_ItDoesNotFail()
    {
        var run = GeneratorHarness.Run("ClassNamesSourceGenerator", ["// nothing"]);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void WithoutARootNamespace_ItStillEmitsCompilableCode()
    {
        var run = GeneratorHarness.Run("ClassNamesSourceGenerator", ["""
            namespace Demo;

            public class Alpha { }
            """], rootNamespace: null);

        run.Errors.Should().NotContain(d => d.Id == "CS1001", run.ErrorText);
    }
}

public class DescriptionSourceGeneratorTests
{
    [Fact]
    public void ItEmitsDescriptionsForDecoratedEnumMembers()
    {
        var run = GeneratorHarness.Run("DescriptionSourceGenerator", ["""
            using System.ComponentModel;

            namespace Demo;

            public enum Colour
            {
                [Description("Bright red")] Red,
                [Description("Deep blue")] Blue,
            }
            """]);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void OnAnEnumWithNoDescriptions_ItDoesNotFail()
    {
        var run = GeneratorHarness.Run("DescriptionSourceGenerator", ["""
            namespace Demo;

            public enum Colour { Red, Blue }
            """]);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }
}

public class GenericMethodSourceGeneratorTests
{
    [Fact]
    public void OnAPlainCompilation_ItDoesNotFail()
    {
        var run = GeneratorHarness.Run("GenericMethodSourceGenerator", ["""
            namespace Demo;

            public class Thing { }
            """]);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }
}

public class MapperGeneratorTests
{
    private static GeneratorRun Run(string source)
        => GeneratorHarness.Run("MapperGenerator", [source], generatorAssemblyName: "MintPlayer.Mapper");

    [Fact]
    public void ItGeneratesAMapperForMatchingProperties()
    {
        var run = Run("""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            [GenerateMapper(typeof(PersonDto))]
            public class Person
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void ItLeavesUndecoratedTypesAlone()
    {
        var run = Run("""
            namespace Demo;

            public class Person { public int Id { get; set; } }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    /// <summary>
    /// MapperGenerator.Producer passes RootNamespace! into its emitted namespace, which is one
    /// of the sites that produced a bare `namespace` and CS1001 before the Producer base
    /// started normalizing it.
    /// </summary>
    [Fact]
    public void WithoutARootNamespace_ItStillEmitsCompilableCode()
    {
        var run = GeneratorHarness.Run("MapperGenerator", ["""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto { public int Id { get; set; } }

            [GenerateMapper(typeof(PersonDto))]
            public class Person { public int Id { get; set; } }
            """], rootNamespace: null, generatorAssemblyName: "MintPlayer.Mapper");

        run.Errors.Should().NotContain(d => d.Id == "CS1001", run.ErrorText);
    }
}

public class CliCommandSourceGeneratorTests
{
    private static GeneratorRun Run(string source, string? rootNamespace = "TestRoot")
        => GeneratorHarness.Run("CliCommandSourceGenerator", [source], rootNamespace,
            generatorAssemblyName: "MintPlayer.CliGenerator");

    [Fact]
    public void ItBuildsACommandTree()
    {
        var run = Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliRootCommand("Demo tool")]
            public partial class RootCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }

            [CliCommand("build")]
            public partial class BuildCommand : ICliCommand
            {
                [CliOption("--verbose")] public bool Verbose { get; set; }

                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void OnACompilationWithNoCommands_ItDoesNotFail()
    {
        var run = Run("""
            namespace Demo;

            public class NotACommand { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void WithoutARootNamespace_ItStillEmitsCompilableCode()
    {
        var run = Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliRootCommand("Demo tool")]
            public partial class RootCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """, rootNamespace: null);

        run.Errors.Should().NotContain(d => d.Id == "CS1001", run.ErrorText);
    }
}

public class ValueComparerGeneratorTests
{
    private static GeneratorRun Run(string source, string? rootNamespace = "TestRoot")
        => GeneratorHarness.Run("ValueComparerGenerator", [source], rootNamespace,
            generatorAssemblyName: "MintPlayer.ValueComparerGenerator");

    [Fact]
    public void ItGeneratesAComparerForADecoratedHierarchy()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [AutoValueComparer]
            public abstract partial class Shape
            {
                public string Name { get; set; } = "";
            }

            public partial class Circle : Shape
            {
                public double Radius { get; set; }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void ItHonoursComparerIgnore()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [AutoValueComparer]
            public abstract partial class Shape
            {
                public string Name { get; set; } = "";
            }

            public partial class Circle : Shape
            {
                [ComparerIgnore] public double Radius { get; set; }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void OnAnUndecoratedHierarchy_ItDoesNotFail()
    {
        var run = Run("""
            namespace Demo;

            public abstract class Shape { }
            public class Circle : Shape { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void WithoutARootNamespace_ItStillEmitsCompilableCode()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [AutoValueComparer]
            public abstract partial class Shape { public string Name { get; set; } = ""; }

            public partial class Circle : Shape { }
            """, rootNamespace: null);

        run.Errors.Should().NotContain(d => d.Id == "CS1001", run.ErrorText);
    }
}

public class JoinMethodGeneratorTests
{
    [Fact]
    public void OnAPlainCompilation_ItDoesNotFail()
    {
        var run = GeneratorHarness.Run("JoinMethodGenerator", ["""
            namespace Demo;

            public class Thing { }
            """], generatorAssemblyName: "MintPlayer.ValueComparerGenerator");

        run.Errors.Should().BeEmpty(run.ErrorText);
    }
}
