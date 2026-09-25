using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// <c>context.ReportDiagnostics(...)</c> must cost nothing per edit when there is nothing to report,
/// and must still report at the right place when there is.
/// </summary>
/// <remarks>
/// <para>
/// The reporter needs the <c>Compilation</c> to turn a stored <c>LocationKey</c> back into an in-tree
/// <see cref="Location"/>, and a <c>Compilation</c> is new on every edit — so a reporter combined with
/// it unconditionally re-ran on every keystroke. Reporters with nothing to report are now filtered out
/// before that combine. These tests pin both halves: no re-run without diagnostics, and a diagnostic
/// that still follows its code when an edit moves it.
/// </para>
/// <para>
/// The location has to stay in-tree, not become a <c>Location.Create(path, …)</c>: <c>#pragma warning</c>
/// and <c>.editorconfig</c> severity are applied per syntax tree, and a location outside the
/// compilation's trees escapes both. The pragma test is the one that would notice.
/// </para>
/// </remarks>
public class ReportDiagnosticsIncrementalityTests
{
    private const string PostConstructRequiresInject = "INJECT004";

    private const string Clean = """
        using Microsoft.Extensions.DependencyInjection;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public static class Leading { public static int Touch() { return 1; } }

        public interface IGreeter { string Greet(); }

        [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
        public class Greeter : IGreeter
        {
            public string Greet() => "hi";
        }
        """;

    /// <summary>INJECT004, a warning: [PostConstruct] in a class with nothing injected.</summary>
    private const string WithWarning = """
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public static class Leading { public static int Touch() { return 1; } }

        public partial class Service
        {
            [PostConstruct]
            private void Init() { }
        }
        """;

    private const string WithSuppressedWarning = """
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public static class Leading { public static int Touch() { return 1; } }

        public partial class Service
        {
        #pragma warning disable INJECT004
            [PostConstruct]
            private void Init() { }
        #pragma warning restore INJECT004
        }
        """;

    /// <summary>Spreads the leading class over more lines, so everything below it moves down.</summary>
    private static string ShiftDown(string text)
        => text.Replace(
            "public static class Leading { public static int Touch() { return 1; } }",
            "public static class Leading\n{\n    public static int Touch()\n    {\n        return 42;\n    }\n}");

    [Fact]
    public void AReporterWithNothingToReport_RunsNoOutputStepAfterAnEdit()
    {
        var run = GeneratorHarness.RunKeystroke("ServiceRegistrationsGenerator", [Clean], 0, ShiftDown);

        // Guards: the generator did emit, and it reported nothing — the case being pinned.
        run.First.GeneratedSources.Should().NotBeEmpty();
        run.Second.Diagnostics.Should().BeEmpty();
        run.OutputReasons.Should().NotBeEmpty("the source output step must be tracked for this to mean anything");

        run.OutputReasons.Should().NotContain(IncrementalStepRunReason.Modified,
            $"with no diagnostics to report, the diagnostics output must not re-run. OutputReasons: [{string.Join(", ", run.OutputReasons)}]");
    }

    [Fact]
    public void ADiagnostic_FollowsItsCode_WhenAnEditMovesIt()
    {
        var run = GeneratorHarness.RunKeystroke("InjectSourceGenerator", [WithWarning], 0, ShiftDown);

        LineOf(Single(run.First)).Should().Be(LineContaining(WithWarning, "void Init"),
            "the first run must report INJECT004 on the [PostConstruct] method");

        // After the edit the method is further down. A diagnostic served from cache would still
        // point at the old line, which is now inside the Leading class.
        var moved = Single(run.Second);
        LineOf(moved).Should().Be(LineContaining(ShiftDown(WithWarning), "void Init"),
            "the diagnostic must move with the method it is about");

        moved.Location.IsInSource.Should().BeTrue("an in-tree location is what #pragma and .editorconfig apply to");
        moved.Location.SourceTree!.GetText().ToString().Should().Be(ShiftDown(WithWarning),
            "the location must be in the edited tree, not the one from before the edit");
    }

    [Fact]
    public void APragmaAroundTheCode_SuppressesTheDiagnostic_AfterAnEditMovesIt()
    {
        var run = GeneratorHarness.RunKeystroke("InjectSourceGenerator", [WithSuppressedWarning], 0, ShiftDown);

        // Suppressed means either filtered out by the driver or marked IsSuppressed; both are fine.
        // What must not happen is an active INJECT004 escaping the pragma.
        run.Second.Diagnostics
            .Where(d => d.Id == PostConstructRequiresInject && !d.IsSuppressed)
            .Should().BeEmpty("#pragma warning disable INJECT004 surrounds the method");

        // Guard: without the pragma the same code does report (see the test above), so an empty
        // result here is the pragma's doing, not a reporter that went quiet.
        var unsuppressed = GeneratorHarness.RunKeystroke("InjectSourceGenerator", [WithWarning], 0, ShiftDown);
        unsuppressed.Second.Diagnostics.Where(d => d.Id == PostConstructRequiresInject).Should().NotBeEmpty();
    }

    private static Diagnostic Single(GeneratorRunResult result)
    {
        var matches = result.Diagnostics.Where(d => d.Id == PostConstructRequiresInject).ToList();
        matches.Should().HaveCount(1, $"exactly one {PostConstructRequiresInject} is expected");
        return matches[0];
    }

    private static int LineOf(Diagnostic diagnostic) => diagnostic.Location.GetLineSpan().StartLinePosition.Line;

    private static int LineContaining(string text, string fragment)
        => text.Split('\n').Select((line, i) => (line, i)).Single(l => l.line.Contains(fragment)).i;
}
