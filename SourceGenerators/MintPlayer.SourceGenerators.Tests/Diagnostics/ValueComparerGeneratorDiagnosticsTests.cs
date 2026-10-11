using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tests._Infrastructure;
using MintPlayer.SourceGenerators.Tests.Snapshots;

namespace MintPlayer.SourceGenerators.Tests.Diagnostics;

/// <summary>MINT002 to MINT008, reported by ValueComparerGenerator itself (not by an analyzer).</summary>
public class ValueComparerGeneratorDiagnosticsTests
{
    private static GeneratorRun Run(params string[] sources)
        => GeneratorHarness.Run("ValueComparerGenerator", sources, "Demo", generatorAssemblyName: "MintPlayer.ValueComparerGenerator");

    private static int LineOf(Diagnostic diagnostic) => diagnostic.Location.GetLineSpan().StartLinePosition.Line;

    private const string EqualityFile = "GeneratedEquality.g.cs";

    /// <summary>
    /// One model's declaration in the shared equality file, from its <c>partial</c> line to its closing brace, so an
    /// assertion about one model isn't satisfied, or broken, by another model's members.
    /// </summary>
    private static string ModelBlock(string source, string declaration)
    {
        var lines = source.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(l => l.TrimStart().StartsWith(declaration, StringComparison.Ordinal));
        start.Should().BeGreaterThanOrEqualTo(0, $"the equality file must declare '{declaration}'");

        var close = new string(' ', lines[start].Length - lines[start].TrimStart().Length) + "}";
        var end = lines.FindIndex(start, l => l == close);
        return string.Join("\n", lines.Skip(start).Take(end - start + 1));
    }

    [Fact]
    public void MINT002_NamesEveryMemberTheTypeDeclaresItself_AsInfo()
    {
        var run = Run(EqualityShapes.UserDeclared);

        var infos = run.Of("MINT002").Where(d => d.GetMessage().Contains("'Handmade'")).ToList();
        infos.Should().AllSatisfy(d => d.Severity.Should().Be(DiagnosticSeverity.Info));
        infos.Should().HaveCount(3);
        infos.Should().Contain(d => d.GetMessage().Contains("Equals(Handmade)"));
        infos.Should().Contain(d => d.GetMessage().Contains("Equals(object)"));
        infos.Should().Contain(d => d.GetMessage().Contains("GetHashCode()"));
        infos.Should().AllSatisfy(d => d.Location.IsInSource.Should().BeTrue());

        // IEquatable<T> is still added, and nothing the user wrote is generated a second time.
        var generated = ModelBlock(run.SourceFor(EqualityFile)!, "partial class Handmade");
        generated.Should().Contain("global::System.IEquatable<global::Demo.Handmade>");
        generated.Should().NotContain("bool Equals(");
        generated.Should().NotContain("GetHashCode()");
    }

    [Fact]
    public void MINT008_IsAWarning_WhenOnlyOneOfEqualsObjectAndGetHashCodeIsDeclared()
    {
        // Was a MINT002 warning: Tools' DiagnosticInfo resolves a rule by id, so one id has one severity.
        var run = Run(EqualityShapes.UserDeclared);

        run.Of("MINT002").Should().NotContain(d => d.GetMessage().Contains("'HalfMade'"));
        var warning = run.Of("MINT008").Single(d => d.GetMessage().Contains("'HalfMade'"));
        warning.Severity.Should().Be(DiagnosticSeverity.Warning);
        warning.GetMessage().Should().Contain("GetHashCode()");
        ModelBlock(run.SourceFor(EqualityFile)!, "partial class HalfMade").Should().Contain("public override int GetHashCode()");
        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void MINT003_ADerivedTypeThatIsNotPartial_IsAnError_AndGetsNoMembers()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            public abstract partial class Node { public string Name { get; set; } = ""; }

            public class Leaf : Node { public int Weight { get; set; } }
            """);

        var error = run.Of("MINT003").Single();
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage().Should().Be("'Leaf' derives from [GenerateEquality] type 'Node' and must be partial");
        LineOf(error).Should().Be(7);
        var generated = run.SourceFor(EqualityFile)!;
        generated.Should().Contain("partial class Node");
        generated.Should().NotContain("partial class Leaf");
    }

    [Fact]
    public void MINT003_AnAttributedTypeThatIsNotPartial_IsAnError()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            public sealed class Model { public string Name { get; set; } = ""; }
            """);

