using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.SourceGenerator.Tests.Generators;

/// <summary>
/// After an edit a generator does not care about — a method body changes — every output step of
/// both assertion generators must be served from the driver's cache.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>MintPlayer.SourceGenerators.Tests.Generators.IncrementalOutputCachingTests</c>
/// for the generators that ship inside MintPlayer.Assertions. Identical output after a second run
/// proves nothing: a pipeline that recomputes everything on every keystroke writes the same text.
/// Only the output step's run reason tells the two apart, so that is what is asserted —
/// <see cref="IncrementalGeneratorResult.OutputsFullyCached"/>, which is false when there are no
/// output steps at all, so a generator that stopped registering output cannot pass vacuously.
/// </para>
/// <para>
/// Two edit positions per generator. <em>Trailing</em> edits a body after every declaration the
/// generator reads, so nothing moves. <em>Leading</em> grows a body before them by two lines, so
/// every declaration moves down: a model that carries a location (<c>AssertionMethodDeclaration</c>
/// keeps a line-based <c>LocationKey</c> for its diagnostic) changes even though nothing the
/// generator emits depends on it. Pressing Enter inside a method is the most common edit there is.
/// </para>
/// </remarks>
public class IncrementalOutputCachingTests
{
    private const string BodyToken = "__BODY__";

    public sealed record Case(string Generator, string Position, string Fixture)
    {
        // The edited body grows by two lines, so under a Leading edit everything after it moves
        // down. Changing only the length of a line would not be enough: LocationKey is line-based.
        public string Before => Fixture.Replace(BodyToken, "1");
        public string After => Fixture.Replace(BodyToken, "1\n            + 2\n            + 3");

        public override string ToString() => $"{Generator} ({Position})";
    }

    private const string Predicate = """
            [GenerateAssertion]
            public static bool IsEven(int value) => value % 2 == 0;
        """;

    private const string Equivalency = """
        public class Person
        {
            public string Name { get; set; } = "";
            public int Age { get; set; }
        }

        public class Test
        {
            public void Run(Person actual, Person expected)
                => actual.Should().BeEquivalentTo(expected);
        }
        """;

    private static readonly Case[] Cases =
    [
        new("GenerateAssertionGenerator", "Trailing", $$"""
            using MintPlayer.Assertions;

            namespace Demo;

            public static class Predicates
            {
            {{Predicate}}

                public static int Touch() => {{BodyToken}};
            }
            """),

        new("GenerateAssertionGenerator", "Leading", $$"""
            using MintPlayer.Assertions;

            namespace Demo;

            public static class Predicates
            {
                public static int Touch() => {{BodyToken}};

            {{Predicate}}
            }
            """),

        new("EquivalencyRegistrationGenerator", "Trailing", $$"""
            using MintPlayer.Assertions;

            namespace Demo;

            {{Equivalency}}

            public static class Unrelated
            {
                public static int Touch() => {{BodyToken}};
            }
            """),

        new("EquivalencyRegistrationGenerator", "Leading", $$"""
            using MintPlayer.Assertions;

            namespace Demo;

            public static class Unrelated
            {
                public static int Touch() => {{BodyToken}};
            }

            {{Equivalency}}
            """),
    ];

    public static TheoryData<Case> AllGenerators()
    {
        var data = new TheoryData<Case>();
        foreach (var c in Cases) data.Add(c);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllGenerators))]
    public void AMethodBodyEdit_IsServedFromCache(Case c)
    {
        var run = Harness.Instance.RunGeneratorTwice(c.Generator, [c.Before], [c.After]);

        // Guard: a generator that emits nothing, or registers no output, is vacuously "cached".
        run.First.GeneratedSources.Should().NotBeEmpty(
            $"the {c.Generator} fixture must make the generator emit something for this test to mean anything");
        run.OutputReasons.Should().NotBeEmpty(
            "trackIncrementalGeneratorSteps must be on and the generator must register output");

        // The edit changes nothing the generator reads, so the output must be byte-identical.
        run.OutputUnchanged.Should().BeTrue("a method-body edit must not change the generated sources");

        run.OutputsFullyCached.Should().BeTrue(
            $"a method-body edit must not re-run any output step of {c.Generator}. "
            + $"OutputReasons: [{string.Join(", ", run.OutputReasons)}]. "
            + $"TrackedOutputSteps: {string.Join(", ", run.Second.TrackedOutputSteps.Keys)}");
    }
}
