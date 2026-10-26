using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// <c>[ImplementInterface]</c> (docs/PRD-ImplementInterfaceAttribute.md): generation, every IMPLINTF diagnostic, the
/// IMPLINTF008 analyzer and every code fix.
/// </summary>
public class ImplementInterfaceTests
{
    private const string Generator = "ImplementInterfaceSourceGenerator";
    private const string Hint = "ImplementInterface.g.cs";

    private static GeneratorRun Run(params string[] sources) => GeneratorHarness.Run(Generator, sources);

    private static GeneratorRun RunInDemo(string body, bool nullable = true) => Run(
        (nullable ? "#nullable enable\n" : string.Empty) + $$"""
        using System;
        using System.Collections.Generic;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo
        {
        {{body}}
        }
        """);

    private static string Generated(GeneratorRun run) => run.SourceFor(Hint) ?? string.Empty;

    private static void Compiles(GeneratorRun run) => run.Errors.Should().BeEmpty(run.ErrorText);

    #region Generation

    /// <summary>The PRD's overview example: an empty class body implements both interfaces.</summary>
    [Fact]
    public void CarDealerExample_ImplementsBothInterfaces()
    {
        var run = RunInDemo("""
            public class Customer { }
            public class Part { }
            public interface IQueryableSet<T> { }

            public interface ICustomerManagement { IQueryableSet<Customer> Customers { get; set; } }
            public interface IWarehouseManagement
            {
                IQueryableSet<Part> Parts { get; set; }
                IQueryableSet<Customer> Suppliers { get; set; }
            }

            [ImplementInterface(typeof(ICustomerManagement), typeof(IWarehouseManagement))]
            public partial class CarDealerContext { }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Should().Contain("partial class CarDealerContext : global::Demo.ICustomerManagement, global::Demo.IWarehouseManagement");
        source.Should().Contain("public global::Demo.IQueryableSet<global::Demo.Customer> Customers { get; set; } = default!;");

        var assembly = run.Emit();
        var type = assembly.GetGeneratedType("Demo.CarDealerContext");
        assembly.GetGeneratedType("Demo.ICustomerManagement").IsAssignableFrom(type).Should().BeTrue();
        assembly.GetGeneratedType("Demo.IWarehouseManagement").IsAssignableFrom(type).Should().BeTrue();
        type.GetProperty("Suppliers")!.CanWrite.Should().BeTrue();
    }

    /// <summary>R2.1: an inherited interface, reached twice through a diamond, is implemented once.</summary>
    [Fact]
    public void Diamond_ImplementsTheSharedBaseOnce()
    {
        var run = RunInDemo("""
            public interface IBase { int Id { get; set; } }
            public interface ILeft : IBase { string Left { get; set; } }
            public interface IRight : IBase { string Right { get; set; } }

            [ImplementInterface(typeof(ILeft), typeof(IRight))]
            public partial class Both { }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Split("int Id").Length.Should().Be(2);
        source.Should().Contain(": global::Demo.ILeft, global::Demo.IRight");
        source.Should().NotContain("global::Demo.IBase,");
    }

    /// <summary>R2.6: the same name and type in two interfaces is one public property.</summary>
    [Fact]
    public void SameNameSameType_IsOneProperty()
    {
        var run = RunInDemo("""
            public interface IA { string Name { get; set; } }
            public interface IB { string Name { get; set; } }

            [ImplementInterface(typeof(IA), typeof(IB))]
            public partial class Both { }
            """);

        Compiles(run);
        Generated(run).Split(" Name ").Length.Should().Be(2);
    }

    /// <summary>R2.7: the interface listed first keeps the public property; the other is implemented explicitly.</summary>
    [Fact]
    public void SameNameDifferentType_FallsBackToExplicit()
    {
        var run = RunInDemo("""
            public interface IA { string Name { get; set; } }
            public interface IB { int Name { get; set; } }

            [ImplementInterface(typeof(IA), typeof(IB))]
            public partial class Both { }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Should().Contain("public string Name { get; set; }");
        source.Should().Contain("int global::Demo.IB.Name { get; set; }");
    }

    /// <summary>R2.7 with a hand-written property for the first interface: the second still gets an explicit one.</summary>
    [Fact]
    public void SameNameDifferentType_WithHandWrittenFirst_FallsBackToExplicit()
    {
        var run = RunInDemo("""
            public interface IA { string Name { get; set; } }
            public interface IB { int Name { get; set; } }

            [ImplementInterface(typeof(IA), typeof(IB))]
            public partial class Both { public string Name { get; set; } = ""; }
            """);

        Compiles(run);
        run.Of("IMPLINTF009").Should().BeEmpty();
        Generated(run).Should().Contain("int global::Demo.IB.Name { get; set; }");
    }

    /// <summary>R2.3: members written by hand, in another partial part or a base class, win.</summary>
    [Fact]
    public void HandWrittenAndInheritedMembers_Win()
    {
        var run = RunInDemo("""
            public interface IThing { string Name { get; set; } int Count { get; set; } bool Flag { get; set; } }

            public class ThingBase { public bool Flag { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing : ThingBase { }

            public partial class Thing { public string Name { get => "x"; set { } } }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Should().Contain(" Count ");
        source.Should().NotContain(" Name ");
        source.Should().NotContain(" Flag ");
    }

    /// <summary>R2.2 / Q8: an interface in the base list and in the attribute is fine and not repeated.</summary>
    [Fact]
    public void InterfaceInBaseListAndAttribute_IsNotRepeated()
    {
        var run = RunInDemo("""
            public interface IThing { string Name { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing : IThing { }
            """);

        Compiles(run);
        run.Diagnostics.Should().BeEmpty();
        Generated(run).Should().NotContain(": global::Demo.IThing");
    }

    /// <summary>R3: init is mirrored (a set accessor cannot implement init, CS8854); static properties are generated.</summary>
    [Fact]
    public void InitAndStaticProperties_AreMirrored()
    {
        var run = RunInDemo("""
            public interface IThing { string Name { get; init; } static abstract string Schema { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing { }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Should().Contain("public string Name { get; init; } = default!;");
        source.Should().Contain("public static string Schema { get; set; } = default!;");
    }

    /// <summary>R1: Explicit = true implements this attribute's interfaces explicitly; the other attribute's publicly.</summary>
    [Fact]
    public void Explicit_AppliesPerAttribute()
    {
        var run = RunInDemo("""
            public interface IA { string A { get; set; } }
            public interface IB { string B { get; set; } }

            [ImplementInterface(typeof(IA))]
            [ImplementInterface(typeof(IB), Explicit = true)]
            public partial class Thing { }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Should().Contain("public string A { get; set; }");
        source.Should().Contain("string global::Demo.IB.B { get; set; }");
    }

    /// <summary>Q1: non-nullable references get default!, unless InitializeNonNullable = false; others never do.</summary>
    [Fact]
    public void Initializers_FollowNullability()
    {
        var run = RunInDemo("""
            public interface IA { string Name { get; set; } string? Nick { get; set; } int Age { get; set; } }
            public interface IB { string Code { get; set; } }

            [ImplementInterface(typeof(IA))]
            [ImplementInterface(typeof(IB), InitializeNonNullable = false)]
            public partial class Thing { public Thing() { Code = ""; } }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Should().Contain("public string Name { get; set; } = default!;");
        source.Should().Contain("public string? Nick { get; set; }" + Environment.NewLine);
        source.Should().Contain("public int Age { get; set; }" + Environment.NewLine);
        source.Should().Contain("public string Code { get; set; }" + Environment.NewLine);
    }

    /// <summary>Q1: types from a nullable-oblivious context stay oblivious; the file switches back afterwards.</summary>
    [Fact]
    public void ObliviousTypes_AreEmittedWithoutAnnotations()
    {
        var run = RunInDemo("""
            public interface IThing { string Name { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing { }
            """, nullable: false);

        Compiles(run);
        var source = Generated(run);
        source.Should().Contain("#nullable disable annotations");
        source.Should().Contain("#nullable enable annotations");
        source.Should().NotContain("default!");
    }

    /// <summary>R4: closed generic interfaces, generic and nested targets, and every struct and record kind.</summary>
    [Theory]
    [InlineData("class")]
    [InlineData("record")]
    [InlineData("struct")]
    [InlineData("record struct")]
    public void TargetKinds_Compile(string kind)
    {
        var run = RunInDemo($$"""
            public interface IRepo<T> { List<T> Items { get; set; } }
            public interface IThing { string Name { get; set; } }

            public partial class Outer<TOuter>
            {
                [ImplementInterface(typeof(IRepo<string>), typeof(IThing))]
                public partial {{kind}} Inner<T> { }
            }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Should().Contain("global::System.Collections.Generic.List<string> Items { get; set; }");
        if (kind.Contains("struct"))
            source.Should().NotContain("default!");
    }

    /// <summary>R9: the generic attribute, on a second partial part, merges with the non-generic one.</summary>
    [Fact]
    public void GenericAttributeAndPartialParts_Merge()
    {
        var run = RunInDemo("""
            public interface IA { string A { get; set; } }
            public interface IB { string B { get; set; } }
            public interface IC { string C { get; set; } }

            [ImplementInterface<IA>]
            public partial class Thing { }

            [ImplementInterface(typeof(IB))]
            [ImplementInterface<IC>]
            public partial class Thing { }
            """);

        Compiles(run);
        var source = Generated(run);
        source.Split("partial class Thing").Length.Should().Be(2);
        source.Should().Contain(": global::Demo.IA, global::Demo.IB, global::Demo.IC");
    }

    /// <summary>S5: a property type less accessible than the type is implemented explicitly (CS0053 otherwise).</summary>
    [Fact]
    public void InaccessiblePropertyType_IsImplementedExplicitly()
    {
        var run = RunInDemo("""
            internal class Hidden { }
            internal interface IHidden { Hidden Value { get; set; } }

            [ImplementInterface(typeof(IHidden))]
            public partial class Thing { }
            """);

        Compiles(run);
        Generated(run).Should().Contain("global::Demo.Hidden global::Demo.IHidden.Value { get; set; }");
    }

    /// <summary>
    /// Q5: an abstract class declares every member without an obvious body as abstract, mirroring generic constraints
    /// (CS0425), ref kinds, params and default values; a concrete subclass that supplies them compiles.
    /// </summary>
    [Fact]
    public void AbstractTarget_DeclaresAbstractMembers()
    {
        var run = RunInDemo("""
            public enum Mode { A, B = 5 }

            public interface IService
            {
                string Name { get; set; }
                string ReadOnly { get; }
                string WriteOnly { set; }
                event EventHandler? Changed;
                int this[string key] { get; }
                T Get<T>(T value) where T : class, IDisposable, new();
                void Shapes(out int a, ref int b, in int c, params int[] d);
                int Defaults(int a = 5, string? s = null, Mode m = Mode.B, double d = 1.5, bool b = true);
            }

            [ImplementInterface(typeof(IService))]
            public abstract partial class ServiceBase { }

            public sealed class Service : ServiceBase
            {
                public override string ReadOnly => "";
                public override string WriteOnly { set { } }
                public override event EventHandler? Changed;
                public override int this[string key] => 0;
                public override T Get<T>(T value) => value;
                public override void Shapes(out int a, ref int b, in int c, params int[] d) => a = 0;
                public override int Defaults(int a = 5, string? s = null, Mode m = Mode.B, double d = 1.5, bool b = true) => a;
                private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
            }
            """);

        Compiles(run);
        run.Diagnostics.Should().BeEmpty();
        var source = Generated(run);
        source.Should().Contain("public string Name { get; set; } = default!;");
        source.Should().Contain("public abstract string ReadOnly { get; }");
        source.Should().Contain("where T : class, global::System.IDisposable, new();");
        source.Should().Contain("params int[] d");
    }

    #endregion

    #region Diagnostics

    [Fact]
    public void NotPartial_ReportsImplintf001()
    {
        var run = RunInDemo("""
            public interface IThing { string Name { get; set; } }

            public class Outer
            {
                [ImplementInterface(typeof(IThing))]
                public partial class Thing { }
            }
            """);

        run.Of("IMPLINTF001").Length.Should().Be(1);
        run.Of("IMPLINTF001")[0].GetMessage().Should().Contain("type 'Outer' is not partial");
    }

    [Fact]
    public void NotAnInterface_ReportsImplintf002()
    {
        var run = RunInDemo("""
            public class NotAnInterface { }
            public interface IRepo<T> { T Value { get; set; } }

            [ImplementInterface(typeof(NotAnInterface), typeof(IRepo<>))]
            public partial class Thing { }
            """);

        run.Of("IMPLINTF002").Length.Should().Be(2);
        run.Of("IMPLINTF002")[1].GetMessage().Should().Contain("open generic");
    }

    [Fact]
    public void Method_OnConcreteTarget_ReportsImplintf003_UnlessHandWritten()
    {
        var reported = RunInDemo("""
            public interface IThing { string Name { get; set; } void Recalculate(int year); }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing { }
            """);
        reported.Of("IMPLINTF003").Length.Should().Be(1);
        reported.Of("IMPLINTF003")[0].GetMessage().Should().Contain("Demo.IThing.Recalculate(int)");
        Generated(reported).Should().Contain("Name"); // S4: the supported members are still emitted

        var handWritten = RunInDemo("""
            public interface IThing { string Name { get; set; } void Recalculate(int year); }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing { public void Recalculate(int y) { } }
            """);
        Compiles(handWritten);
        handWritten.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void StaticMethod_AndExplicitAbstract_ReportImplintf003()
    {
        var run = RunInDemo("""
            public interface IFactory { static abstract IFactory Create(); }
            public interface IThing { void Run(); }

            [ImplementInterface(typeof(IFactory))]
            public partial class Factory { }

            [ImplementInterface(typeof(IThing), Explicit = true)]
            public abstract partial class ThingBase { }
            """);

        var messages = run.Of("IMPLINTF003").Select(d => d.GetMessage()).ToArray();
        messages.Length.Should().Be(2);
        messages.Should().Contain(m => m.Contains("a static method"));
        messages.Should().Contain(m => m.Contains("cannot be abstract"));
    }

    [Fact]
    public void GetOnlyProperty_OnConcreteTarget_ReportsImplintf004()
    {
        var run = RunInDemo("""
            public interface IThing { string Name { get; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing { }
            """);

        run.Of("IMPLINTF004").Length.Should().Be(1);
        run.Of("IMPLINTF004")[0].GetMessage().Should().Contain("a get-only property");
    }

    [Fact]
    public void ConflictingAbstractMembers_ReportImplintf005()
    {
        var run = RunInDemo("""
            public interface IA { int Compute(int x); }
            public interface IB { string Compute(int x); }

            [ImplementInterface(typeof(IA), typeof(IB))]
            public abstract partial class Both { }
            """);

        run.Of("IMPLINTF005").Length.Should().Be(1);
    }

    [Fact]
    public void UnsupportedTargets_ReportImplintf006()
    {
        var run = RunInDemo("""
            public interface IThing { string Name { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public static partial class Helpers { }
            """);

        run.Of("IMPLINTF006").Length.Should().Be(1);
    }

    [Fact]
    public void DuplicateInterface_ReportsImplintf007()
    {
        var run = RunInDemo("""
            public interface IThing { string Name { get; set; } }

            [ImplementInterface(typeof(IThing))]
            [ImplementInterface(typeof(IThing))]
            public partial class Thing { }
            """);

        Compiles(run);
        run.Of("IMPLINTF007").Length.Should().Be(1);
    }

    [Fact]
    public void MismatchedHandWrittenMembers_ReportImplintf009()
    {
        var run = RunInDemo("""
            public interface IThing { IEnumerable<string> Names { get; set; } int Count { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing
            {
                public List<string> Names { get; set; } = new();
                private int Count;
            }
            """);

        var messages = run.Of("IMPLINTF009").Select(d => d.GetMessage()).ToArray();
        messages.Length.Should().Be(2);
        messages.Should().Contain(m => m.Contains("'System.Collections.Generic.List<string>' instead of"));
        messages.Should().Contain(m => m.Contains("a field instead of a property"));
        Generated(run).Should().NotContain(" Names ");
    }

    #endregion

    #region IMPLINTF008 and INTF001

    [Fact]
    public async Task RedundantProperty_IsReportedAndRemoved()
    {
        var result = await CodeFixHarness.ApplyAsync(
            "RedundantImplementInterfacePropertyAnalyzer",
            "RemoveRedundantPropertyCodeFixProvider",
            """
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IThing { string Name { get; set; } int Count { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing
            {
                public string Name { get; set; }
                [System.Obsolete] public int Count { get; set; }
            }
            """);

        result.Of("IMPLINTF008").Count.Should().Be(1);
        result.Applied.Should().BeTrue();
        result.FixedSource.Should().NotContain("public string Name");
        result.FixedSource.Should().Contain("public int Count");
    }

    [Theory]
    [InlineData("public string Name { get; set; } = \"\";")]
    [InlineData("public string Name { get; private set; }")]
    [InlineData("public virtual string Name { get; set; }")]
    [InlineData("public string? Name { get; set; }")]
    public async Task NonBareProperty_IsNotRedundant(string declaration)
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("RedundantImplementInterfacePropertyAnalyzer", [$$"""
            #nullable enable
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IThing { string Name { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing
            {
                {{declaration}}
            }
            """]);

        diagnostics.Where(d => d.Id == "IMPLINTF008").Should().BeEmpty();
    }

    [Fact]
    public async Task ExplicitAttribute_PropertyIsNotRedundant()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("RedundantImplementInterfacePropertyAnalyzer", ["""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IThing { string Name { get; set; } }

            [ImplementInterface(typeof(IThing), Explicit = true)]
            public partial class Thing { public string Name { get; set; } }
            """]);

        diagnostics.Where(d => d.Id == "IMPLINTF008").Should().BeEmpty();
    }

    /// <summary>Q3: INTF001 offers "Move to interface" beside "Add", and the move deletes the class's copy.</summary>
    [Fact]
    public async Task Intf001_MovesABarePropertyToTheInterface()
    {
        var result = await CodeFixHarness.ApplyAsync(
            "InterfaceImplementationAnalyzer",
            "InterfaceCodeFixProvider",
            [FixtureProject.Of("App", """
                using MintPlayer.SourceGenerators.Attributes;

                namespace Demo;

                public interface IThing { }

                [ImplementInterface(typeof(IThing))]
                public partial class Thing : IThing
                {
                    public string Extra { get; set; }
                }
                """)],
            actionIndex: 1);

        result.Titles.Should().Contain(t => t.StartsWith("Move 'Extra' to IThing"));
        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("string Extra { get; set; }");
        result.FixedSource.Should().NotContain("public string Extra");
    }

    [Fact]
    public async Task Intf001_DoesNotOfferMoveForAPropertyWithABody()
    {
        var result = await CodeFixHarness.ApplyAsync(
            "InterfaceImplementationAnalyzer",
            "InterfaceCodeFixProvider",
            """
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IThing { }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing : IThing
            {
                public string Extra => "x";
            }
            """);

        result.Titles.Should().NotContain(t => t.StartsWith("Move"));
    }

    #endregion

    #region Code fixes for generator and compiler diagnostics

    [Fact]
    public async Task MakePartial_FixesTypeAndContainingTypes()
    {
        const string source = """
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IThing { string Name { get; set; } }

            public class Outer
            {
                [ImplementInterface(typeof(IThing))]
                class Thing { }
            }
            """;

        var fixedSource = await ApplyGeneratorFixAsync(source, "IMPLINTF001", "MakePartialCodeFixProvider");

        fixedSource.Should().Contain("public partial class Outer");
        fixedSource.Should().Contain("    partial class Thing { }");
        Compiles(Run(fixedSource));
    }

    [Fact]
    public async Task DeleteMismatchedMember_LetsTheGeneratorImplementIt()
    {
        const string source = """
            using System.Collections.Generic;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IThing { IEnumerable<string> Names { get; set; } }

            [ImplementInterface(typeof(IThing))]
            public partial class Thing
            {
                public List<string> Names { get; set; } = new();
            }
            """;

        var fixedSource = await ApplyGeneratorFixAsync(source, "IMPLINTF009", "DeleteMismatchedMemberCodeFixProvider");

        fixedSource.Should().NotContain("List<string> Names");
        Compiles(Run(fixedSource));
    }

    /// <summary>S6 / Q8: CS0535 offers the attribute, keeps the base-list entry, adds partial and the using.</summary>
    [Fact]
    public async Task Cs0535_ImplementsWithTheAttribute()
    {
        const string source = """
            namespace Demo;

            public interface IThing { string Name { get; set; } }

            public class Thing : IThing
            {
            }
            """;

        var fixedSource = await ApplyCompilerFixAsync(source, "CS0535", "ImplementWithAttributeCodeFixProvider");

        fixedSource.Should().Contain("using MintPlayer.SourceGenerators.Attributes;");
        fixedSource.Should().Contain("[ImplementInterface(typeof(IThing))]");
        fixedSource.Should().Contain("public partial class Thing : IThing");
        Compiles(Run(fixedSource));
    }

    [Fact]
    public async Task Cs0535_AppendsToAnExistingAttribute()
    {
        const string source = """
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IA { string A { get; set; } }
            public interface IB { string B { get; set; } }

            [ImplementInterface(typeof(IA), InitializeNonNullable = false)]
            public partial class Thing : IB
            {
            }
            """;

        var fixedSource = await ApplyCompilerFixAsync(source, "CS0535", "ImplementWithAttributeCodeFixProvider");

        fixedSource.Should().Contain("[ImplementInterface(typeof(IA), typeof(IB), InitializeNonNullable = false)]");
    }

    /// <summary>
    /// The harness's code-fix runner needs an analyzer and a compiling fixture; generator diagnostics need neither. The
    /// generator runs on the source, and its diagnostic is re-created on the workspace document at the same span.
    /// </summary>
    private static async Task<string> ApplyGeneratorFixAsync(string source, string diagnosticId, string providerName)
    {
        var reported = Run(source).Of(diagnosticId);
        reported.Length.Should().BeGreaterThan(0);
        return await ApplyFixAsync(source, providerName, (tree, _) => Diagnostic.Create(reported[0].Descriptor, Location.Create(tree, reported[0].Location.SourceSpan)));
    }

    private static Task<string> ApplyCompilerFixAsync(string source, string diagnosticId, string providerName)
        => ApplyFixAsync(source, providerName, (tree, compilation) => compilation.GetDiagnostics().First(d => d.Id == diagnosticId));

    private static async Task<string> ApplyFixAsync(string source, string providerName, Func<SyntaxTree, Compilation, Diagnostic> diagnostic)
    {
        var provider = (CodeFixProvider)Activator.CreateInstance(
            Type.GetType($"MintPlayer.SourceGenerators.Diagnostics.{providerName}, MintPlayer.SourceGenerators", throwOnError: true)!)!;

        using var workspace = new AdhocWorkspace();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(Attributes.ImplementInterfaceAttribute).Assembly.Location));
        var project = workspace.CurrentSolution
            .AddProject("App", "App", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(references);
        var document = project.AddDocument("Thing.cs", SourceText.From(source), filePath: "/App/Thing.cs");

        var tree = (await document.GetSyntaxTreeAsync())!;
        var compilation = (await document.Project.GetCompilationAsync())!;
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic(tree, compilation), (a, _) => actions.Add(a), default));
        actions.Should().NotBeEmpty();

        var operation = (await actions[0].GetOperationsAsync(default)).OfType<ApplyChangesOperation>().Single();
        return (await operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }

    #endregion
}