        run.Of("MINT003").Single().GetMessage().Should().Be("'Model' is marked [GenerateEquality] and must be partial");
        run.GeneratedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("typeof(string)", "does not implement IEqualityComparer<string>")]
    [InlineData("typeof(NoWayToCreate)", "neither a static Instance member nor a public parameterless constructor")]
    [InlineData("typeof(IntComparer)", "does not implement IEqualityComparer<string>")]
    [InlineData("typeof(AbstractComparer)", "closed, non-abstract")]
    public void MINT004_AnInvalidComparerType_IsAnError(string comparer, string reason)
    {
        var run = Run($$"""
            using System.Collections.Generic;
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            public sealed class NoWayToCreate : IEqualityComparer<string>
            {
                private NoWayToCreate() { }
                public bool Equals(string? x, string? y) => x == y;
                public int GetHashCode(string obj) => 0;
            }

            public sealed class IntComparer : IEqualityComparer<int>
            {
                public bool Equals(int x, int y) => x == y;
                public int GetHashCode(int obj) => obj;
            }

            public abstract class AbstractComparer : IEqualityComparer<string>
            {
                public abstract bool Equals(string? x, string? y);
                public abstract int GetHashCode(string obj);
            }

            [GenerateEquality]
            public sealed partial class Model
            {
                [UseEqualityComparer({{comparer}})] public string Name { get; set; } = "";
            }
            """);

        var error = run.Of("MINT004").Single();
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage().Should().Contain("property 'Name'");
        error.GetMessage().Should().Contain(reason);
        // The property falls back to its ordinary comparison, so the generated file itself still compiles.
        run.UpdatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void MINT005_APropertyWithOnlyReferenceEquality_IsAWarning_AlsoAsACollectionElement()
    {
        var run = Run("""
            using System.Collections.Generic;
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            public class Plain { public string Name { get; set; } = ""; }

            [GenerateEquality]
            public sealed partial class Model
            {
                public Plain? Direct { get; set; }
                public List<Plain> Many { get; set; } = [];
            }
            """);

        var warnings = run.Of("MINT005");
        warnings.Should().HaveCount(2);
        warnings.Should().AllSatisfy(d => d.Severity.Should().Be(DiagnosticSeverity.Warning));
        warnings.Should().Contain(d => d.GetMessage().Contains("Property 'Direct' of 'Model' compares 'Demo.Plain'"));
        warnings.Should().Contain(d => d.GetMessage().Contains("Property 'Many' of 'Model' compares 'Demo.Plain'"));
        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void MINT005_IsNotReportedForTypesThatHaveValueEquality()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            public record Rec(string Name);
            public class Equatable : IEquatable<Equatable> { public bool Equals(Equatable? other) => true; public override bool Equals(object? obj) => true; public override int GetHashCode() => 0; }
            public class Overrides { public override bool Equals(object? obj) => true; public override int GetHashCode() => 0; }
            public class Plain { }
            public sealed class PlainComparer : IEqualityComparer<Plain> { public static readonly PlainComparer Instance = new(); public bool Equals(Plain? x, Plain? y) => true; public int GetHashCode(Plain obj) => 0; }

            [GenerateEquality]
            public sealed partial class Other { }

            public interface IShape { }

            [GenerateEquality]
            public sealed partial class Model
            {
                public string Text { get; set; } = "";
                public object? Anything { get; set; }
                public Rec? Record { get; set; }
                public Equatable? Equatable { get; set; }
                public Overrides? Overrides { get; set; }
                public Other? NestedModel { get; set; }
                public IShape? Interface { get; set; }
                public Uri? Uri { get; set; }
                [UseEqualityComparer(typeof(PlainComparer))] public Plain? WithComparer { get; set; }
                [EqualityIgnore] public Plain? Ignored { get; set; }
            }
            """);

        run.Of("MINT005").Should().BeEmpty();
        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void MINT006_AContainingTypeThatIsNotPartial_IsAnError()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            public class Outer
            {
                [GenerateEquality]
                public partial class Inner { public int X { get; set; } }
            }
            """);

        var error = run.Of("MINT006").Single();
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage().Should().Contain("'Inner' is nested in 'Outer'");
        LineOf(error).Should().Be(7);
        run.GeneratedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("file", "'Model' is file-local")]
    [InlineData("nested", "'Outer' is file-local")]
    public void MINT007_AFileLocalModel_OrOneNestedInAFileType_IsAnError_AndGeneratesNoPhantomPartial(string where, string message)
    {
        // S1: a model nested in a file type used to get a partial in the generated file, which is another type.
        var source = where == "file"
            ? """
              using MintPlayer.ValueComparerGenerator.Attributes;

              namespace Demo;

              [GenerateEquality]
              file sealed partial class Model { public int X { get; set; } }
              """
            : """
              using MintPlayer.ValueComparerGenerator.Attributes;

              namespace Demo;

              file partial class Outer
              {
                  [GenerateEquality]
                  public sealed partial class Model { public int X { get; set; } }
              }
              """;
        var run = Run(source);

        var error = run.Of("MINT007").Single();
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage().Should().StartWith("[GenerateEquality] cannot generate the equality members of 'Model': ").And.Contain(message);
        error.Location.IsInSource.Should().BeTrue();
        run.GeneratedSources.Should().BeEmpty();
        run.Errors.Where(d => d.Id != "MINT007").Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void MINT007_AStaticClass_IsAnError()
    {
        var run = Run("""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            public static partial class Helpers { public static int X { get; set; } }
            """);

        run.Of("MINT007").Single().GetMessage().Should().Contain("'Helpers'").And.Contain("static");
        run.GeneratedSources.Should().BeEmpty();
    }

    /// <summary>W17: a model whose base or property type did not bind is skipped, so the generated file adds no errors.</summary>
    [Theory]
    [InlineData("public sealed partial class Model : MissingBase { public int X { get; set; } }")]
    [InlineData("public sealed partial class Model { public MissingType? X { get; set; } }")]
    [InlineData("public sealed partial class Model { public System.Collections.Generic.List<MissingType> X { get; set; } = new(); }")]
    public void W17_AModelWithAnUnboundType_IsSkipped_SoTheGeneratedCodeAddsNoErrors(string declaration)
    {
        var run = Run($$"""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [GenerateEquality]
            {{declaration}}

            [GenerateEquality]
            public sealed partial class Fine { public int Y { get; set; } }
            """);

        // The user's own CS0246 stays; nothing else, and the valid model still generates.
        run.Errors.Should().AllSatisfy(d => d.Id.Should().Be("CS0246"));
        var generated = run.SourceFor(EqualityFile)!;
        generated.Should().Contain("partial class Fine");
        generated.Should().NotContain("partial class Model");
    }

    private static readonly string[] DiagnosticCaseSources =
    [
        """
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo;

        public class Plain { }

        [GenerateEquality]
        public sealed partial class Model { public Plain? Direct { get; set; } }
        """,
        """
        namespace Demo;

        public class Unrelated { public int Touch() => 1; }
        """,
    ];

    [Fact]
    public void AMovedDiagnostic_IsReportedAtItsNewLine_WithoutRegeneratingTheFile()
    {
        // Only a type with a diagnostic carries a span. The span lives beside the model, not in it, so an edit
        // above the type moves the diagnostic but leaves the producer's input Unchanged.
        var result = GeneratorHarness.RunKeystroke("ValueComparerGenerator", DiagnosticCaseSources, editIndex: 0,
            edit: s => "// a new first line\n" + s, generatorAssemblyName: "MintPlayer.ValueComparerGenerator");

        result.WasFullyCached("ValueComparerGenerator.Models").Should().BeTrue();
        var before = result.First.Diagnostics.Single(d => d.Id == "MINT005");
        var after = result.Second.Diagnostics.Single(d => d.Id == "MINT005");
        LineOf(after).Should().Be(LineOf(before) + 1);
    }

    [Fact]
    public void AnUnrelatedEdit_LeavesTheModelsCached_EvenWithADiagnosticPresent()
    {
        var result = GeneratorHarness.RunKeystroke("ValueComparerGenerator", DiagnosticCaseSources, editIndex: 1,
            edit: s => s.Replace("=> 1", "=> 42"), generatorAssemblyName: "MintPlayer.ValueComparerGenerator");

        // The diagnostics output itself re-runs, by design: a reporter WITH something to report is combined with
        // the compilation to map its spans back into the tree (see GeneratorExtensions.ReportDiagnostics).
        result.WasFullyCached("ValueComparerGenerator.Models").Should().BeTrue();
        result.Second.Diagnostics.Should().Contain(d => d.Id == "MINT005");
    }
}
