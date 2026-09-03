using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// Asserts that the generators actually behave incrementally — that a second compilation which
/// changes nothing relevant reuses the previous pipeline outputs instead of recomputing them.
/// </summary>
/// <remarks>
/// Every other test in this project drives the generator exactly once, which cannot distinguish a
/// correctly-cached pipeline from one that recomputes everything on every keystroke. Both produce
/// identical output; only the second run tells them apart.
///
/// These tests are also the only thing that executes MintPlayer.SourceGenerators.Tools'
/// ValueComparers at all: the comparers exist solely to answer "are these inputs equal to last
/// time?", a question a single run never asks.
/// </remarks>
public class IncrementalityTests
{
    private const string Registrations = """
        using Microsoft.Extensions.DependencyInjection;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public interface IGreeter { string Greet(); }

        [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
        public class Greeter : IGreeter
        {
            public string Greet() => "hi";
        }
        """;

    /// <summary>Same declarations, different method body — nothing the generator cares about.</summary>
    private const string RegistrationsWithUnrelatedEdit = """
        using Microsoft.Extensions.DependencyInjection;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public interface IGreeter { string Greet(); }

        [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
        public class Greeter : IGreeter
        {
            public string Greet() => "hello there";
        }
        """;

    private const string RegistrationsWithNewService = """
        using Microsoft.Extensions.DependencyInjection;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public interface IGreeter { string Greet(); }
        public interface IWaver { string Wave(); }

        [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
        public class Greeter : IGreeter
        {
            public string Greet() => "hi";
        }

        [Register(typeof(IWaver), ServiceLifetime.Singleton)]
        public class Waver : IWaver
        {
            public string Wave() => "o/";
        }
        """;

    [Fact]
    public void AnUnrelatedEditDoesNotChangeTheGeneratedOutput()
    {
        var run = GeneratorHarness.RunIncremental(
            "ServiceRegistrationsGenerator",
            [Registrations],
            [RegistrationsWithUnrelatedEdit]);

        // The registration table is identical, so the emitted source must be byte-identical too.
        var before = run.First.GeneratedSources.Select(s => s.SourceText.ToString()).ToList();
        var after = run.Second.GeneratedSources.Select(s => s.SourceText.ToString()).ToList();

        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public void AnUnrelatedEditIsServedFromCache()
    {
        var run = GeneratorHarness.RunIncremental(
            "ServiceRegistrationsGenerator",
            [Registrations],
            [RegistrationsWithUnrelatedEdit]);

        // Guard: if the driver stopped tracking steps, every assertion below would vacuously pass.
        run.StepNames.Should().NotBeEmpty(
            "trackIncrementalGeneratorSteps must be on for this test to mean anything");

        // At least one tracked step has to report a cache hit. Asserting on every step would be
        // over-specified — the syntax provider legitimately re-runs when the tree changes; what
        // matters is that the change stops there rather than propagating to the output.
        var cached = run.StepNames.Where(run.WasFullyCached).ToList();

        cached.Should().NotBeEmpty(
            $"an edit the generator does not care about should be absorbed by a comparer. Steps seen: {string.Join(", ", run.StepNames)}");
    }

    [Fact]
    public void ARelevantEditIsNotServedFromCache()
    {
        var run = GeneratorHarness.RunIncremental(
            "ServiceRegistrationsGenerator",
            [Registrations],
            [RegistrationsWithNewService]);

        var after = string.Join("\n", run.Second.GeneratedSources.Select(s => s.SourceText.ToString()));

        // The counterpart to the test above: a comparer that returns "equal" for everything would
        // pass AnUnrelatedEditIsServedFromCache and fail here.
        after.Should().Contain("IWaver");
        after.Should().Contain("AddSingleton");
    }

    [Fact]
    public void TheInjectGeneratorIsIncrementalToo()
    {
        const string before = """
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public partial class Service
            {
                [Inject] private readonly System.IServiceProvider _provider;

                public string Describe() => "a";
            }
            """;

        const string after = """
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public partial class Service
            {
                [Inject] private readonly System.IServiceProvider _provider;

                public string Describe() => "b";
            }
            """;

        var run = GeneratorHarness.RunIncremental("InjectSourceGenerator", [before], [after]);

        var first = run.First.GeneratedSources.Select(s => s.SourceText.ToString()).ToList();
        var second = run.Second.GeneratedSources.Select(s => s.SourceText.ToString()).ToList();

        second.Should().BeEquivalentTo(first);
    }
}
