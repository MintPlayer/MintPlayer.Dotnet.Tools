using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// <c>InjectSourceGenerator</c> over every type kind and nesting (#193), plus one regression test per finding of the
/// #194 sweep (K2, K5, W4, P1-P8, P25; docs/PRD-TypeKindRobustness.md).
/// </summary>
public class InjectTypeKindTests
{
    private const string Generator = "InjectSourceGenerator";

    private static GeneratorRun Run(params string[] sources) => GeneratorHarness.Run(Generator, sources, "Demo");

    private const string Dependencies = """
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public interface IDep { }
        public sealed class Dep : IDep { }
        public interface IOther { }
        public sealed class Other : IOther { }
        internal interface IInternalDep { }

        """;

    #region Five kinds

    public static TheoryData<string, string> KindsAndParents()
    {
        var data = new TheoryData<string, string>();
        foreach (var kind in new[] { "class", "record class", "record", "struct", "record struct" })
        foreach (var parent in new[] { "", "class", "record class", "record", "struct", "record struct", "interface" })
            data.Add(kind, parent);
        return data;
    }

    /// <summary>
    /// K2 and K5: every kind gets the constructor, at top level, nested in each kind and nested in an interface. The
    /// header carries the user's keyword (<c>record</c> for a reference record, S1), and the constructor assigns the
    /// field at run time.
    /// </summary>
    [Theory]
    [MemberData(nameof(KindsAndParents))]
    public void EveryKind_GetsAWorkingConstructor(string kind, string parent)
    {
        var target = $$"""
            public partial {{kind}} Svc
            {
                [Inject] private readonly IDep dep;
                public IDep Dependency => dep;
            }
            """;
        var source = Dependencies + (parent == "" ? target : $$"""
            public partial {{parent}} Outer
            {
            {{target}}
            }
            """);

        var run = Run(source);

        run.Errors.Should().BeEmpty(run.ErrorText);
        var expectedKeyword = kind == "record class" ? "record" : kind;
        run.SourceFor("Inject.g.cs").Should().Contain($"partial {expectedKeyword} Svc");
        if (kind.Contains("struct"))
            run.SourceFor("Inject.g.cs").Should().Contain(": this()", "a value type chains to this() (S3)");

        var assembly = run.Emit();
        var svcType = assembly.GetGeneratedType(parent == "" ? "Demo.Svc" : "Demo.Outer+Svc");
        var dep = Activator.CreateInstance(assembly.GetGeneratedType("Demo.Dep"))!;
        var svc = Activator.CreateInstance(svcType, dep)!;
        svcType.GetProperty("Dependency")!.GetValue(svc).Should().BeSameAs(dep);
    }

    /// <summary>
    /// The #193 case: unrelated partial code (a <c>[GeneratedRegex]</c> partial method in a record, a record struct, a
    /// struct and an interface) must neither crash the generator nor be touched by it, and a valid use beside it still
    /// generates. The regex methods have no implementation here (no regex generator runs), so only the generated file
    /// is asserted error-free.
    /// </summary>
    [Fact]
    public void UnrelatedPartialCode_IsLeftAlone()
    {
        var run = Run(GeneratedCodeChecks.UnrelatedPartials, Dependencies + """
            public partial class Valid
            {
                [Inject] private readonly IDep dep;
            }
            """);

        run.GeneratedErrors().Should().BeEmpty(run.ErrorText);
        run.Diagnostics.Where(d => d.Id.StartsWith("INJECT")).Should().BeEmpty();
        var output = run.SourceFor("Inject.g.cs");
        output.Should().Contain("public Valid(global::Demo.IDep dep)");
        output.Should().NotContain(" R(").And.NotContain(" RS(").And.NotContain(" S(").And.NotContain(" I(");
    }

