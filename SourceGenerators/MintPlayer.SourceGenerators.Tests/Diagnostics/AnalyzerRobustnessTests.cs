using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Diagnostics;

/// <summary>
/// MP001 (unused usings) and INTF001 (public member missing from the interface) on the shapes that crashed them, broke
/// the code they fixed, or skipped a type kind (docs/PRD-TypeKindRobustness.md: C5, W14, K8, W15, W16).
/// </summary>
public class AnalyzerRobustnessTests
{
    #region MP001

    private static Task<CodeFixResult> FixUsings(string source)
        => CodeFixHarness.ApplyAsync("UnusedUsingsAnalyzer", "UnusedUsingsCodeFixProvider", source);

    /// <summary>C5: `Name` is null for a type alias, and the NullReferenceException lost MP001 for the file.</summary>
    [Theory]
    [InlineData("using Pair = (int, int);", "(int, int)")]
    [InlineData("using Numbers = int[];", "int[]")]
    [InlineData("using Text = System.Text.StringBuilder;", "System.Text.StringBuilder")]
    public async Task AnUnusedTypeAliasIsReported(string directive, string expected)
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("UnusedUsingsAnalyzer", [$$"""
            {{directive}}
            using System.Collections.Generic;

            namespace Demo;

            public class Thing { }
            """]);

