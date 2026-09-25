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

    /// <summary>
    /// A root command with one subcommand carrying one option, asserted all the way to the
    /// emitted tree.
    /// </summary>
    /// <remarks>
    /// This test used to declare <c>BuildCommand</c> with <c>[CliCommand("build")]</c> alone and
    /// then assert only that <c>Errors</c> was empty and that something had been generated. Both
    /// held — but a non-nested command without <c>[CliParentCommand]</c> is silently dropped, so
    /// the generated tree contained the root and nothing else. The test named for building a
    /// command tree never checked that the tree had been built, and passed for years while the
    /// subcommand it declared was thrown away.
    ///
    /// It now declares the parent explicitly and asserts the subcommand and its option are
    /// present. The dropping behaviour itself is pinned separately by
    /// <c>CliCommandFeatureTests.AnOrphanCommand_IsSilentlyDroppedFromTheTree</c>.
    /// </remarks>
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
            [CliParentCommand(typeof(RootCommand))]
            public partial class BuildCommand : ICliCommand
            {
                [CliOption("--verbose")] public bool Verbose { get; set; }

                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("\"build\"", "the subcommand belongs in the emitted tree");
        run.AllSources.Should().Contain("--verbose", "its option belongs on it");
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
    /// <summary>The one file every model's equality goes into (PRD-EqualitySingleFile).</summary>
    private const string EqualityFile = "GeneratedEquality.g.cs";

    private static GeneratorRun Run(string source, string? rootNamespace = "TestRoot")
        => RunFiles([source], rootNamespace);

    private static GeneratorRun RunFiles(string[] sources, string? rootNamespace = "TestRoot")
        => GeneratorHarness.Run("ValueComparerGenerator", sources, rootNamespace,
            generatorAssemblyName: "MintPlayer.ValueComparerGenerator");

    [Fact]
    public void ItGeneratesEqualityForEveryTypeOfADecoratedHierarchy()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
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
        run.GeneratedSources.Select(s => s.HintName).Should().Equal(EqualityFile);
        var generated = run.SourceFor(EqualityFile)!;
        generated.Should().Contain("partial class Shape");
        // Circle has no attribute of its own, and still gets its members: without them it would inherit
        // Shape's EqualsCore and compare only Name.
        generated.Should().Contain("protected override bool EqualsCore(global::Demo.Shape other)");
    }

    [Fact]
    public void ItGeneratesNoComparerClassesAndNoWithComparerExtensions()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            public sealed partial class Model { public string Name { get; set; } = ""; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("ValueComparer<");
        run.AllSources.Should().NotContain("ComparerRegistry");
        run.AllSources.Should().NotContain("ValueComparerAttribute");
        run.AllSources.Should().NotContain("WithComparer");
        run.AllSources.Should().NotContain("ModelValueComparer");
    }

    [Fact]
    public void ItHonoursEqualityIgnore_InEqualsAndInGetHashCode()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            public abstract partial class Shape
            {
                public string Name { get; set; } = "";
            }

            public partial class Circle : Shape
            {
                [EqualityIgnore] public double Radius { get; set; }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        var circle = run.SourceFor(EqualityFile)!;
        circle.Should().Contain("// Radius: [EqualityIgnore]");
        // The #184 bug: the hash used to include ignored properties.
        circle.Should().NotContain("Radius.GetHashCode()");
        circle.Should().NotContain("Radius ==");
    }

    [Fact]
    public void ItFindsRecordsAndStructs()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality] public partial record R(string Name);
            [GenerateEquality] public partial struct S { public int X { get; set; } }
            [GenerateEquality] public partial record struct RS(int X);
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Select(s => s.HintName).Should().Equal(EqualityFile);
        var generated = run.SourceFor(EqualityFile)!;
        generated.Should().Contain("partial record R");
        generated.Should().Contain("partial struct S");
        generated.Should().Contain("partial record struct RS");
    }

    [Fact]
    public void AGenericModel_NamesItsTypeParametersEverywhere()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            public sealed partial class Box<T> { public T? Value { get; set; } }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        // The old generator concatenated this into Box<T>ValueComparer, which does not compile.
        run.SourceFor(EqualityFile)!.Should().Contain("partial class Box<T> : global::System.IEquatable<global::Demo.Box<T>>");
    }

    [Fact]
    public void ATypeInTheGlobalNamespace_IsNotMovedIntoTheRootNamespace()
    {
        var run = RunFiles(["""
            using MintPlayer.ValueComparerGenerator.Attributes;

            [GenerateEquality]
            public sealed partial class Global { public int X { get; set; } }
            """, """
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            public sealed partial class Local { public int X { get; set; } }
            """]);

        run.Errors.Should().BeEmpty(run.ErrorText);
        // Both share the one file, so the assertion is about Global's declaration, not the whole file: it sits at the
        // top level, before the one namespace block, which holds Local.
        var lines = run.SourceFor(EqualityFile)!.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var global = lines.IndexOf("partial class Global : global::System.IEquatable<global::Global>");
        var ns = lines.IndexOf("namespace Demo");
        global.Should().BeGreaterThanOrEqualTo(0, "the global model is written with no namespace and no indentation");
        ns.Should().BeGreaterThan(global);
        lines.Should().Contain("    partial class Local : global::System.IEquatable<global::Demo.Local>");
        lines.Count(l => l.StartsWith("namespace ", StringComparison.Ordinal)).Should().Be(1);
    }

    /// <summary>No models, no file: the producer's filename is empty, so nothing is added, not an empty file.</summary>
    [Fact]
    public void WithNoModels_ItEmitsNoEqualityFile()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            public sealed partial class NotAModel { public int X { get; set; } }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor(EqualityFile).Should().BeNull();
        run.GeneratedSources.Should().BeEmpty();
    }

    /// <summary>
    /// The same models, declared in other files and in another order, give the same text: the models are sorted by
    /// fully qualified name, ordinally, so the file doesn't depend on the order the driver sees the trees in.
    /// </summary>
    [Fact]
    public void TheEqualityFile_DoesNotDependOnDeclarationOrFileOrder()
    {
        const string first = """
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo.Zeta
            {
                [GenerateEquality] public sealed partial class Omega { public int X { get; set; } }
                [GenerateEquality] public sealed partial class Alpha { public int X { get; set; } }
            }

            [GenerateEquality] public sealed partial class TopLevel { public int X { get; set; } }
            """;

        const string second = """
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo.Alpha
            {
                [GenerateEquality] public abstract partial class Shape { public string Name { get; set; } = ""; }
                [GenerateEquality] public partial record Point(int X, int Y);
            }
            """;

        const string third = """
            namespace Demo.Alpha
            {
                public partial class Circle : Shape { public double Radius { get; set; } }
            }
            """;

        // The same declarations, each file's in reverse order.
        const string firstReversed = """
            using MintPlayer.ValueComparerGenerator.Attributes;

            [GenerateEquality] public sealed partial class TopLevel { public int X { get; set; } }

            namespace Demo.Zeta
            {
                [GenerateEquality] public sealed partial class Alpha { public int X { get; set; } }
                [GenerateEquality] public sealed partial class Omega { public int X { get; set; } }
            }
            """;

        const string secondReversed = """
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo.Alpha
            {
                [GenerateEquality] public partial record Point(int X, int Y);
                [GenerateEquality] public abstract partial class Shape { public string Name { get; set; } = ""; }
            }
            """;

        var run = RunFiles([first, second, third]);
        var shuffled = RunFiles([third, secondReversed, firstReversed]);

        run.Errors.Should().BeEmpty(run.ErrorText);
        shuffled.Errors.Should().BeEmpty(shuffled.ErrorText);
        run.GeneratedSources.Select(s => s.HintName).Should().Equal(EqualityFile);

        var text = run.SourceFor(EqualityFile)!;
        shuffled.SourceFor(EqualityFile).Should().Be(text);

        // Global namespace first, then Demo.Alpha (Circle, Point, Shape), then Demo.Zeta (Alpha, Omega).
        string[] order =
        [
            "partial class TopLevel", "namespace Demo.Alpha", "partial class Circle", "partial record Point",
            "partial class Shape", "namespace Demo.Zeta", "partial class Alpha", "partial class Omega",
        ];
        order.Select(d => text.IndexOf(d, StringComparison.Ordinal)).Should().BeInAscendingOrder();
        order.Select(d => text.IndexOf(d, StringComparison.Ordinal)).Should().NotContain(-1);
    }

    /// <summary>
    /// The CS0016 regression (PRD-EqualitySingleFile P1): a model whose fully qualified name alone passes 255
    /// characters still lands in the one fixed-name file, instead of a file named after it.
    /// </summary>
    [Fact]
    public void AModelInAVeryLongNamespace_StillEmitsOnlyTheFixedFile()
    {
        var ns = "Demo." + string.Join(".", Enumerable.Range(1, 30).Select(i => $"Segment{i:D2}"));
        ns.Length.Should().BeGreaterThan(290);

        var run = Run($$"""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace {{ns}};

            public static partial class Outer
            {
                [GenerateEquality]
                public sealed partial class Model<TKey, TValue> { public TKey? Key { get; set; } public TValue? Value { get; set; } }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Select(s => s.HintName).Should().Equal(EqualityFile);
        run.SourceFor(EqualityFile)!.Should().Contain($"namespace {ns}");
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
        run.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void WithoutARootNamespace_ItStillEmitsCompilableCode()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            public abstract partial class Shape { public string Name { get; set; } = ""; }

            public partial class Circle : Shape { }
            """, rootNamespace: null);

        run.Errors.Should().BeEmpty(run.ErrorText);
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