    /// <summary>
    /// The generated code compiles without a single warning under <c>#nullable enable</c> (P5 included): no CS8625
    /// from an optional parameter, no CS4014 from a [PostConstruct] call.
    /// </summary>
    [Fact]
    public void TheGeneratedCode_IsWarningFree()
    {
        var run = Run("#nullable enable\n" + Dependencies + """
            public class SmtpOptions { public string Host { get; set; } = ""; }

            public partial class Svc
            {
                [Inject] private readonly IDep? optional;
                [Inject] private readonly IOther other;
                [Config("App:Name")] private readonly string name;
                [Config("App:Retries", DefaultValue = 3)] private readonly int retries;
                [Config("App:Nick")] private readonly string? nick;
                [ConnectionString("Default")] private readonly string connection;
                [Options("Smtp")] private readonly Microsoft.Extensions.Options.IOptions<SmtpOptions> smtp;

                [PostConstruct] private void Init() { }
            }

            public partial record struct Point
            {
                [Inject] private readonly IDep dep;
            }

            public partial record Base
            {
                [Inject] private readonly IDep dep;
            }

            public partial record Derived : Base
            {
                [Inject] private readonly IOther other;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);

        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
    }

    #endregion

    #region W4: split partials

    /// <summary>W4: [Inject] in both parts of a split partial gives ONE constructor that assigns both fields.</summary>
    [Fact]
    public void ASplitPartial_GetsOneConstructorForEveryPart()
    {
        var run = Run(
            Dependencies + """
                public partial class Svc
                {
                    [Inject] private readonly IDep dep;
                    public IDep Dependency => dep;
                }
                """,
            """
                using MintPlayer.SourceGenerators.Attributes;

                namespace Demo;

                public partial class Svc
                {
                    [Inject] private readonly IOther other;
                    public IOther OtherDependency => other;
                }
                """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        System.Text.RegularExpressions.Regex.Matches(run.SourceFor("Inject.g.cs")!, @"public Svc\(").Should().HaveCount(1);

        var assembly = run.Emit();
        var svcType = assembly.GetGeneratedType("Demo.Svc");
        var dep = Activator.CreateInstance(assembly.GetGeneratedType("Demo.Dep"))!;
        var other = Activator.CreateInstance(assembly.GetGeneratedType("Demo.Other"))!;
        var svc = Activator.CreateInstance(svcType, dep, other)!;
        svcType.GetProperty("Dependency")!.GetValue(svc).Should().BeSameAs(dep);
        svcType.GetProperty("OtherDependency")!.GetValue(svc).Should().BeSameAs(other);
    }

    /// <summary>
    /// W4, the base-class half: the base lookup read only the first declaration of a split base class
    /// (<c>.FirstOrDefault()</c>), so the derived constructor forwarded half the dependencies.
    /// </summary>
    [Fact]
    public void ASplitBaseClass_IsForwardedWhole()
    {
        var run = Run(
            Dependencies + """
                public partial class Base
                {
                    [Inject] private readonly IDep dep;
                    public IDep Dependency => dep;
                }

                public partial class Derived : Base
                {
                }
                """,
            """
                using MintPlayer.SourceGenerators.Attributes;

                namespace Demo;

                public partial class Base
                {
                    [Inject] private readonly IOther other;
                    public IOther OtherDependency => other;
                }
                """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain(": base(dep, other)");

        var assembly = run.Emit();
        var derivedType = assembly.GetGeneratedType("Demo.Derived");
        var dep = Activator.CreateInstance(assembly.GetGeneratedType("Demo.Dep"))!;
        var other = Activator.CreateInstance(assembly.GetGeneratedType("Demo.Other"))!;
        var derived = Activator.CreateInstance(derivedType, dep, other)!;
        derivedType.GetProperty("OtherDependency")!.GetValue(derived).Should().BeSameAs(other);
    }

    /// <summary>
    /// The base walk follows each base's own generated constructor, including its config and options parameters,
    /// so a three-level chain where the middle class has nothing of its own still lines up.
    /// </summary>
    [Fact]
    public void AMultiLevelChain_ForwardsThroughAnUnmarkedMiddleClass()
    {
        var run = Run(Dependencies + """
            public partial class Root
            {
                [Inject] private readonly IDep dep;
                [Config("App:Name")] private readonly string name;
            }

            public partial class Middle : Root { }

            public partial class Leaf : Middle
            {
                [Inject] private readonly IOther other;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Leaf(global::Demo.IOther other, global::Microsoft.Extensions.Configuration.IConfiguration __configuration, global::Demo.IDep dep)");
    }

    /// <summary>
    /// A partial record deriving from a record without dependencies gets nothing: the copy constructor is not a
    /// dependency to forward.
    /// </summary>
    [Fact]
    public void ARecordsCopyConstructor_IsNotForwarded()
    {
        var run = Run(Dependencies + """
            public record Base { public int X { get; init; } }
            public partial record Derived : Base { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().BeNull();
    }

    #endregion

    #region P1-P8, P25

    /// <summary>P1: a nullable (optional) dependency declared before a required one goes last (CS1737 otherwise).</summary>
    [Fact]
    public void P1_OptionalParametersGoLast()
    {
        var run = Run("#nullable enable\n" + Dependencies + """
            public partial class Svc
            {
                [Inject] private readonly IDep? dep;
                [Inject] private readonly IOther other;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Svc(global::Demo.IOther other, global::Demo.IDep? dep = default)");
    }

    /// <summary>P2: a public class with an internal dependency gets an internal constructor, not CS0051.</summary>
    [Fact]
    public void P2_TheConstructorIsNoMoreAccessibleThanItsParameters()
    {
        var run = Run(Dependencies + """
            public partial class Svc { [Inject] private readonly IInternalDep dep; }
            internal partial class InternalSvc { [Inject] private readonly IInternalDep dep; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        var output = run.SourceFor("Inject.g.cs");
        output.Should().Contain("internal Svc(global::Demo.IInternalDep dep)");
        output.Should().Contain("public InternalSvc(global::Demo.IInternalDep dep)",
            "an internal class keeps a public constructor: the container only calls public constructors");
    }

    /// <summary>P3: keys are read from the bound attribute, so a const, a concatenation and a const name work.</summary>
    [Fact]
    public void P3_KeysAreConstantsNotSyntax()
    {
        var run = Run(Dependencies + """
            public static class Keys { public const string Db = "Db:Name"; public const string Conn = "Default"; public const string Sec = "Smtp"; }
            public class SmtpOptions { public string Host { get; set; } = ""; }
            public partial class Svc
            {
                [Config(Keys.Db)] private readonly string a;
                [Config("Db:" + "Other")] private readonly string b;
                [ConnectionString(Keys.Conn)] private readonly string c;
                [Options(Keys.Sec)] private readonly Microsoft.Extensions.Options.IOptions<SmtpOptions> d;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("CONFIG002").Should().BeEmpty();
        run.Of("CONNSTR001").Should().BeEmpty();
        var output = run.SourceFor("Inject.g.cs");
        output.Should().Contain("\"Db:Name\"").And.Contain("\"Db:Other\"").And.Contain("GetConnectionString(__configuration, \"Default\")");
        output.Should().Contain("this.d = d;");
    }

    /// <summary>
    /// P4 (S7): the section of <c>[Options]</c> is documentation. The options are injected, and nothing binds or
    /// reports anything about the section.
    /// </summary>
    [Fact]
    public void P4_TheOptionsSectionIsDocumentationOnly()
    {
        var run = Run(Dependencies + """
            public class EmailSettings { public string Host { get; set; } = ""; }
            public partial class Svc
            {
                [Options("Email")] private readonly Microsoft.Extensions.Options.IOptions<EmailSettings> email;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Diagnostics.Should().BeEmpty();
        run.SourceFor("Inject.g.cs").Should().NotContain("\"Email\"");
    }

    /// <summary>P5: a non-void [PostConstruct] is INJECT005 and is not called (no fire-and-forget, no CS4014).</summary>
    [Fact]
    public void P5_ANonVoidPostConstructIsReported()
    {
        var run = Run(Dependencies + """
            public partial class Svc
            {
                [Inject] private readonly IDep dep;
                [PostConstruct] private async System.Threading.Tasks.Task InitAsync() { await System.Threading.Tasks.Task.Delay(1); }
            }
            """);

        run.Of("INJECT005").Should().ContainSingle().Which.GetMessage().Should().Contain("InitAsync");
        run.SourceFor("Inject.g.cs").Should().NotContain("InitAsync");
    }

    /// <summary>P6: a same-named attribute in another namespace is not ours.</summary>
    [Fact]
    public void P6_AForeignInjectAttributeIsIgnored()
    {
        var run = Run("""
            namespace Other { [System.AttributeUsage(System.AttributeTargets.All)] public class InjectAttribute : System.Attribute { } }
            namespace Demo
            {
                public interface IDep { }
                public partial class Svc { [Other.Inject] private readonly IDep dep; }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().BeNull();
    }

    /// <summary>P7: a non-partial type, or a non-partial parent, is INJECT006 instead of CS0260 on the user's type.</summary>
    [Fact]
    public void P7_ANonPartialTypeIsReported()
    {
        var run = Run(Dependencies + """
            public class NotPartial { [Inject] private readonly IDep dep; }
            public class NpOuter { public partial class Inner { [Inject] private readonly IDep dep; } }
            """);

        run.Errors.Where(e => e.Id.StartsWith("CS")).Should().BeEmpty(run.ErrorText);
        var messages = run.Of("INJECT006").Select(d => d.GetMessage()).ToList();
        messages.Should().HaveCount(2);
        messages.Should().Contain(m => m.Contains("'NotPartial' is not partial"));
        messages.Should().Contain(m => m.Contains("'Inner'") && m.Contains("'NpOuter' is not partial"));
    }

    /// <summary>P8: Inject.g.cs carries the auto-generated header and enables nullable.</summary>
    [Fact]
    public void P8_TheFileHasThePreamble()
    {
        var run = Run(Dependencies + "public partial class Svc { [Inject] private readonly IDep dep; }");

        var output = run.SourceFor("Inject.g.cs");
        output.Should().Contain("<auto-generated>");
        output.Should().Contain("#nullable enable");
    }

    /// <summary>P25: a type with a parameter list cannot take a second constructor (CS8862): INJECT007.</summary>
    [Theory]
    [InlineData("public partial record Svc(int X) { [Inject] private readonly IDep dep; }")]
    [InlineData("public partial record struct Svc(int X) { [Inject] private readonly IDep dep; }")]
    [InlineData("public partial class Svc(int x) { [Inject] private readonly IDep dep; }")]
    [InlineData("public partial struct Svc(int x) { [Inject] private readonly IDep dep; }")]
    public void P25_ATypeWithAParameterListIsReported(string declaration)
    {
        var run = Run(Dependencies + declaration);

        run.Of("INJECT007").Should().ContainSingle();
        run.Errors.Where(e => e.Id == "CS8862").Should().BeEmpty();
        run.SourceFor("Inject.g.cs").Should().BeNull();
    }

    /// <summary>
    /// A property with an ordinary setter is not injected; it used to be skipped without a word, leaving it null at
    /// run time. INJECT008 says so, while get-only and init-only properties on the same type are still assigned.
    /// </summary>
    [Fact]
    public void ASettableInjectPropertyIsReported()
    {
        var run = Run(Dependencies + """
            public partial class Svc
            {
                [Inject] public IDep Settable { get; set; }
                [Inject] public IOther GetOnly { get; }
                [Inject] public IDep InitOnly { get; init; }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        var diagnostic = run.Of("INJECT008").Should().ContainSingle().Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage().Should().Contain("'Settable'");

        var output = run.SourceFor("Inject.g.cs");
        output.Should().Contain("this.GetOnly = ");
        output.Should().Contain("this.InitOnly = ");
        output.Should().NotContain("Settable");
    }

    /// <summary>INJECT008 alone does not hide INJECT006 on a type that cannot be extended.</summary>
    [Fact]
    public void ASettableInjectPropertyOnANonPartialTypeReportsBoth()
    {
        var run = Run(Dependencies + "public class NotPartial { [Inject] public IDep Settable { get; set; } }");

        run.Of("INJECT008").Should().ContainSingle();
        run.Of("INJECT006").Should().ContainSingle();
    }

    #endregion
}