        diagnostics.Where(d => d.Id == "MP001").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains(expected))
            .And.Contain(m => m.Contains("System.Collections.Generic"));
    }

    [Fact]
    public async Task AnUnsafeAliasDoesNotCrashIt()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("UnusedUsingsAnalyzer", ["""
            using unsafe P = int*;
            using System.Text;

            namespace Demo;

            public class Thing { }
            """]);

        diagnostics.Should().Contain(d => d.Id == "MP001" && d.GetMessage().Contains("System.Text"));
    }

    /// <summary>W14: the directive above the first using was deleted with it, changing the nullable context.</summary>
    [Theory]
    [InlineData("#nullable enable")]
    [InlineData("// Copyright header")]
    [InlineData("#region Usings")]
    public async Task TheFixKeepsWhatIsAboveTheFirstUsing(string above)
    {
        var endRegion = above.StartsWith("#region") ? "#endregion" : string.Empty;
        var result = await FixUsings($$"""
            {{above}}
            using System.Text;
            {{endRegion}}

            namespace Demo;

            public class Thing { public string? Name { get; set; } }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().NotContain("using System.Text;");
        result.FixedSource.Should().Contain(above);
        result.Errors.Should().BeEmpty(result.ErrorText);
    }

    #endregion

    #region INTF001

    private const string Analyzer = "InterfaceImplementationAnalyzer";
    private const string CodeFix = "InterfaceCodeFixProvider";
    private const string Id = "INTF001";

    /// <summary>K8: INTF001 never ran on value types.</summary>
    [Theory]
    [InlineData("struct")]
    [InlineData("record struct")]
    [InlineData("readonly struct")]
    [InlineData("class")]
    [InlineData("record")]
    public async Task ItReportsOnEveryKind(string keyword)
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync(Analyzer, [$$"""
            namespace Demo;

            public interface IThing { void DoIt(); }

            public {{keyword}} Thing : IThing
            {
                public void DoIt() { }
                public void Extra() { }
            }
            """]);

        diagnostics.Where(d => d.Id == Id).Should().ContainSingle()
            .Which.GetMessage().Should().Contain("Extra");
    }

    [Fact]
    public async Task ItIgnoresTheMembersARecordStructSynthesizes()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync(Analyzer, ["""
            namespace Demo;

            public interface IPoint { int X { get; } }

            public record struct Point(int X) : IPoint;
            """]);

        diagnostics.Where(d => d.Id == Id).Should().BeEmpty();
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("record struct")]
    public async Task TheFixWorksOnAStruct(string keyword)
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, CodeFix, $$"""
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public {{keyword}} Thing : IThing
            {
                public void DoIt() { }
                public int Extra(string value) => value.Length;
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("int Extra(string value);");
        result.Errors.Should().BeEmpty(result.ErrorText);
    }

    /// <summary>W15: `T Get();` written into a non-generic interface is CS0246 / CS0738.</summary>
    [Theory]
    [InlineData("public T Get() => default!;")]
    [InlineData("public void Put(T value) { }")]
    [InlineData("public System.Collections.Generic.List<T> All() => new();")]
    [InlineData("public T[] Many => new T[0];")]
    [InlineData("public void Constrained<U>() where U : T { }")]
    public async Task TheFixDeclinesASignatureUsingTheClassTypeParameter(string member)
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync(Analyzer, [Generic(member)]);
        diagnostics.Where(d => d.Id == Id).Should().ContainSingle("the member is still missing from the interface");

        var result = await CodeFixHarness.ApplyAsync(Analyzer, CodeFix, Generic(member));
        result.Applied.Should().BeFalse("no interface declaration can express the class's own T");

        static string Generic(string member) => $$"""
            namespace Demo;

            public interface IThing { void DoIt(); }

            public class G<T> : IThing
            {
                public void DoIt() { }
                {{member}}
            }
            """;
    }

    [Fact]
    public async Task TheFixStillHandlesTheMethodsOwnTypeParameter()
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, CodeFix, """
            namespace Demo;

            public interface IThing { void DoIt(); }

            public class G<T> : IThing
            {
                public void DoIt() { }
                public U Echo<U>(U value) => value;
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("U Echo<U>(U value);");
        result.Errors.Should().BeEmpty(result.ErrorText);
    }

    /// <summary>W16: an added overload of an interface method was never reported, since the name was satisfied.</summary>
    [Fact]
    public async Task AnOverloadOfAnInterfaceMethodIsReported()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync(Analyzer, ["""
            namespace Demo;

            public interface IThing { void DoIt(); }

            public class Thing : IThing
            {
                public void DoIt() { }
                public void DoIt(int times) { }
            }
            """]);

        var diagnostic = diagnostics.Where(d => d.Id == Id).Should().ContainSingle().Which;
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(7, "it is about DoIt(int), not DoIt()");
    }

    [Fact]
    public async Task TheFixAddsTheReportedOverload()
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, CodeFix, """
            namespace Demo;

            public interface IThing { void DoIt(); }

            public class Thing : IThing
            {
                public void DoIt() { }
                public void DoIt(int times) { }
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("void DoIt(int times);");
        result.Errors.Should().BeEmpty(result.ErrorText);
    }

    [Fact]
    public async Task TheFixPicksTheReportedOverloadWhenTheFirstIsAlreadyDeclared()
    {
        // The interface has DoIt(int); the class adds DoIt(string). Looking the member up by name found DoIt(int), saw
        // it on the interface, and silently did nothing.
        var result = await CodeFixHarness.ApplyAsync(Analyzer, CodeFix, """
            namespace Demo;

            public interface IThing { void DoIt(int times); }

            public class Thing : IThing
            {
                public void DoIt(int times) { }
                public void DoIt(string label) { }
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("void DoIt(string label);");
        result.Errors.Should().BeEmpty(result.ErrorText);
    }

    [Theory]
    [InlineData("void Echo<T>(T value);", "public void Echo<U>(U value) { }")]
    [InlineData("void Fill(ref int value);", "public void Fill(ref int value) { }")]
    [InlineData("void Take(System.Collections.Generic.List<int> values);", "public void Take(System.Collections.Generic.List<int> values) { }")]
    public async Task AMatchingSignatureIsNotReported(string interfaceMember, string classMember)
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync(Analyzer, [$$"""
            namespace Demo;

            public interface IThing { {{interfaceMember}} }

            public class Thing : IThing
            {
                {{classMember}}
            }
            """]);

        diagnostics.Where(d => d.Id == Id).Should().BeEmpty();
    }

    [Fact]
    public async Task AGenericInterfaceMemberMatchesThroughItsTypeArgument()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync(Analyzer, ["""
            namespace Demo;

            public interface IStore<T> { void Save(T item); }

            public class Store : IStore<string>
            {
                public void Save(string item) { }
                public void Save(int item) { }
            }
            """]);

        diagnostics.Where(d => d.Id == Id).Should().ContainSingle()
            .Which.Location.GetLineSpan().StartLinePosition.Line.Should().Be(7);
    }

    #endregion
}
