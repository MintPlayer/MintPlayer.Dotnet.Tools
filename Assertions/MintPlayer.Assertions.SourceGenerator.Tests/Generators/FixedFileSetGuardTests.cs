namespace MintPlayer.Assertions.SourceGenerator.Tests.Generators;

/// <summary>
/// Every generator emits a fixed set of files: the set of hint names must not depend on how many inputs there are,
/// nor on what they are called.
/// </summary>
/// <remarks>
/// The counterpart of <c>MintPlayer.SourceGenerators.Tests.Guards.FixedFileSetGuardTests</c> for the generators
/// that ship inside MintPlayer.Assertions (PRD-EqualitySingleFile D4). A hint name derived from a type grows without
/// bound, and past 255 characters a build with <c>EmitCompilerGeneratedFiles</c> fails with <c>CS0016</c>. Each
/// generator runs over one decorated input and over five, each in a namespace of its own, and the two sets of hint
/// names must be equal. That guard's negative control (a per-type generator it must reject) lives beside it.
/// </remarks>
public class FixedFileSetGuardTests
{
    /// <summary><see cref="Item"/> is repeated with <c>{i}</c> replaced by 1 to n, each copy in its own namespace.</summary>
    public sealed record Case(string Generator, string Item)
    {
        public string Corpus(int count)
            => "using MintPlayer.Assertions;" + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine,
                Enumerable.Range(1, count).Select(i =>
                    $"namespace Demo.N{i}{Environment.NewLine}{{{Environment.NewLine}{Item.Replace("{i}", i.ToString())}{Environment.NewLine}}}"));

        public override string ToString() => Generator;
    }

    private static readonly Case[] Cases =
    [
        new("GenerateAssertionGenerator", """
            public static class Item{i}Predicates
            {
                [GenerateAssertion]
                public static bool IsItem{i}(int value) => value == {i};
            }
            """),

        new("EquivalencyRegistrationGenerator", """
            [AssertEquivalency]
            public class Item{i}
            {
                public string Name { get; set; } = "";
            }
            """),
    ];

    public static TheoryData<Case> AllCases()
    {
        var data = new TheoryData<Case>();
        foreach (var c in Cases) data.Add(c);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void EveryGenerator_EmitsTheSameFiles_ForOneInputAndForFive(Case c)
    {
        var one = Harness.Instance.RunGenerator(c.Generator, c.Corpus(1));
        var five = Harness.Instance.RunGenerator(c.Generator, c.Corpus(5));

        one.GeneratedSources.Should().NotBeEmpty($"the {c.Generator} corpus must make the generator emit something");
        foreach (var i in Enumerable.Range(1, 5))
            five.AllSources.Should().Contain($"Item{i}", $"{c.Generator} must have seen all five inputs for the comparison to mean anything");

        five.GeneratedSources.Select(s => s.HintName).ToList().Should().BeEquivalentTo(
            one.GeneratedSources.Select(s => s.HintName).ToList(), because:
            $"{c.Generator} must emit a fixed set of files; a hint name derived from a type grows without bound");
    }

    /// <summary>Every generator in the assembly must have a case, so a new one cannot skip the guard.</summary>
    [Fact]
    public void EveryGenerator_HasAFixedFileSetCase()
    {
        var generators = Harness.Instance.GeneratorTypes();
        generators.Should().NotBeEmpty("the harness must find the generators, or this guard guards nothing");

        var covered = Cases.Select(c => c.Generator).ToHashSet(StringComparer.Ordinal);
        var missing = generators.Select(g => g.Name).Where(name => !covered.Contains(name)).ToList();

        missing.Should().BeEmpty($"every generator needs a corpus in {nameof(FixedFileSetGuardTests)}.{nameof(Cases)}. Missing: {string.Join(", ", missing)}");
    }
}
